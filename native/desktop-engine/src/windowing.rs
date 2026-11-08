//! Window-family and menu metadata; no input or focus changes.
use crate::{
    native::{self, Handle, Rect},
    server::{Error, Result},
};
use serde_json::{Value, json};
#[repr(C)]
#[derive(Default)]
struct GuiInfo {
    size: u32,
    flags: u32,
    active: Handle,
    focus: Handle,
    capture: Handle,
    menu_owner: Handle,
    move_size: Handle,
    caret: Handle,
    rect: Rect,
}
#[link(name = "user32")]
unsafe extern "system" {
    fn GetGUIThreadInfo(thread: u32, info: *mut GuiInfo) -> i32;
    fn GetWindow(h: Handle, cmd: u32) -> Handle;
    fn GetTopWindow(h: Handle) -> Handle;
    fn IsWindowEnabled(h: Handle) -> i32;
    fn GetWindowLongPtrW(h: Handle, index: i32) -> isize;
    fn GetClassNameW(h: Handle, text: *mut u16, n: i32) -> i32;
    fn FindWindowExW(parent: Handle, after: Handle, class: *const u16, title: *const u16)
    -> Handle;
}
pub fn gui(h: Handle) -> Value {
    let mut g = GuiInfo {
        size: std::mem::size_of::<GuiInfo>() as u32,
        ..Default::default()
    };
    let thread = unsafe { native::GetWindowThreadProcessId(h, std::ptr::null_mut()) };
    if thread == 0 || unsafe { GetGUIThreadInfo(thread, &mut g) } == 0 {
        return json!({"menu_mode":false});
    }
    json!({"menu_mode":g.flags & 0x1c !=0,"menu_owner":g.menu_owner,"focus_hwnd":g.focus,"active_hwnd":g.active})
}
pub fn class(h: Handle) -> String {
    let mut b = [0u16; 256];
    let n = unsafe { GetClassNameW(h, b.as_mut_ptr(), 256) };
    String::from_utf16_lossy(&b[..n.max(0) as usize])
}
pub fn root_owner(h: Handle) -> Handle {
    let mut start = h;
    if class(h) == "#32768" {
        let owner = gui(h)["menu_owner"].as_i64().unwrap_or(0) as Handle;
        if owner != 0 && native::pid(owner) == native::pid(h) {
            start = owner;
        }
    }
    let mut usable = start;
    let mut current = start;
    if matches!(class(h).as_str(), "#32770" | "ProAlertBox") {
        let raw = unsafe { native::GetAncestor(h, 3) };
        if raw != h && raw != 0 {
            let disabled: Vec<_> = native::windows(false)
                .into_iter()
                .filter(|candidate| {
                    *candidate != h
                        && native::pid(*candidate) == native::pid(h)
                        && !enabled(*candidate)
                        && unsafe { native::GetAncestor(*candidate, 3) } == raw
                        && native::rect(*candidate)
                            .is_ok_and(|r| r.right > r.left && r.bottom > r.top)
                })
                .collect();
            if disabled.len() == 1 {
                return disabled[0];
            }
        }
    }

    for _ in 0..32 {
        let owner = unsafe { GetWindow(current, 4) };
        if owner == 0 || owner == current || native::pid(owner) != native::pid(h) {
            break;
        }
        if unsafe { native::IsWindowVisible(owner) } != 0
            && native::rect(owner).is_ok_and(|r| r.right > r.left && r.bottom > r.top)
        {
            usable = owner;
        }
        current = owner;
    }
    usable
}
pub fn exists(h: Handle) -> bool {
    unsafe { native::IsWindow(h) != 0 }
}
pub fn enabled(h: Handle) -> bool {
    unsafe { IsWindowEnabled(h) != 0 }
}
pub fn popup(h: Handle) -> bool {
    let cls = class(h);
    if cls == "#32768" || cls == "tooltips_class32" {
        return true;
    }
    if matches!(cls.as_str(), "#32770" | "ProAlertBox") {
        return false;
    }
    let style = unsafe { GetWindowLongPtrW(h, -16) };
    let extended = unsafe { GetWindowLongPtrW(h, -20) };
    (extended & 0x08000000 != 0)
        || (style as u64 & 0x80000000 != 0
            && style & 0x00c00000 == 0
            && unsafe { GetWindow(h, 4) } != 0)
}
pub fn family(a: Handle, b: Handle) -> bool {
    a != 0
        && b != 0
        && native::pid(a) != 0
        && native::pid(a) == native::pid(b)
        && (root_owner(a) == root_owner(b)
            || unsafe {
                native::GetAncestor(a, 3) != 0
                    && native::GetAncestor(a, 3) == native::GetAncestor(b, 3)
            })
}
pub fn focus_matches(h: Handle, owned: bool) -> bool {
    let foreground = unsafe { native::GetForegroundWindow() };
    foreground == h || (owned && popup(h) && family(h, foreground) && enabled(h))
}
pub fn point_matches(h: Handle, x: i32, y: i32, owned: bool) -> bool {
    let hit = unsafe { native::GetAncestor(native::WindowFromPoint(native::Point { x, y }), 2) };
    hit == h
        || (owned
            && popup(hit)
            && family(h, hit)
            && family(h, unsafe { native::GetForegroundWindow() }))
}
pub fn enrich(h: Handle, w: &mut Value) {
    let owner = unsafe { GetWindow(h, 4) };
    let root = root_owner(h);
    let cls = w["class_name"].as_str().unwrap_or("");
    let kind = match cls {
        "#32768" => "menu",
        "#32770" | "ProAlertBox" => "dialog",
        "tooltips_class32" => "tooltip",
        _ if popup(h) => "popup",
        _ => "top",
    };
    let mut z = 0;
    let mut cursor = unsafe { GetTopWindow(0) };
    while cursor != 0 && cursor != h && z < 10000 {
        z += 1;
        cursor = unsafe { GetWindow(cursor, 2) };
    }
    w["kind"] = json!(kind);
    if kind == "dialog" {
        w["dialog_icon"] = json!(dialog_icon(h));
    }
    w["owner_id"] = json!(if owner == 0 && kind == "menu" {
        gui(h)["menu_owner"].as_i64().unwrap_or(0)
    } else {
        owner as i64
    });
    w["root_owner_id"] = json!(root);
    w["enabled"] = json!(enabled(h));
    w["is_modal"] = json!(
        kind == "dialog" && ((owner != 0 && !enabled(owner)) || (root != h && !enabled(root)))
    );
    w["foreground"] = json!(unsafe { native::GetForegroundWindow() } == h);
    w["topmost"] = json!(unsafe { GetWindowLongPtrW(h, -20) } & 8 != 0);
    w["z_order"] = json!(z);
    w["menu_mode"] = gui(h)["menu_mode"].clone();
}

pub fn dialog_icon(h: Handle) -> &'static str {
    #[link(name = "user32")]
    unsafe extern "system" {
        fn EnumChildWindows(
            h: Handle,
            callback: unsafe extern "system" fn(Handle, isize) -> i32,
            data: isize,
        ) -> i32;
        fn SendMessageTimeoutW(
            h: Handle,
            message: u32,
            w: usize,
            l: isize,
            flags: u32,
            ms: u32,
            result: *mut usize,
        ) -> isize;
        fn LoadIconW(instance: Handle, name: *const u16) -> Handle;
    }
    unsafe extern "system" fn collect(child: Handle, data: isize) -> i32 {
        unsafe {
            let list = &mut *(data as *mut Vec<Handle>);
            if GetWindowLongPtrW(child, -16) & 31 == 3
                && class(child).eq_ignore_ascii_case("Static")
            {
                list.push(child);
                return 0;
            }
            1
        }
    }
    let mut icons = Vec::<Handle>::new();
    unsafe {
        EnumChildWindows(h, collect, &mut icons as *mut _ as isize);
    }
    for child in icons {
        let mut icon = 0usize;
        if unsafe { SendMessageTimeoutW(child, 0x171, 0, 0, 2, 100, &mut icon) } != 0 && icon != 0 {
            for (id, name) in [
                (32513, "error"),
                (32514, "question"),
                (32515, "warning"),
                (32516, "information"),
            ] {
                if unsafe { LoadIconW(0, id as usize as *const u16) } as usize == icon {
                    return name;
                }
            }
        }
    }
    "unknown"
}
pub fn extra_windows() -> Vec<Handle> {
    let mut result = Vec::new();
    for name in ["#32768", "#32770"] {
        let cls: Vec<u16> = name.encode_utf16().chain(Some(0)).collect();
        let mut after = 0;
        for _ in 0..256 {
            let h = unsafe { FindWindowExW(0, after, cls.as_ptr(), std::ptr::null()) };
            if h == 0 || h == after {
                break;
            }
            result.push(h);
            after = h;
        }
    }
    result
}
pub fn focus() -> Value {
    let h = unsafe { native::GetForegroundWindow() };
    let g = gui(h);
    let f = g["focus_hwnd"].as_i64().unwrap_or(0) as Handle;
    json!({"hwnd":f,"name":Value::Null,"control_type":class(f)})
}
pub fn blocked_by(h: Handle) -> Option<Value> {
    if enabled(h) {
        return None;
    }
    native::windows(false)
        .into_iter()
        .filter(|candidate| *candidate != h && family(h, *candidate))
        .filter_map(|candidate| native::describe(candidate).ok())
        .find(|w| w["is_modal"] == true && w["enabled"] == true)
}
pub fn process_times(pid: u32) -> Result<(u64, u64)> {
    #[repr(C)]
    #[derive(Default)]
    struct FileTime {
        low: u32,
        high: u32,
    }
    #[link(name = "kernel32")]
    unsafe extern "system" {
        fn OpenProcess(access: u32, inherit: i32, pid: u32) -> Handle;
        fn CloseHandle(h: Handle) -> i32;
        fn GetProcessTimes(
            h: Handle,
            c: *mut FileTime,
            e: *mut FileTime,
            k: *mut FileTime,
            u: *mut FileTime,
        ) -> i32;
    }
    let h = unsafe { OpenProcess(0x1000, 0, pid) };
    if h == 0 {
        return Err(Error::new(
            "PROCESS_UNAVAILABLE",
            "Cannot inspect the selected PID.",
        ));
    }
    let (mut c, mut e, mut k, mut u) = (
        FileTime::default(),
        FileTime::default(),
        FileTime::default(),
        FileTime::default(),
    );
    let ok = unsafe { GetProcessTimes(h, &mut c, &mut e, &mut k, &mut u) };
    unsafe { CloseHandle(h) };
    if ok == 0 {
        return Err(Error::new(
            "PROCESS_UNAVAILABLE",
            "Cannot read process CPU time.",
        ));
    }
    let ticks = |t: FileTime| ((t.high as u64) << 32) | t.low as u64;
    Ok((ticks(c), ticks(k) + ticks(u)))
}

/// Read menu labels/state through documented Win32 menu APIs; never invoke commands.
pub fn menu_items(h: Handle) -> Vec<Value> {
    read_menu(h).1
}

/// Complete, labelled Win32 menu inventory, including rows outside the viewport.
pub fn menu_inventory(h: Handle) -> Option<Vec<Value>> {
    let (complete, rows) = read_menu(h);
    complete.then_some(rows)
}

fn read_menu(h: Handle) -> (bool, Vec<Value>) {
    #[repr(C)]
    struct Bar {
        size: u32,
        rect: Rect,
        menu: Handle,
        window: Handle,
        flags: u32,
    }
    #[repr(C)]
    struct Item {
        size: u32,
        mask: u32,
        kind: u32,
        state: u32,
        id: u32,
        submenu: Handle,
        checked: Handle,
        unchecked: Handle,
        data: usize,
        text: *mut u16,
        chars: u32,
        bitmap: Handle,
    }
    #[link(name = "user32")]
    unsafe extern "system" {
        fn GetMenu(h: Handle) -> Handle;
        fn GetMenuBarInfo(h: Handle, object: i32, item: i32, info: *mut Bar) -> i32;
        fn GetMenuItemCount(menu: Handle) -> i32;
        fn GetMenuItemInfoW(menu: Handle, index: u32, by_position: i32, info: *mut Item) -> i32;
        fn GetMenuItemRect(h: Handle, menu: Handle, index: u32, rect: *mut Rect) -> i32;
    }
    unsafe {
        let mut menu = GetMenu(h);
        if menu == 0 {
            let mut info = Bar {
                size: std::mem::size_of::<Bar>() as u32,
                rect: Rect::default(),
                menu: 0,
                window: 0,
                flags: 0,
            };
            if GetMenuBarInfo(h, -4, 0, &mut info) != 0 {
                menu = info.menu;
            }
        }
        if menu == 0 {
            return (false, Vec::new());
        }
        let count = GetMenuItemCount(menu);
        let mut complete = (1..=500).contains(&count);
        let mut result = Vec::new();
        let viewport = (class(h) == "#32768")
            .then(|| native::rect(h).ok())
            .flatten();
        for index in 0..count.clamp(0, 500) {
            let mut text = [0u16; 1024];
            let mut item = Item {
                size: std::mem::size_of::<Item>() as u32,
                mask: 0x147,
                kind: 0,
                state: 0,
                id: 0,
                submenu: 0,
                checked: 0,
                unchecked: 0,
                data: 0,
                text: text.as_mut_ptr(),
                chars: 1023,
                bitmap: 0,
            };
            if GetMenuItemInfoW(menu, index as u32, 1, &mut item) == 0 {
                complete = false;
                continue;
            }
            if item.kind & 0x800 != 0 {
                continue;
            }
            let label = String::from_utf16_lossy(
                &text[..text.iter().position(|v| *v == 0).unwrap_or(1024)],
            );
            if label.trim().is_empty() || item.chars >= 1023 {
                complete = false;
            }
            let mut key = String::new();
            let mut chars = label.chars();
            while let Some(c) = chars.next() {
                if c == '&'
                    && let Some(next) = chars.next()
                    && next != '&'
                {
                    key = next.to_string();
                    break;
                }
            }
            let offscreen = viewport.and_then(|view| {
                let mut item_rect = Rect::default();
                if GetMenuItemRect(0, menu, index as u32, &mut item_rect) == 0
                    || item_rect.bottom <= item_rect.top
                    || item_rect.right <= item_rect.left
                {
                    return None;
                }
                Some(
                    item_rect.top < view.top
                        || item_rect.bottom > view.bottom
                        || item_rect.left < view.left
                        || item_rect.right > view.right,
                )
            });
            result.push(json!({"name":label.split('\t').next().unwrap_or(&label).replace('&',""),"position":index,"has_submenu":item.submenu!=0,"enabled":item.state&3==0,"is_highlighted":item.state&0x80!=0,"access_key":key,"offscreen":offscreen,"accelerator_key":label.split_once('\t').map(|(_,v)|v).unwrap_or("")}));
        }
        (complete, result)
    }
}
