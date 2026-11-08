//! Read native menu metadata without opening a menu or dispatching a command.
use crate::server::{Error, Result, integer};
use serde_json::{Value, json};
use std::collections::{HashMap, HashSet};
use windows::{
    Win32::{
        Foundation::HWND,
        UI::WindowsAndMessaging::{
            GetMenu, GetMenuBarInfo, GetMenuItemCount, GetMenuItemInfoW, HMENU, MENUBARINFO,
            MENUITEMINFOW, MFS_DISABLED, MFT_SEPARATOR, MIIM_FTYPE, MIIM_STATE, MIIM_STRING,
            MIIM_SUBMENU, OBJID_CLIENT,
        },
    },
    core::PWSTR,
};

struct Row {
    name: String,
    position: u32,
    occurrence: usize,
    enabled: bool,
    submenu: HMENU,
}

fn menu_error(code: &str, message: &str, context: Value) -> Error {
    Error::new(code, message).context(context)
}

fn normalized(name: &str) -> String {
    name.replace('&', "").trim().to_lowercase()
}

fn rows(menu: HMENU) -> (Vec<Row>, bool) {
    let count = unsafe { GetMenuItemCount(Some(menu)) };
    let mut complete = (0..=5000).contains(&count);
    let mut rows = Vec::new();
    let mut occurrences = HashMap::new();
    for position in 0..count.clamp(0, 5000) as u32 {
        let mut text = [0u16; 1024];
        let mut item = MENUITEMINFOW {
            cbSize: std::mem::size_of::<MENUITEMINFOW>() as u32,
            fMask: MIIM_FTYPE | MIIM_STATE | MIIM_STRING | MIIM_SUBMENU,
            dwTypeData: PWSTR(text.as_mut_ptr()),
            cch: 1023,
            ..Default::default()
        };
        if unsafe { GetMenuItemInfoW(menu, position, true, &mut item) }.is_err() {
            complete = false;
            continue;
        }
        if item.fType.contains(MFT_SEPARATOR) {
            continue;
        }
        let label = String::from_utf16_lossy(
            &text[..text.iter().position(|c| *c == 0).unwrap_or(text.len())],
        );
        let name = label.split('\t').next().unwrap_or("").replace('&', "");
        complete &= !name.trim().is_empty() && item.cch < 1023;
        let occurrence = occurrences.entry(normalized(&name)).or_insert(0usize);
        *occurrence += 1;
        rows.push(Row {
            name,
            position,
            occurrence: *occurrence,
            enabled: item.fState.0 & MFS_DISABLED.0 == 0,
            submenu: item.hSubMenu,
        });
    }
    complete &= unsafe { GetMenuItemCount(Some(menu)) } == count;
    (rows, complete)
}

fn choose(items: &[Row], segment: &Value) -> Result<usize> {
    let name = segment
        .as_str()
        .or_else(|| segment["name"].as_str())
        .unwrap_or("");
    let occurrence = segment["occurrence"].as_u64();
    let matches: Vec<_> = items
        .iter()
        .enumerate()
        .filter(|(_, row)| {
            normalized(&row.name) == normalized(name)
                && occurrence.is_none_or(|number| row.occurrence as u64 == number)
        })
        .map(|(index, _)| index)
        .collect();
    match matches.as_slice() {
        [index] => Ok(*index),
        _ => Err(menu_error(
            if matches.is_empty() {
                "MENU_ITEM_NOT_FOUND"
            } else {
                "MENU_ITEM_AMBIGUOUS"
            },
            "La ruta necesita una etiqueta exacta y su occurrence cuando está duplicada.",
            json!({"requested":segment,"candidates":items.iter().map(|row|
                json!({"name":row.name,"occurrence":row.occurrence,"position":row.position})
            ).collect::<Vec<_>>()}),
        )),
    }
}

struct Reader {
    remaining: usize,
    count: usize,
    complete: bool,
    truncated: bool,
    visited: HashSet<usize>,
}

impl Reader {
    fn level(&mut self, menu: HMENU, depth: u64) -> Vec<Value> {
        if !self.visited.insert(menu.0 as usize) {
            self.complete = false;
            return Vec::new();
        }
        let (items, complete) = rows(menu);
        self.complete &= complete && !items.is_empty();
        let mut output = Vec::new();
        for row in items {
            if self.remaining == 0 {
                self.truncated = true;
                self.complete = false;
                break;
            }
            self.remaining -= 1;
            self.count += 1;
            let mut node = json!({"name":row.name,"position":row.position,
                "occurrence":row.occurrence,"has_submenu":!row.submenu.0.is_null()});
            if !row.enabled {
                node["enabled"] = json!(false);
            }
            if !row.submenu.0.is_null() {
                if depth > 1 && self.remaining > 0 {
                    let children = self.level(row.submenu, depth - 1);
                    if children.is_empty() {
                        node["children_pending"] = json!(true);
                    } else {
                        node["children"] = json!(children);
                    }
                } else {
                    self.truncated = true;
                    self.complete = false;
                    node["children_pending"] = json!(true);
                }
            }
            output.push(node);
        }
        self.visited.remove(&(menu.0 as usize));
        output
    }
}

pub fn inspect(id: u64, options: &Value) -> Result<Option<Value>> {
    let (max_depth, max_items) = limits(options)?;
    let hwnd = HWND(id as usize as *mut _);
    let mut menu = unsafe { GetMenu(hwnd) };
    if menu.0.is_null() {
        let mut bar = MENUBARINFO {
            cbSize: std::mem::size_of::<MENUBARINFO>() as u32,
            ..Default::default()
        };
        if unsafe { GetMenuBarInfo(hwnd, OBJID_CLIENT, 0, &mut bar) }.is_ok() {
            menu = bar.hMenu;
        }
    }
    if menu.0.is_null() || unsafe { GetMenuItemCount(Some(menu)) } <= 0 {
        return Ok(None);
    }
    for segment in options["path"].as_array().into_iter().flatten() {
        let (items, complete) = rows(menu);
        if !complete {
            return Err(menu_error(
                "MENU_SEARCH_INCOMPLETE",
                "El menú nativo no expone todas sus etiquetas.",
                json!({"path":options["path"]}),
            ));
        }
        let row = &items[choose(&items, segment)?];
        if row.submenu.0.is_null() {
            return Err(menu_error(
                "MENU_STATE_UNVERIFIED",
                "La ruta termina en una opción sin submenú.",
                json!({"requested":segment}),
            ));
        }
        menu = row.submenu;
    }
    let mut reader = Reader {
        remaining: max_items,
        count: 0,
        complete: true,
        truncated: false,
        visited: HashSet::new(),
    };
    let items = reader.level(menu, max_depth);
    Ok(Some(
        json!({"source":"win32","read_only":true,"path":options.get("path").cloned().unwrap_or(json!([])),
        "complete":reader.complete,"truncated":reader.truncated,"node_count":reader.count,"items":items}),
    ))
}

fn limits(options: &Value) -> Result<(u64, usize)> {
    let path_valid = options.get("path").is_none_or(|path| {
        path.as_array().is_some_and(|parts| {
            parts.len() <= 12
                && parts.iter().all(|part| {
                    let valid_name = |name: &str| !name.trim().is_empty() && name.len() <= 4096;
                    part.as_str().is_some_and(valid_name)
                        || part.as_object().is_some_and(|segment| {
                            segment.len() == 2
                                && part["name"].as_str().is_some_and(valid_name)
                                && part["occurrence"]
                                    .as_u64()
                                    .is_some_and(|n| (1..=5000).contains(&n))
                        })
                })
        })
    });
    if !options.is_object() || !path_valid {
        return Err(Error::new(
            "INVALID_PARAMS",
            "Use a menu object and a bounded path of labels or name/occurrence pairs.",
        ));
    }
    let depth = integer(options, "max_depth", 4, 1, 12)? as u64;
    let items = integer(options, "max_items", 500, 1, 1000)? as usize;
    Ok((depth, items))
}

pub fn snapshot(data: &Value) -> Result<Value> {
    let hwnd = data["hwnd"].as_i64().unwrap_or(0) as isize;
    crate::native::validate_target(hwnd)?;
    let options = &data["menu"];
    if let Some(native) = inspect(hwnd as u64, options)? {
        return Ok(native);
    }
    let (_, max_items) = limits(options)?;
    let ui = crate::accessibility::snapshot(&json!({"hwnd":hwnd,"max_elements":500}))?;
    // UIA_Menu, UIA_MenuBar and UIA_MenuItem are a partial view of the visible menu.
    let mut visible = ui["elements"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|element| {
            matches!(element["control_type_id"].as_i64(), Some(50009..=50011))
                && element["offscreen"] == false
                && element["is_password"] == false
        });
    let items: Vec<_> = visible
        .by_ref()
        .take(max_items)
        .map(|element| {
            json!({"name":element["name"],"enabled":element["enabled"],
            "control_type_id":element["control_type_id"]})
        })
        .collect();
    Ok(
        json!({"source":"accessibility_visible","visible_only":true,"read_only":true,
        "path_resolved":false,"complete":false,"truncated":visible.next().is_some(),
        "node_count":items.len(),"items":items}),
    )
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn duplicate_names_require_an_occurrence() {
        use windows::Win32::UI::WindowsAndMessaging::{
            AppendMenuW, CreateMenu, DestroyMenu, MF_GRAYED, MF_STRING,
        };
        use windows::core::w;

        struct TestMenu(HMENU);
        impl Drop for TestMenu {
            fn drop(&mut self) {
                let _ = unsafe { DestroyMenu(self.0) };
            }
        }
        let menu = TestMenu(unsafe { CreateMenu().unwrap() });
        unsafe {
            AppendMenuW(menu.0, MF_STRING, 1, w!("&Reto90")).unwrap();
            AppendMenuW(menu.0, MF_STRING | MF_GRAYED, 2, w!("Reto90")).unwrap();
        }
        let (items, complete) = rows(menu.0);
        assert!(complete);
        assert!(choose(&items, &json!("Reto90")).is_err());
        assert_eq!(
            choose(&items, &json!({"name":"Reto90","occurrence":2})).unwrap(),
            1
        );
        assert!(!items[1].enabled);
        let mut reader = Reader {
            remaining: 1,
            count: 0,
            complete: true,
            truncated: false,
            visited: HashSet::new(),
        };
        assert_eq!(reader.level(menu.0, 1).len(), 1);
        assert!(reader.truncated);
        assert!(!reader.complete);
    }
}
