use crate::server::{Error, Result};
use serde_json::{Value, json};

fn name(value: &Value) -> Option<String> {
    let value = value.as_str()?.trim();
    (!value.is_empty() && value.chars().count() <= 512).then(|| value.to_lowercase())
}

pub(crate) fn validate(find: &Value) -> Result<u64> {
    let invalid = || Error::new("INVALID_PARAMS", "find requiere name y opciones válidas.");
    if name(&find["name"]).is_none()
        || find
            .get("within")
            .is_some_and(|scope| !scope.is_object() || name(&scope["name"]).is_none())
        || find
            .get("control_type")
            .is_some_and(|v| v.as_str().is_none())
        || find["within"]
            .get("control_type")
            .is_some_and(|v| v.as_str().is_none())
        || ["enabled", "include_bounds", "include_value"]
            .iter()
            .any(|key| find.get(key).is_some_and(|v| !v.is_boolean()))
    {
        return Err(invalid());
    }
    match find.get("wait_ms") {
        None => Ok(0),
        Some(value) => value.as_u64().filter(|v| *v <= 5000).ok_or_else(invalid),
    }
}

fn control_type(row: &Value) -> &'static str {
    const TYPES: [&str; 41] = [
        "Button",
        "Calendar",
        "CheckBox",
        "ComboBox",
        "Edit",
        "Hyperlink",
        "Image",
        "ListItem",
        "List",
        "Menu",
        "MenuBar",
        "MenuItem",
        "ProgressBar",
        "RadioButton",
        "ScrollBar",
        "Slider",
        "Spinner",
        "StatusBar",
        "Tab",
        "TabItem",
        "Text",
        "ToolBar",
        "ToolTip",
        "Tree",
        "TreeItem",
        "Custom",
        "Group",
        "Thumb",
        "DataGrid",
        "DataItem",
        "Document",
        "SplitButton",
        "Window",
        "Pane",
        "Header",
        "HeaderItem",
        "Table",
        "TitleBar",
        "Separator",
        "SemanticZoom",
        "AppBar",
    ];
    row["control_type_id"]
        .as_u64()
        .and_then(|v| v.checked_sub(50000))
        .and_then(|i| TYPES.get(i as usize))
        .copied()
        .unwrap_or("Unknown")
}

fn matches_label(row: &Value, label: &str, kind: Option<&str>) -> bool {
    row["is_password"] == false
        && name(&row["name"]).as_deref() == Some(label)
        && kind.is_none_or(|kind| kind.eq_ignore_ascii_case(control_type(row)))
}

fn inside(rows: &[Value], row: &Value, label: &str, kind: Option<&str>) -> bool {
    let mut parent = row["parent_index"].as_u64();
    for _ in 0..16 {
        let Some(row) = parent.and_then(|i| rows.get(i as usize)) else {
            return false;
        };
        if matches_label(row, label, kind) {
            return true;
        }
        parent = row["parent_index"].as_u64();
    }
    false
}

pub(crate) fn project(state: &mut Value, find: &Value) {
    let accessibility = state
        .as_object_mut()
        .unwrap()
        .remove("accessibility")
        .unwrap();
    let rows = accessibility["elements"]
        .as_array()
        .map_or(&[][..], Vec::as_slice);
    let label = name(&find["name"]).unwrap();
    let within = name(&find["within"]["name"]);
    let window = &state["window"]["bounds"];
    let mut count = 0;
    let mut matches = Vec::new();
    for row in rows {
        if row["offscreen"] != false
            || !matches_label(row, &label, find["control_type"].as_str())
            || within.as_deref().is_some_and(|label| {
                !inside(rows, row, label, find["within"]["control_type"].as_str())
            })
            || find["enabled"]
                .as_bool()
                .is_some_and(|enabled| row["enabled"].as_bool() != Some(enabled))
        {
            continue;
        }
        count += 1;
        if matches.len() == 5 {
            continue;
        }
        let mut entry = json!({"element_index":row["index"],"name":row["name"],
            "control_type":control_type(row),"enabled":row["enabled"]});
        if find["include_bounds"] == true && row["bounds"].is_object() {
            let bounds = &row["bounds"];
            entry["bounds"] = json!({
                "x":bounds["x"].as_i64().unwrap_or(0)-window["x"].as_i64().unwrap_or(0),
                "y":bounds["y"].as_i64().unwrap_or(0)-window["y"].as_i64().unwrap_or(0),
                "width":bounds["width"],"height":bounds["height"]
            });
        }
        if find["include_value"] == true
            && let Some(value) = row["value"].as_str()
        {
            entry["value"] = json!(value.chars().take(256).collect::<String>());
            if value.chars().count() > 256 {
                entry["value_truncated"] = json!(true);
            }
        }
        matches.push(entry);
    }
    let truncated = count > matches.len() || accessibility["truncated"] != false;
    state["matches"] = json!(matches);
    state["matched_count"] = json!(count);
    state["truncated"] = json!(truncated);
    state["matched"] = json!(count == 1 && !truncated);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn scoped_enabled_find_preserves_the_element_index_and_window_relative_bounds() {
        let mut state = json!({"window":{"bounds":{"x":-500,"y":80}},"observation_id":"current",
        "accessibility":{"truncated":false,"elements":[
            {"index":0,"name":"Shipping","control_type_id":50026,"is_password":false},
            {"index":1,"parent_index":0,"name":"City","control_type_id":50003,"is_password":false,
                "offscreen":false,"enabled":true,"bounds":{"x":-490,"y":100,"width":80,"height":20}},
            {"index":2,"parent_index":0,"name":"City","control_type_id":50003,"is_password":true,
                "offscreen":false,"enabled":true,"value":"secret"},
            {"index":3,"name":"City","control_type_id":50003,"is_password":false,
                "offscreen":false,"enabled":true}
        ]}});
        let find = json!({"name":"city","control_type":"ComboBox","enabled":true,"include_bounds":true,
            "include_value":true,"within":{"name":"Shipping","control_type":"Group"},"wait_ms":5000});
        assert_eq!(validate(&find).unwrap(), 5000);
        project(&mut state, &find);
        assert_eq!(state["matched"], true);
        assert_eq!(state["observation_id"], "current");
        assert_eq!(state["matches"][0]["element_index"], 1);
        assert_eq!(
            state["matches"][0]["bounds"],
            json!({"x":10,"y":20,"width":80,"height":20})
        );
        assert!(!state.to_string().contains("secret"));
    }

    #[test]
    fn truncated_search_cannot_confirm_a_unique_match() {
        let mut state = json!({"accessibility":{"truncated":true,"elements":[
            {"index":0,"name":"City","is_password":false,"offscreen":false}
        ]}});
        project(&mut state, &json!({"name":"City"}));
        assert_eq!(state["matched_count"], 1);
        assert_eq!(state["matched"], false);
    }
}
