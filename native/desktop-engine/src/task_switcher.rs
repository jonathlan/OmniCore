//! One bounded selection in the native Windows task switcher.
use crate::{
    input::{TaskSwitcherAction, task_switcher_input},
    server::{Error, Result, Server},
    switcher_ocr, windowing,
};
use serde_json::Value;
#[cfg(test)]
use serde_json::json;
use std::{
    os::windows::ffi::OsStringExt,
    path::PathBuf,
    time::{Duration, Instant},
};
use windows::Win32::{
    Foundation::{CloseHandle, HWND},
    System::Threading::{
        OpenProcess, PROCESS_NAME_WIN32, PROCESS_QUERY_LIMITED_INFORMATION,
        QueryFullProcessImageNameW,
    },
    UI::{
        Input::KeyboardAndMouse::{GetLastInputInfo, LASTINPUTINFO},
        WindowsAndMessaging::{GetForegroundWindow, GetWindowThreadProcessId, IsWindowVisible},
    },
};
use windows::core::PWSTR;

#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetSystemWindowsDirectoryW(path: *mut u16, capacity: u32) -> u32;
}

pub fn foreground() -> u64 {
    unsafe { GetForegroundWindow() }.0 as usize as u64
}

pub(crate) fn shell_switcher(id: u64) -> bool {
    let hwnd = HWND(id as usize as *mut _);
    if id == 0
        || !unsafe { IsWindowVisible(hwnd) }.as_bool()
        || !matches!(
            windowing::class(id as isize).as_str(),
            "MultitaskingViewFrame" | "XamlExplorerHostIslandWindow"
        )
    {
        return false;
    }
    unsafe {
        let mut owner = 0;
        GetWindowThreadProcessId(hwnd, Some(&mut owner));
        let Ok(process) = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, owner) else {
            return false;
        };
        let mut path = [0u16; 1024];
        let mut len = path.len() as u32;
        let query = QueryFullProcessImageNameW(
            process,
            PROCESS_NAME_WIN32,
            PWSTR(path.as_mut_ptr()),
            &mut len,
        );
        let _ = CloseHandle(process);
        if query.is_err() {
            return false;
        }
        let mut directory = [0u16; 1024];
        let size =
            GetSystemWindowsDirectoryW(directory.as_mut_ptr(), directory.len() as u32) as usize;
        if size == 0 || size >= directory.len() {
            return false;
        }
        let explorer =
            PathBuf::from(std::ffi::OsString::from_wide(&directory[..size])).join("explorer.exe");
        explorer
            .to_string_lossy()
            .eq_ignore_ascii_case(&String::from_utf16_lossy(&path[..len as usize]))
    }
}

fn unavailable(reason: &str) -> Error {
    Error::new("TASK_SWITCHER_UNAVAILABLE", reason)
}

pub struct TaskSwitcher(u64);

pub fn input_stamp() -> Result<u32> {
    let mut last = LASTINPUTINFO {
        cbSize: std::mem::size_of::<LASTINPUTINFO>() as u32,
        dwTime: 0,
    };
    if !unsafe { GetLastInputInfo(&mut last) }.as_bool() {
        return Err(unavailable(
            "No se pudo comprobar la actividad de entrada de la sesión.",
        ));
    }
    Ok(last.dwTime)
}

impl TaskSwitcher {
    pub fn open(origin: u64) -> Result<Self> {
        if origin == 0 || foreground() != origin || shell_switcher(origin) {
            return Err(unavailable(
                "Cambió el foco o el selector ya estaba abierto.",
            ));
        }
        if windowing::gui(origin as isize)["menu_mode"] == true {
            return Err(unavailable("Hay un menú activo en la ventana de origen."));
        }
        task_switcher_input(origin as isize, TaskSwitcherAction::Open)?;
        let deadline = Instant::now() + Duration::from_millis(1200);
        loop {
            crate::indicator::check()?;
            let current = foreground();
            if current != origin && shell_switcher(current) {
                return Ok(Self(current));
            }
            if Instant::now() >= deadline {
                return Err(unavailable(
                    "Windows no abrió un selector de tareas reconocido.",
                ));
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }

    pub fn hwnd(&self) -> u64 {
        self.0
    }

    pub fn card(&self, engine: &Server, title: &str) -> Result<Option<(i32, i32)>> {
        if foreground() != self.0 {
            return Err(unavailable("El selector perdió el primer plano."));
        }
        let state = engine.inspect_accessibility(self.0 as isize, 500)?;
        if state["elements"]
            .as_array()
            .into_iter()
            .flatten()
            .filter(|row| named_card(row, title))
            .take(2)
            .count()
            > 1
        {
            return Err(unavailable(
                "El selector contiene más de una tarjeta con el mismo título.",
            ));
        }
        Ok(card_point(&state, title))
    }

    pub fn select(&self, point: (i32, i32), target: u64, expected_input: u32) -> Result<()> {
        if foreground() != self.0 || !shell_switcher(self.0) {
            return Err(unavailable(
                "El selector cambió antes de elegir la tarjeta.",
            ));
        }
        if input_stamp()? != expected_input {
            return Err(Error::new(
                "INPUT_BUSY",
                "Hubo entrada durante la observación del selector.",
            ));
        }
        task_switcher_input(
            self.0 as isize,
            TaskSwitcherAction::Select(point.0, point.1),
        )?;
        let deadline = Instant::now() + Duration::from_millis(1200);
        loop {
            crate::indicator::check()?;
            let current = foreground();
            if current == target {
                return Ok(());
            }
            if (current != 0 && current != self.0) || Instant::now() >= deadline {
                return Err(unavailable(
                    "La ventana seleccionada no quedó en primer plano.",
                ));
            }
            std::thread::sleep(Duration::from_millis(20));
        }
    }
}

impl Drop for TaskSwitcher {
    fn drop(&mut self) {
        if foreground() == self.0 && shell_switcher(self.0) {
            crate::indicator::dismiss_switcher(self.0 as isize);
        }
    }
}

fn card_point(state: &Value, title: &str) -> Option<(i32, i32)> {
    if state["truncated"] != false {
        return None;
    }
    let rows = state["elements"].as_array()?;
    let mut cards = rows.iter().filter(|row| named_card(row, title));
    let card = cards.next()?;
    if cards.next().is_some() || card["offscreen"] != false || card["enabled"] != true {
        return None;
    }
    let bounds = &card["bounds"];
    let [Some(x), Some(y), Some(w), Some(h)] =
        ["x", "y", "width", "height"].map(|key| bounds[key].as_i64())
    else {
        return None;
    };
    // Stay within the thumbnail body, away from the close button at the top.
    if w < 24 || h < 24 {
        return None;
    }
    Some((
        i32::try_from(x + w / 2).ok()?,
        i32::try_from(y + h / 2).ok()?,
    ))
}

fn named_card(row: &Value, title: &str) -> bool {
    matches!(row["control_type_id"].as_i64(), Some(50007 | 50019 | 50029))
        && row["name"].as_str() == Some(title)
}

pub fn visual_card(state: &Value, title: &str, windows: &[Value]) -> Result<(i32, i32)> {
    let shot = &state["screenshots"][0];
    let origin = || Some((shot["originX"].as_i64()?, shot["originY"].as_i64()?));
    let (left, top) =
        origin().ok_or_else(|| unavailable("La captura del selector no tiene origen."))?;
    let result = switcher_ocr::read(state)?;
    let title = switcher_ocr::normalize(title);
    let other_titles: Vec<_> = windows
        .iter()
        .filter_map(|window| window["title"].as_str())
        .map(switcher_ocr::normalize)
        .collect();
    let candidates = result["candidates"].as_array().into_iter().flatten();
    let mut matches = Vec::new();
    for candidate in candidates {
        let Some(words) = candidate["words"].as_array() else {
            continue;
        };
        // The icon immediately before a card label may appear as one OCR word.
        for start in 0..words.len().min(2) {
            let text = words[start..]
                .iter()
                .filter_map(|word| word["text"].as_str())
                .collect::<Vec<_>>()
                .join(" ");
            if !visual_title_matches(&text, &title, &other_titles) {
                continue;
            }
            let bounds = &words[start]["bounds"];
            if let (Some(x), Some(y), Some(w), Some(h)) = (
                bounds["x"].as_i64(),
                bounds["y"].as_i64(),
                bounds["width"].as_i64(),
                bounds["height"].as_i64(),
            ) && h >= 9
                && w > 0
                && let (Ok(x), Ok(y)) = (
                    i32::try_from(left + x + w / 2),
                    i32::try_from(top + y + h / 2),
                )
            {
                matches.push((x, y));
            }
        }
    }
    match matches.as_slice() {
        [point] => Ok(*point),
        _ => Err(unavailable(
            "El OCR no encontró un rótulo único y verificable para la ventana en el selector.",
        )),
    }
}

fn visual_title_matches(text: &str, title: &str, windows: &[String]) -> bool {
    let text = switcher_ocr::normalize(text);
    if text == title {
        return true;
    }
    let Some(prefix) = text.strip_suffix("...").or_else(|| text.strip_suffix('…')) else {
        return false;
    };
    let prefix = prefix.trim_end();
    prefix.chars().count() >= 10
        && title.starts_with(prefix)
        && windows
            .iter()
            .filter(|title| title.starts_with(prefix))
            .count()
            == 1
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn accepts_only_one_exact_visible_enabled_card() {
        let card = json!({"name":"Corbiz (QA)","control_type_id":50007,"offscreen":false,
            "enabled":true,"bounds":{"x":-100,"y":100,"width":90,"height":70}});
        let state = |rows| json!({"elements":rows,"truncated":false});
        assert_eq!(
            card_point(&state(vec![card.clone()]), "Corbiz (QA)"),
            Some((-55, 135))
        );
        assert_eq!(card_point(&state(vec![card.clone()]), "Corbiz"), None);
        assert_eq!(
            card_point(&state(vec![card.clone(), card.clone()]), "Corbiz (QA)"),
            None
        );
        let mut button = card;
        button["control_type_id"] = json!(50000);
        assert_eq!(card_point(&state(vec![button]), "Corbiz (QA)"), None);
    }

    #[test]
    fn visual_title_requires_exact_text_or_a_unique_explicit_truncation() {
        let title = "corbiz qa ventas";
        let windows = vec![title.to_owned(), "corbiz prod ventas".to_owned()];
        assert!(visual_title_matches("Corbiz QA Ventas", title, &windows));
        assert!(visual_title_matches("Corbiz QA Ve...", title, &windows));
        assert!(!visual_title_matches("Corbiz QA Ve", title, &windows));
        assert!(!visual_title_matches(
            "Corbiz QA Ve...",
            title,
            &[title.to_owned(), "corbiz qa verificación".to_owned()]
        ));
    }
}
