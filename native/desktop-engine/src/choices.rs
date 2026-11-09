use crate::server::{Error, Result};
use serde_json::{Value, json};
use windows::{
    Win32::{System::Variant::VARIANT, UI::Accessibility::*},
    core::BSTR,
};

fn ambiguous() -> Error {
    Error::new(
        "OPTION_AMBIGUOUS",
        "Hay varias opciones con esa etiqueta dentro del control.",
    )
}

fn incomplete() -> Error {
    Error::new(
        "OPTION_SEARCH_INCOMPLETE",
        "El proveedor no permitió verificar la lista de coincidencias.",
    )
}

unsafe fn belongs_to(
    automation: &IUIAutomation,
    walker: &IUIAutomationTreeWalker,
    item: &IUIAutomationElement,
    container: &IUIAutomationElement,
    combo: bool,
) -> bool {
    unsafe {
        let mut current = item.clone();
        let mut passed_list = false;
        for _ in 0..16 {
            let Ok(parent) = walker.GetParentElement(&current) else {
                return false;
            };
            if automation
                .CompareElements(&parent, container)
                .is_ok_and(|same| same.as_bool())
            {
                return true;
            }
            match parent.CurrentControlType().ok() {
                Some(kind) if kind == UIA_ComboBoxControlTypeId => return false,
                Some(kind) if kind == UIA_ListControlTypeId => {
                    if !combo || passed_list {
                        return false;
                    }
                    passed_list = true;
                }
                _ => {}
            }
            current = parent;
        }
        false
    }
}

fn option_type(kind: Option<UIA_CONTROLTYPE_ID>) -> bool {
    kind.is_some_and(|kind| {
        [
            UIA_ListItemControlTypeId,
            UIA_MenuItemControlTypeId,
            UIA_DataItemControlTypeId,
        ]
        .contains(&kind)
    })
}

/// A named lookup only searches the previously observed control, never the desktop.
pub(crate) unsafe fn select(
    automation: &IUIAutomation,
    walker: &IUIAutomationTreeWalker,
    container: &IUIAutomationElement,
    label: &str,
    validate: impl Fn() -> Result<()>,
) -> Result<Value> {
    unsafe {
        let combo = match container.CurrentControlType().ok() {
            Some(kind) if kind == UIA_ComboBoxControlTypeId => true,
            Some(kind) if kind == UIA_ListControlTypeId => false,
            _ => {
                return Err(Error::new(
                    "PATTERN_UNSUPPORTED",
                    "option requiere un ComboBox o List.",
                ));
            }
        };
        let pid = container.CurrentProcessId().map_err(|_| incomplete())?;
        let value = VARIANT::from(BSTR::from(label));
        let condition = automation
            .CreatePropertyCondition(UIA_NamePropertyId, &value)
            .map_err(|_| incomplete())?;
        let find_exposed = || -> Result<Option<IUIAutomationElement>> {
            let rows = container
                .FindAll(TreeScope_Descendants, &condition)
                .map_err(|_| incomplete())?;
            let count = rows.Length().map_err(|_| incomplete())?;
            if count > 256 {
                return Err(incomplete());
            }
            let mut found = None;
            for index in 0..count {
                let item = rows.GetElement(index).map_err(|_| incomplete())?;
                if item.CurrentProcessId().ok() != Some(pid)
                    || !option_type(item.CurrentControlType().ok())
                    || !belongs_to(automation, walker, &item, container, combo)
                {
                    continue;
                }
                if found.replace(item).is_some() {
                    return Err(ambiguous());
                }
            }
            Ok(found)
        };
        let mut found = find_exposed()?;
        if let Ok(items) = container
            .GetCurrentPatternAs::<IUIAutomationItemContainerPattern>(UIA_ItemContainerPatternId)
            && let Ok(first) =
                items.FindItemByProperty(None::<&IUIAutomationElement>, UIA_NamePropertyId, &value)
        {
            match items.FindItemByProperty(&first, UIA_NamePropertyId, &value) {
                Ok(_) => return Err(ambiguous()),
                // The COM wrapper converts a successful null result into E_POINTER.
                Err(e) if e.code().0 as u32 == 0x80004003 => {}
                Err(_) => return Err(incomplete()),
            }
            let item = items
                .FindItemByProperty(None::<&IUIAutomationElement>, UIA_NamePropertyId, &value)
                .map_err(|_| incomplete())?;
            if let Ok(pattern) = item.GetCurrentPatternAs::<IUIAutomationVirtualizedItemPattern>(
                UIA_VirtualizedItemPatternId,
            ) {
                validate()?;
                pattern.Realize().map_err(|_| {
                    Error::new(
                        "UIA_ACTION_FAILED",
                        "No se pudo materializar la opción virtualizada.",
                    )
                })?;
            }
            found = Some(item);
        }

        let mut opened = false;
        if found.is_none()
            && combo
            && let Ok(pattern) = container
                .GetCurrentPatternAs::<IUIAutomationExpandCollapsePattern>(
                    UIA_ExpandCollapsePatternId,
                )
            && pattern.CurrentExpandCollapseState().ok() == Some(ExpandCollapseState_Collapsed)
        {
            validate()?;
            pattern
                .Expand()
                .map_err(|_| Error::new("UIA_ACTION_FAILED", "No se pudo abrir el desplegable."))?;
            opened = true;
            for delay in [0, 50, 100] {
                std::thread::sleep(std::time::Duration::from_millis(delay));
                validate()?;
                found = find_exposed()?;
                if found.is_some() {
                    break;
                }
            }
        }
        let item = found.ok_or_else(|| {
            Error::new(
                "OPTION_NOT_EXPOSED",
                "La opción no apareció por accesibilidad. Su lista puede requerir desplazamiento.",
            )
            .context(json!({"option":label,"opened":opened}))
        })?;
        let check_item = || {
            item.CurrentProcessId().ok() == Some(pid)
                && option_type(item.CurrentControlType().ok())
                && item.CurrentName().is_ok_and(|name| name == label)
                && item.CurrentIsPassword().is_ok_and(|v| !v.as_bool())
                && item.CurrentIsEnabled().is_ok_and(|v| v.as_bool())
                && belongs_to(automation, walker, &item, container, combo)
        };
        if !check_item() {
            return Err(Error::new(
                "ELEMENT_UNAVAILABLE",
                "La opción ya no pertenece a la lista o está deshabilitada.",
            ));
        }
        let offscreen = item.CurrentIsOffscreen().is_ok_and(|v| v.as_bool());
        let selection = item
            .GetCurrentPatternAs::<IUIAutomationSelectionItemPattern>(UIA_SelectionItemPatternId)
            .map_err(|_| {
                Error::new(
                    "PATTERN_UNSUPPORTED",
                    "La opción carece de SelectionItemPattern.",
                )
            })?;
        // Some providers select directly even when the entry is offscreen.
        validate()?;
        selection.Select().map_err(|_| {
            Error::new(
                "UIA_ACTION_FAILED",
                "La selección no pudo confirmarse. Observa antes de reintentar.",
            )
        })?;
        let confirmed = selection.CurrentIsSelected().is_ok_and(|v| v.as_bool());
        Ok(
            json!({"performed":true,"method":"uia_select_option","option":label,
            "confirmed":confirmed,"was_offscreen":offscreen,"opened":opened}),
        )
    }
}
