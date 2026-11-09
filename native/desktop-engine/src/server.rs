use crate::{
    input, native,
    task_switcher::{self, TaskSwitcher},
};
use base64::Engine;
use serde_json::{Value, json};
use std::{
    collections::HashMap,
    io::{Read, Write},
    os::windows::process::CommandExt,
    path::PathBuf,
    process::{Command, Stdio},
    time::{Duration, Instant},
};
#[derive(Debug)]
pub struct Error {
    code: String,
    message: String,
    hint: String,
    retriable: bool,
    context: Value,
}
pub type Result<T> = std::result::Result<T, Error>;
impl std::fmt::Display for Error {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}: {}", self.code, self.message)
    }
}
impl std::error::Error for Error {}
impl Error {
    pub fn new(code: &str, message: &str) -> Self {
        Self {
            code: code.into(),
            message: message.into(),
            hint: "Refresh the target observation and inspect its window family before retrying."
                .into(),
            retriable: matches!(
                code,
                "FOCUS_CHANGED"
                    | "FOCUS_LOST"
                    | "FOCUS_DENIED"
                    | "FOREGROUND_LOCKED"
                    | "STALE_OBSERVATION"
                    | "ELEMENT_CHANGED"
                    | "WINDOW_CHANGED"
                    | "INPUT_BUSY"
            ),
            context: json!({}),
        }
    }
    pub fn value(&self) -> Value {
        json!({"code":self.code,"message":self.message,"hint":self.hint,"retriable":self.retriable,"context":self.context})
    }
}
impl Error {
    pub fn context(mut self, context: Value) -> Self {
        self.context = context;
        self
    }
    pub fn from_value(v: &Value) -> Self {
        let mut e = Self::new(
            v["code"].as_str().unwrap_or("WORKER_FAILED"),
            v["message"].as_str().unwrap_or("Native worker failed."),
        );
        if let Some(h) = v["hint"].as_str() {
            e.hint = h.into();
        }
        if let Some(r) = v["retriable"].as_bool() {
            e.retriable = r;
        }
        if v["context"].is_object() {
            e.context = v["context"].clone();
        }
        e
    }
}
pub fn integer(p: &Value, key: &str, default: i64, min: i64, max: i64) -> Result<i64> {
    p.get(key)
        .map_or(Some(default), Value::as_i64)
        .filter(|n| *n >= min && *n <= max)
        .ok_or_else(|| {
            Error::new(
                "INVALID_PARAMS",
                &format!("{key} must be an integer in {min}..{max}."),
            )
        })
}
fn boolean(p: &Value, key: &str, default: bool) -> Result<bool> {
    p.get(key)
        .map_or(Some(default), Value::as_bool)
        .ok_or_else(|| Error::new("INVALID_PARAMS", &format!("{key} must be boolean.")))
}
fn uid() -> String {
    uuid::Uuid::new_v4().simple().to_string()
}
fn worker(kind: &str, data: Value) -> Result<Value> {
    let mut child = Command::new(
        std::env::current_exe()
            .map_err(|_| Error::new("WORKER_FAILED", "Cannot locate native worker."))?,
    )
    .args(["--worker", kind])
    .creation_flags(0x08000000)
    .stdin(Stdio::piped())
    .stdout(Stdio::piped())
    .stderr(Stdio::null())
    .spawn()
    .map_err(|_| Error::new("WORKER_FAILED", "Cannot launch native worker."))?;
    let sent = child
        .stdin
        .take()
        .unwrap()
        .write_all(data.to_string().as_bytes());
    if sent.is_err() {
        let _ = child.kill();
        let _ = child.wait();
        return Err(Error::new(
            "WORKER_FAILED",
            "Cannot write native worker request.",
        ));
    }
    let stdout = child.stdout.take().unwrap();
    let reader = std::thread::spawn(move || {
        let mut data = Vec::new();
        stdout
            .take(4 * 1024 * 1024 + 1)
            .read_to_end(&mut data)
            .map(|_| data)
    });
    let started = Instant::now();
    loop {
        if crate::indicator::cancelled() {
            let _ = child.kill();
            let _ = child.wait();
            let _ = reader.join();
            return crate::indicator::check().map(|_| Value::Null);
        }
        match child.try_wait() {
            Ok(Some(_)) => break,
            Ok(None) if started.elapsed() < Duration::from_secs(8) => {
                std::thread::sleep(Duration::from_millis(10))
            }
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                let _ = reader.join();
                return Err(Error::new(
                    &format!("{}_TIMEOUT", kind.to_uppercase()),
                    "Isolated native worker timed out and was terminated.",
                ));
            }
        }
    }
    let raw = reader
        .join()
        .ok()
        .and_then(|v| v.ok())
        .filter(|v| v.len() <= 4 * 1024 * 1024)
        .ok_or_else(|| Error::new("WORKER_FAILED", "Invalid worker output."))?;
    let answer: Value = serde_json::from_slice(&raw)
        .map_err(|_| Error::new("WORKER_FAILED", "Malformed native worker output."))?;
    if answer["ok"] != true {
        return Err(Error::from_value(&answer["error"]));
    }
    Ok(answer["result"].clone())
}
#[derive(Clone)]
struct Observation {
    window: Value,
    created: Instant,
    accessibility: Value,
    image: Value,
    ttl: f64,
}
pub struct Server {
    output: PathBuf,
    ttl: f64,
    allow_input: bool,
    visible_pixels_allowed: bool,
    allow_owned_popups: bool,
    switcher_ocr: bool,
    bindings: HashMap<String, (String, String)>,
    observations: HashMap<String, Observation>,
}
impl Server {
    pub fn new(output: PathBuf, ttl: f64, allow_input: bool) -> Self {
        Self {
            output,
            ttl,
            allow_input,
            visible_pixels_allowed: false,
            allow_owned_popups: false,
            switcher_ocr: false,
            bindings: HashMap::new(),
            observations: HashMap::new(),
        }
    }
    /// Adapter policy, never supplied by tool arguments. OmniCore leaves this disabled.
    pub fn allow_visible_pixel_capture(&mut self) {
        self.visible_pixels_allowed = true;
    }
    pub fn allow_switcher_ocr(&mut self) {
        self.switcher_ocr = true;
    }
    pub fn validate_key_sequence(&self, p: &Value) -> Result<()> {
        input::validate_sequence(p)
    }
    pub fn allow_owned_popup_input(&mut self) {
        self.allow_owned_popups = true;
    }
    pub fn inspect_accessibility(&self, h: isize, maximum: usize) -> Result<Value> {
        native::validate_target(h)?;
        worker(
            "accessibility",
            json!({"hwnd":h,"max_elements":maximum.min(500)}),
        )
    }
    fn invalidate(&mut self, id: &str) {
        self.observations.retain(|_, o| o.window["id"] != id);
    }
    fn bind(&mut self, h: native::Handle) -> Result<Value> {
        native::validate_target(h)?;
        let mut w = native::describe(h)?;
        let key = native::identity(&w)?;
        let id = w["id"].as_str().unwrap().to_string();
        if self.bindings.get(&id).is_none_or(|a| a.0 != key) {
            self.bindings.insert(id.clone(), (key, uid()));
            self.invalidate(&id)
        }
        w["identity"] = json!(self.bindings[&id].1);
        Ok(w)
    }
    fn resolve(&mut self, p: &Value) -> Result<(native::Handle, Value)> {
        let supplied = p.get("window").unwrap_or(&p["id"]);
        let h = native::hwnd(supplied)?;
        let w = self.bind(h)?;
        crate::indicator::target(h);
        if supplied.is_object()
            && (supplied["pid"] != w["pid"] || supplied["identity"] != w["identity"])
        {
            return Err(Error::new(
                "WINDOW_CHANGED",
                "Window identity no longer matches discovery.",
            ));
        }
        Ok((h, w))
    }
    fn observed(&mut self, p: &Value) -> Result<(native::Handle, Value, Observation)> {
        if !self.allow_input {
            return Err(Error::new(
                "INPUT_DISABLED",
                "Start with --allow-input to enable input.",
            ));
        }
        let o = p["observation_id"]
            .as_str()
            .and_then(|id| self.observations.get(id))
            .cloned()
            .ok_or_else(|| {
                Error::new(
                    "STALE_OBSERVATION",
                    "Call get_window_state for a current observation.",
                )
            })?;
        let (h, w) = self.resolve(p)?;
        if w["id"] != o.window["id"] || w["identity"] != o.window["identity"] {
            return Err(Error::new(
                "WINDOW_CHANGED",
                "Observation belongs to another target.",
            ));
        }
        if o.created.elapsed().as_secs_f64() > o.ttl {
            self.invalidate(w["id"].as_str().unwrap());
            return Err(Error::new("STALE_OBSERVATION", "Observation expired."));
        }
        if w["bounds"] != o.window["bounds"] {
            self.invalidate(w["id"].as_str().unwrap());
            return Err(Error::new("WINDOW_CHANGED", "Target geometry changed."));
        }
        if w["visible"] != true || w["minimized"] == true {
            return Err(Error::new("WINDOW_NOT_VISIBLE", "Target must be visible."));
        }
        if !crate::windowing::focus_matches(h, self.allow_owned_popups) {
            return Err(Error::new(
                "FOCUS_CHANGED",
                "Activate target and refresh state.",
            ));
        }
        if let Some(modal) = crate::windowing::blocked_by(h) {
            return Err(Error::new(
                "TARGET_BLOCKED_BY_MODAL",
                "The selected window is disabled by a modal dialog.",
            )
            .context(json!({"blocked_by":modal})));
        }
        if p.get("screenshot_id")
            .is_some_and(|id| id != &o.image["id"])
        {
            return Err(Error::new(
                "STALE_SCREENSHOT",
                "Screenshot does not match observation.",
            ));
        }
        Ok((h, w, o))
    }
    fn point(&self, p: &Value, w: &Value, o: &Observation, prefix: &str) -> Result<(i32, i32)> {
        let b = &w["bounds"];
        let x = b["x"].as_i64().unwrap();
        let y = b["y"].as_i64().unwrap();
        let (point_x, point_y) = if prefix.is_empty() && p.get("element_index").is_some() {
            let index = integer(p, "element_index", 0, 0, 499)?;
            let element = o.accessibility["elements"]
                .as_array()
                .and_then(|a| a.iter().find(|e| e["index"] == index))
                .ok_or_else(|| {
                    Error::new("ELEMENT_NOT_FOUND", "Element absent from observation.")
                })?;
            if element["enabled"] == false || element["offscreen"] == true {
                return Err(Error::new(
                    "ELEMENT_UNAVAILABLE",
                    "Element is disabled or offscreen.",
                ));
            }
            if element["runtime_id"]
                .as_array()
                .is_none_or(|a| a.is_empty())
            {
                return Err(Error::new(
                    "ELEMENT_IDENTITY_UNAVAILABLE",
                    "Use observed coordinates instead.",
                ));
            }
            let fresh = worker(
                "accessibility",
                json!({"hwnd":native::hwnd(w)?,"max_elements":500}),
            )?;
            let found = fresh["elements"]
                .as_array()
                .and_then(|a| a.iter().find(|e| e["runtime_id"] == element["runtime_id"]));
            if found.is_none_or(|e| {
                e["bounds"] != element["bounds"] || e["enabled"] == false || e["offscreen"] == true
            }) {
                return Err(Error::new(
                    "ELEMENT_CHANGED",
                    "Element changed; refresh state.",
                ));
            }
            let eb = &element["bounds"];
            let width = eb["width"].as_f64().unwrap_or(0.0);
            let height = eb["height"].as_f64().unwrap_or(0.0);
            if width <= 0.0 || height <= 0.0 {
                return Err(Error::new(
                    "ELEMENT_UNAVAILABLE",
                    "No usable element bounds.",
                ));
            }
            (
                (eb["x"].as_f64().unwrap_or(0.0) + width / 2.0).round() as i64,
                (eb["y"].as_f64().unwrap_or(0.0) + height / 2.0).round() as i64,
            )
        } else {
            let coordinate = |name: &str| -> Result<i64> {
                p[name]
                    .as_f64()
                    .filter(|v| v.is_finite() && v.abs() < 1000000.0)
                    .map(|v| v.round() as i64)
                    .ok_or_else(|| {
                        Error::new("INVALID_PARAMS", "Coordinates must be finite numbers.")
                    })
            };
            (
                if p.get("screenshot_id").is_some() {
                    crate::capture::map_coordinate(
                        &o.image,
                        coordinate(&format!("{prefix}x"))?,
                        true,
                    )?
                } else {
                    x + coordinate(&format!("{prefix}x"))?
                },
                if p.get("screenshot_id").is_some() {
                    crate::capture::map_coordinate(
                        &o.image,
                        coordinate(&format!("{prefix}y"))?,
                        false,
                    )?
                } else {
                    y + coordinate(&format!("{prefix}y"))?
                },
            )
        };
        if point_x < x
            || point_y < y
            || point_x >= x + b["width"].as_i64().unwrap()
            || point_y >= y + b["height"].as_i64().unwrap()
        {
            if self.allow_owned_popups
                && crate::windowing::point_matches(
                    native::hwnd(w)?,
                    point_x as i32,
                    point_y as i32,
                    true,
                )
            {
                return Ok((point_x as i32, point_y as i32));
            }
            let hit = unsafe {
                native::GetAncestor(
                    native::WindowFromPoint(native::Point {
                        x: point_x as i32,
                        y: point_y as i32,
                    }),
                    2,
                )
            };
            let other = native::describe(hit)
                .ok()
                .map(|v| json!({"id":hit,"kind":v["kind"],"same_pid":v["pid"]==w["pid"]}));
            return Err(Error::new("OUTSIDE_TARGET","Point lies outside target bounds.").context(json!({"target_bounds":b,"point_screen":{"x":point_x,"y":point_y},"window_at_point":other})));
        }
        Ok((point_x as i32, point_y as i32))
    }
    fn state(&mut self, p: &Value) -> Result<Value> {
        let (h, w) = self.resolve(p)?;
        if task_switcher::shell_switcher(h as u64) {
            return Err(Error::new(
                "WINDOW_REQUIRED",
                "El selector sólo se observa internamente durante la activación.",
            ));
        }
        if let Some(menu) = p.get("menu") {
            if boolean(p, "include_screenshot", false)?
                || boolean(p, "include_text", false)?
                || p.get("find").is_some()
                || p.get("screenshot_region").is_some()
            {
                return Err(Error::new(
                    "INVALID_PARAMS",
                    "Read the menu separately from screenshot or text.",
                ));
            }
            if w["visible"] != true || w["minimized"] == true {
                return Err(Error::new(
                    "WINDOW_NOT_VISIBLE",
                    "Observe a visible non-minimized window.",
                ));
            }
            let inventory = worker("menu", json!({"hwnd":h,"menu":menu}))?;
            if self.bind(h)?["identity"] != w["identity"] {
                return Err(Error::new(
                    "WINDOW_CHANGED",
                    "Target changed while reading the menu.",
                ));
            }
            return Ok(json!({"window":w,"menu":inventory}));
        }
        let Some(find) = p.get("find") else {
            return self.observe(h, w, p);
        };
        let wait_ms = crate::forms::validate(find)?;
        let image = boolean(p, "include_screenshot", false)?;
        if !boolean(p, "include_text", true)? || (wait_ms > 0 && image) {
            return Err(Error::new(
                "INVALID_PARAMS",
                "find requiere accesibilidad. Para esperar, solicita el recorte después.",
            ));
        }
        let mut params = p.clone();
        params["include_text"] = json!(true);
        if params.get("max_elements").is_none() {
            params["max_elements"] = json!(500);
        }
        let started = Instant::now();
        let budget = Duration::from_millis(wait_ms);
        loop {
            crate::indicator::check()?;
            let mut state = self.observe(h, w.clone(), &params)?;
            crate::forms::project(&mut state, find);
            if wait_ms == 0 || state["matched"] == true || started.elapsed() >= budget {
                if wait_ms > 0 {
                    state["waited_ms"] = json!(started.elapsed().as_millis());
                }
                return Ok(state);
            }
            std::thread::sleep(
                Duration::from_millis(100).min(budget.saturating_sub(started.elapsed())),
            );
        }
    }
    fn observe(&mut self, h: isize, w: Value, p: &Value) -> Result<Value> {
        let image = boolean(p, "include_screenshot", false)?;
        if !image && p.get("screenshot_region").is_some() {
            return Err(Error::new(
                "INVALID_PARAMS",
                "screenshot_region requiere include_screenshot: true.",
            ));
        }
        let ttl = self.ttl;
        let text = boolean(p, "include_text", !image)?;
        if !image && !text {
            return Err(Error::new("INVALID_PARAMS", "Request screenshot or text."));
        }
        if w["visible"] != true || w["minimized"] == true {
            return Err(Error::new(
                "WINDOW_NOT_VISIBLE",
                "Observe a visible non-minimized window.",
            ));
        }
        let oid = uid();
        let path = self.output.join(format!("{oid}.png"));
        std::fs::create_dir_all(&self.output)
            .map_err(|_| Error::new("CAPTURE_FAILED", "Cannot create capture directory."))?;
        let result = (|| {
            let mut shots = Vec::new();
            if image {
                let mut capture = json!({"hwnd":h,"bounds":w["bounds"],"output_path":path,
                    "visible_pixels_allowed":self.visible_pixels_allowed,
                    "capture_backend":p.get("capture_backend").cloned().unwrap_or(json!("wgc")),
                    "image_scale":p.get("image_scale").cloned().unwrap_or(json!(1.0))});
                if let Some(region) = p.get("screenshot_region") {
                    capture["screenshot_region"] = region.clone();
                }
                let mut metadata = worker("capture", capture)?;
                let bytes = std::fs::read(&path)
                    .map_err(|_| Error::new("CAPTURE_FAILED", "Cannot read capture."))?;
                if bytes.len() > 8 * 1024 * 1024 {
                    return Err(Error::new("IMAGE_TOO_LARGE", "PNG exceeds 8 MiB."));
                }
                metadata["id"] = json!(format!("{oid}:image"));
                metadata["url"] = json!(format!(
                    "data:image/png;base64,{}",
                    base64::engine::general_purpose::STANDARD.encode(bytes)
                ));
                metadata["originX"] = metadata["bounds"]["x"].clone();
                metadata["originY"] = metadata["bounds"]["y"].clone();
                metadata["coordinate_space"] = json!("physical-pixels");
                shots.push(metadata);
            }
            let a = if text {
                worker(
                    "accessibility",
                    json!({"hwnd":h,"max_elements":integer(p,"max_elements",200,1,500)?}),
                )?
            } else {
                Value::Null
            };
            let now = self.bind(h)?;
            if now["identity"] != w["identity"] || now["bounds"] != w["bounds"] {
                return Err(Error::new(
                    "WINDOW_CHANGED",
                    "Target changed while acquiring state.",
                ));
            }
            self.invalidate(w["id"].as_str().unwrap());
            self.observations
                .retain(|_, o| o.created.elapsed().as_secs_f64() <= o.ttl);
            if self.observations.len() >= 64 {
                self.observations.clear()
            }
            self.observations.insert(
                oid.clone(),
                Observation {
                    window: w.clone(),
                    created: Instant::now(),
                    ttl,
                    accessibility: a.clone(),
                    image: shots
                        .first()
                        .cloned()
                        .map(|mut s| {
                            s.as_object_mut().unwrap().remove("url");
                            s.as_object_mut().unwrap().remove("path");
                            s
                        })
                        .unwrap_or(Value::Null),
                },
            );
            Ok(
                json!({"window":w,"observation_id":oid,"expires_in_seconds":ttl,"screenshots":shots,"accessibility":a,"limitations":["Sequential snapshots; content changes may remain undetected. Refresh after every input."]}),
            )
        })();
        let _ = std::fs::remove_file(path);
        result
    }
    fn activate_with_switcher(&mut self, origin: u64, h: isize, w: &Value) -> Result<&'static str> {
        let title = w["title"].as_str().unwrap_or_default();
        let windows: Vec<_> = native::windows(false)
            .into_iter()
            .filter_map(|hwnd| native::describe(hwnd).ok())
            .collect();
        let normalized = crate::switcher_ocr::normalize(title);
        if normalized.is_empty()
            || w["kind"] != "top"
            || w["enabled"] != true
            || windows.iter().any(|other| {
                other["id"] != w["id"]
                    && other["title"]
                        .as_str()
                        .is_some_and(|name| crate::switcher_ocr::normalize(name) == normalized)
            })
        {
            return Err(Error::new(
                "TASK_SWITCHER_UNAVAILABLE",
                "El destino debe ser una ventana principal habilitada con título único.",
            ));
        }
        crate::indicator::target(h);
        let switcher = TaskSwitcher::open(origin)?;
        let stamp = task_switcher::input_stamp()?;
        let (point, source) = match switcher.card(self, title)? {
            Some(point) => (point, "accessibility"),
            None if self.switcher_ocr => {
                let shell = self.bind(switcher.hwnd() as isize)?;
                let state = self.observe(switcher.hwnd() as isize, shell, &json!({
                    "include_screenshot":true,"include_text":false,"capture_backend":"printwindow"
                }))?;
                self.invalidate(&format!("0x{:x}", switcher.hwnd()));
                (task_switcher::visual_card(&state, title, &windows)?, "ocr")
            }
            None => {
                return Err(Error::new(
                    "TASK_SWITCHER_UNAVAILABLE",
                    "La tarjeta no se pudo leer por accesibilidad y el OCR del selector no está disponible.",
                ));
            }
        };
        let current = self.bind(h)?;
        if current["identity"] != w["identity"]
            || current["title"] != title
            || current["enabled"] != true
        {
            return Err(Error::new(
                "WINDOW_CHANGED",
                "Cambió la ventana antes de elegir su tarjeta.",
            ));
        }
        switcher.select(point, h as u64, stamp)?;
        if self.bind(h)?["identity"] != w["identity"] {
            return Err(Error::new(
                "WINDOW_CHANGED",
                "Cambió la identidad de la ventana activada.",
            ));
        }
        Ok(source)
    }
    fn call(&mut self, method: &str, p: &Value) -> Result<Value> {
        crate::indicator::check()?;
        match method {
            "capabilities" => Ok(
                json!({"protocol":"ndjson-v2-independent","implementation":"rust","product":"OmniDesktop Control","input_enabled":self.allow_input,"activation_fallback":"task_switcher","task_switcher_ocr":self.switcher_ocr,"observation_ttl_seconds":self.ttl,"capture_backend":"wgc","capture_backends":["wgc","printwindow"],"engine_version":env!("CARGO_PKG_VERSION"),"capture_scope":"window-only","activity_indicator":"purple-curtain","cancellation":["button","Escape"],"accessibility_backend":"Microsoft UI Automation","input_backend":"SendInput","compact_find":true,"screenshot_region":true,"methods":["list_windows","get_window","activate_window","get_window_state","click","scroll","drag","type_text","press_key","invoke","set_value","select_option"]}),
            ),
            "list_windows" => {
                let hidden = boolean(p, "include_hidden", false)?;
                let list: Vec<_> = native::windows(hidden)
                    .into_iter()
                    .filter(|h| !task_switcher::shell_switcher(*h as u64))
                    .filter_map(|h| self.bind(h).ok())
                    .collect();
                Ok(json!(list))
            }
            "get_window" => Ok(self.resolve(p)?.1),
            "get_window_state" => self.state(p),
            "activate_window" => {
                if !self.allow_input {
                    return Err(Error::new("INPUT_DISABLED", "Input is disabled."));
                }
                let fallback = match p.get("fallback") {
                    None => false,
                    Some(value) if value == "none" => false,
                    Some(value) if value == "task_switcher" => true,
                    _ => {
                        return Err(Error::new(
                            "INVALID_PARAMS",
                            "fallback debe ser none o task_switcher.",
                        ));
                    }
                };
                let (h, w) = self.resolve(p)?;
                if task_switcher::shell_switcher(h as u64) {
                    return Err(Error::new(
                        "WINDOW_REQUIRED",
                        "El selector no es una ventana de aplicación.",
                    ));
                }
                if fallback && let Some(modal) = crate::windowing::blocked_by(h) {
                    return Err(Error::new(
                        "TARGET_BLOCKED_BY_MODAL",
                        "La ventana tiene un diálogo modal.",
                    )
                    .context(json!({"dialog":modal})));
                }
                let direct_timeout = integer(p, "direct_timeout_ms", 150, 0, 2000)? as u64;
                let origin = task_switcher::foreground();
                if origin == h as u64 {
                    self.invalidate(w["id"].as_str().unwrap());
                    return Ok(
                        json!({"window":w,"activated":true,"activation_method":"already_foreground",
                        "requires_fresh_observation":true}),
                    );
                }
                unsafe {
                    if native::IsIconic(h) != 0 {
                        native::ShowWindow(h, 9);
                    }
                    native::SetForegroundWindow(h);
                }
                let deadline = Instant::now() + Duration::from_millis(direct_timeout);
                loop {
                    crate::indicator::check()?;
                    if unsafe { native::GetForegroundWindow() } == h {
                        self.invalidate(w["id"].as_str().unwrap());
                        return Ok(
                            json!({"window":self.bind(h)?,"activated":true,"activation_method":"direct","requires_fresh_observation":true}),
                        );
                    }
                    let remaining = deadline.saturating_duration_since(Instant::now());
                    if remaining.is_zero() {
                        break;
                    }
                    std::thread::sleep(remaining.min(Duration::from_millis(25)));
                }
                let title = w["title"]
                    .as_str()
                    .filter(|s| !s.is_empty())
                    .unwrap_or("la ventana seleccionada");
                let mut context =
                    json!({"target":{"id":w["id"],"pid":w["pid"],"title":title,"kind":w["kind"]}});
                if fallback {
                    self.invalidate(w["id"].as_str().unwrap());
                    match self.activate_with_switcher(origin, h, &w) {
                        Ok(source) => {
                            return Ok(json!({"window":self.bind(h)?,"activated":true,
                            "activation_method":"task_switcher","selection_source":source,"requires_fresh_observation":true}));
                        }
                        Err(error)
                            if matches!(
                                error.value()["code"].as_str(),
                                Some("CANCELLED" | "INPUT_BUSY" | "INPUT_CLEANUP_FAILED")
                            ) =>
                        {
                            return Err(error);
                        }
                        Err(error) => context["task_switcher"] = error.value(),
                    }
                }
                Err(Error::new(
                    "FOREGROUND_LOCKED",
                    &format!("Windows no concedió el foco. Activa «{title}» y vuelve a observar."),
                )
                .context(context))
            }
            "dismiss_menu" | "navigate_menu" => {
                if !self.allow_input || !self.allow_owned_popups {
                    return Err(Error::new(
                        "RESERVED_CANCEL_KEY",
                        "Owned menu input is unavailable in this adapter.",
                    ));
                }
                let (h, w) = self.resolve(p)?;
                if !crate::windowing::focus_matches(h, true)
                    || crate::windowing::gui(h)["menu_mode"] != true
                    || w["visible"] != true
                    || w["enabled"] != true
                    || w["minimized"] == true
                {
                    return Err(Error::new(
                        "MENU_NOT_ACTIVE",
                        "The owned menu loop is not active.",
                    ));
                }
                let (operation, args) = if method == "navigate_menu" {
                    let keys = p["keys"]
                        .as_array()
                        .filter(|keys| {
                            (1..=33).contains(&keys.len())
                                && keys.iter().all(|key| {
                                    matches!(
                                        key.as_str(),
                                        Some(
                                            "Home"
                                                | "End"
                                                | "ArrowUp"
                                                | "ArrowDown"
                                                | "ArrowRight"
                                                | "Enter"
                                        )
                                    )
                                })
                        })
                        .ok_or_else(|| {
                            Error::new("INVALID_PARAMS", "Use a bounded menu navigation sequence.")
                        })?;
                    (
                        "press_key",
                        json!({"keys":keys,"delay_ms":16,"allow_owned_popups":true,"require_menu":true}),
                    )
                } else {
                    (
                        "dismiss_menu",
                        json!({"allow_owned_popups":true,"require_menu":true}),
                    )
                };
                let result = input::perform(h, operation, &args, &[])?;
                self.invalidate(w["id"].as_str().unwrap());
                Ok(json!({"performed":method,"details":result}))
            }
            "invoke" | "set_value" | "select_option" => {
                let (h, w, o) = self.observed(p)?;
                let index = integer(p, "element_index", -1, 0, 499)?;
                let element = o.accessibility["elements"]
                    .as_array()
                    .and_then(|rows| rows.iter().find(|e| e["index"] == index))
                    .ok_or_else(|| {
                        Error::new(
                            "ELEMENT_NOT_FOUND",
                            "Refresh accessibility before an element action.",
                        )
                    })?;
                let result = worker(
                    "uia_action",
                    json!({"hwnd":h,"identity":native::identity(&w)?,"element":element,"operation":method,
                        "value":p.get("value"),"option":p.get("option"),"allow_owned_popups":self.allow_owned_popups,
                        "refresh_expired":o.created.elapsed().as_secs_f64()>o.ttl}),
                );
                self.invalidate(w["id"].as_str().unwrap());
                Ok(
                    json!({"performed":method,"window_id":w["id"],"details":result?,"requires_fresh_observation":true}),
                )
            }
            "click" | "scroll" | "drag" | "type_text" | "press_key" => {
                let (h, w, o) = self.observed(p)?;
                let mut points = Vec::new();
                if matches!(method, "click" | "scroll") {
                    points.push(self.point(p, &w, &o, "")?)
                }
                if method == "drag" {
                    points.push(self.point(p, &w, &o, "start_")?);
                    points.push(self.point(p, &w, &o, "end_")?)
                }
                let result = (|| {
                    self.observed(p)?;
                    if method == "dismiss_menu"
                        && (!self.allow_owned_popups
                            || crate::windowing::gui(h)["menu_mode"] != true)
                    {
                        return Err(Error::new(
                            "MENU_NOT_ACTIVE",
                            "Menu cleanup is only allowed in an active menu loop.",
                        ));
                    }
                    let mut input_params = p.clone();
                    input_params["allow_owned_popups"] = json!(self.allow_owned_popups);
                    input::perform(h, method, &input_params, &points)
                })();
                self.invalidate(w["id"].as_str().unwrap());
                Ok(
                    json!({"performed":method,"window_id":w["id"],"details":result?,"requires_fresh_observation":true}),
                )
            }
            _ => Err(Error::new(
                "METHOD_NOT_IMPLEMENTED",
                "Unknown method. Query capabilities.",
            )),
        }
    }
    pub fn dispatch(&mut self, r: Value) -> Value {
        let id = r["id"].clone();
        let result = if !r.is_object() || !(id.is_string() || id.is_i64() || id.is_u64()) {
            Err(Error::new(
                "INVALID_REQUEST",
                "Integer/string request id is required.",
            ))
        } else if r.get("params").is_some_and(|v| !v.is_object()) {
            Err(Error::new("INVALID_PARAMS", "params must be an object."))
        } else {
            self.call(
                r["method"].as_str().unwrap_or(""),
                r.get("params").unwrap_or(&json!({})),
            )
        };
        match result {
            Ok(v) => json!({"id":id,"ok":true,"result":v}),
            Err(e) => json!({"id":id,"ok":false,"error":e.value()}),
        }
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn input_disabled_before_target() {
        let mut s = Server::new(std::env::temp_dir(), 30.0, false);
        let r = s.dispatch(json!({"id":1,"method":"click","params":{}}));
        assert_eq!(r["error"]["code"], "INPUT_DISABLED")
    }
    #[test]
    fn validation() {
        assert!(integer(&json!({"n":true}), "n", 0, 0, 5).is_err());
        let mut s = Server::new(std::env::temp_dir(), 30.0, false);
        assert_eq!(
            s.dispatch(json!({"id":false}))["error"]["code"],
            "INVALID_REQUEST"
        );
    }
}
