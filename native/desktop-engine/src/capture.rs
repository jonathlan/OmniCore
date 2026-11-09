use crate::{
    native,
    server::{Error, Result},
};
use serde_json::{Value, json};

pub fn map_coordinate(frame: &Value, value: i64, horizontal: bool) -> Result<i64> {
    let (dimension, physical, origin) = if horizontal {
        ("width", "physicalWidth", "originX")
    } else {
        ("height", "physicalHeight", "originY")
    };
    let size = frame[dimension].as_i64().unwrap_or(0);
    let physical = frame[physical].as_i64().unwrap_or(0);
    if size <= 0 || physical <= 0 || value < 0 || value >= size {
        return Err(Error::new(
            "OUTSIDE_TARGET",
            "El punto queda fuera de la imagen observada.",
        ));
    }
    Ok(frame[origin].as_i64().unwrap_or(0) + value * physical / size)
}

pub fn capture(data: &Value) -> Result<Value> {
    let hwnd = data["hwnd"]
        .as_i64()
        .ok_or_else(|| Error::new("INVALID_PARAMS", "Falta el HWND."))? as isize;
    native::validate_target(hwnd)?;
    if native::bounds(native::rect(hwnd)?) != data["bounds"] {
        return Err(Error::new(
            "WINDOW_CHANGED",
            "Cambió la geometría del destino.",
        ));
    }
    let scale = data
        .get("image_scale")
        .map_or(Some(1.0), Value::as_f64)
        .filter(|v| v.is_finite() && (0.1..=1.0).contains(v))
        .ok_or_else(|| Error::new("INVALID_PARAMS", "image_scale debe estar entre 0,1 y 1."))?;
    let backend = data["capture_backend"].as_str().unwrap_or("wgc");
    let path = data["output_path"]
        .as_str()
        .ok_or_else(|| Error::new("INVALID_PARAMS", "Falta la ruta de salida."))?;
    let (mut image, mut metadata) = match backend {
        "printwindow" => native::capture(data)?,
        "screen_gdi" if data["visible_pixels_allowed"] == true => native::capture(data)?,
        "wgc" => {
            let frame = crate::capture_wgc::capture(hwnd as usize)
                .map_err(|e| Error::new("WGC_FAILED", &format!("{e:#}")))?;
            let (width, height) = frame.image.dimensions();
            (
                image::DynamicImage::ImageRgba8(frame.image),
                json!({"bounds":{"x":frame.origin_x,"y":frame.origin_y,"width":width,"height":height},
                    "backend":"wgc","occlusion_sensitive":false}),
            )
        }
        _ => {
            return Err(Error::new(
                "INVALID_PARAMS",
                "capture_backend must be wgc or printwindow; desktop pixel capture is excluded.",
            ));
        }
    };
    if let Some(region) = data.get("screenshot_region") {
        let [x, y, width, height] = region_in_frame(region, &data["bounds"], &metadata["bounds"])?;
        image = image.crop_imm(x, y, width, height);
        let bounds = &mut metadata["bounds"];
        bounds["x"] = json!(bounds["x"].as_i64().unwrap() + i64::from(x));
        bounds["y"] = json!(bounds["y"].as_i64().unwrap() + i64::from(y));
        bounds["width"] = json!(width);
        bounds["height"] = json!(height);
    }
    if backend != "wgc" {
        let black = image.as_bytes().iter().all(|b| *b == 0);
        metadata["all_black"] = json!(black);
        metadata["warnings"] = if black {
            json!([
                "La captura está en negro. El contenido puede estar protegido o usar un modo de dibujo incompatible."
            ])
        } else {
            json!([])
        };
    }
    metadata["physicalWidth"] = json!(image.width());
    metadata["physicalHeight"] = json!(image.height());
    let image = scale_image(image, scale);
    metadata["width"] = json!(image.width());
    metadata["height"] = json!(image.height());
    metadata["image_scale"] = json!(scale);
    metadata["capture_scope"] = json!("window");
    metadata["window_id"] = json!(format!("0x{hwnd:x}"));
    metadata["path"] = json!(path);
    let mut output = std::io::Cursor::new(Vec::new());
    image
        .write_to(&mut output, image::ImageFormat::Png)
        .map_err(|_| Error::new("CAPTURE_FAILED", "No se pudo codificar el PNG."))?;
    if output.get_ref().len() > 8 * 1024 * 1024 {
        return Err(Error::new("IMAGE_TOO_LARGE", "El PNG supera 8 MiB."));
    }
    std::fs::write(path, output.into_inner())
        .map_err(|_| Error::new("CAPTURE_FAILED", "No se pudo escribir el PNG."))?;
    Ok(metadata)
}

fn region_in_frame(region: &Value, window: &Value, frame: &Value) -> Result<[u32; 4]> {
    let invalid = || {
        Error::new(
            "INVALID_PARAMS",
            "screenshot_region debe quedar dentro de la ventana y del área capturada.",
        )
        .context(json!({"window_bounds":window,"capture_bounds":frame}))
    };
    let coordinate = |key: &str| {
        region[key]
            .as_i64()
            .filter(|v| (0..=100000).contains(v))
            .ok_or_else(invalid)
    };
    let (x, y, width, height) = (
        coordinate("x")?,
        coordinate("y")?,
        coordinate("width")?,
        coordinate("height")?,
    );
    if width == 0
        || height == 0
        || x + width > window["width"].as_i64().unwrap_or(0)
        || y + height > window["height"].as_i64().unwrap_or(0)
    {
        return Err(invalid());
    }
    // WGC can omit the invisible border present in GetWindowRect.
    let x = x + window["x"].as_i64().unwrap_or(0) - frame["x"].as_i64().unwrap_or(0);
    let y = y + window["y"].as_i64().unwrap_or(0) - frame["y"].as_i64().unwrap_or(0);
    if x < 0
        || y < 0
        || x + width > frame["width"].as_i64().unwrap_or(0)
        || y + height > frame["height"].as_i64().unwrap_or(0)
    {
        return Err(invalid());
    }
    Ok([x as u32, y as u32, width as u32, height as u32])
}

fn scale_image(image: image::DynamicImage, scale: f64) -> image::DynamicImage {
    if scale == 1.0 {
        return image;
    }
    let w = ((image.width() as f64 * scale).round() as u32).max(1);
    let h = ((image.height() as f64 * scale).round() as u32).max(1);
    image.resize_exact(w, h, image::imageops::FilterType::Triangle)
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn rgba_png_reduction_matches_retained_dimensions() {
        let image = image::RgbaImage::from_pixel(624, 392, image::Rgba([173, 216, 230, 255]));
        let scaled = scale_image(image::DynamicImage::ImageRgba8(image), 0.8);
        assert_eq!((scaled.width(), scaled.height()), (499, 314));
    }
    #[test]
    fn region_is_window_relative_and_cannot_include_missing_borders() {
        let window = json!({"x":-1000,"y":200,"width":640,"height":400});
        let frame = json!({"x":-993,"y":200,"width":626,"height":393});
        let region = json!({"x":30,"y":80,"width":140,"height":40});
        assert_eq!(
            region_in_frame(&region, &window, &frame).unwrap(),
            [23, 80, 140, 40]
        );
        let missing_border = json!({"x":0,"y":80,"width":140,"height":40});
        assert!(region_in_frame(&missing_border, &window, &frame).is_err());
        let outside = json!({"x":600,"y":80,"width":140,"height":40});
        assert!(region_in_frame(&outside, &window, &frame).is_err());
    }
    #[test]
    fn scaled_coordinates_use_retained_origin_and_dimensions() {
        let frame = json!({"width":499,"height":314,"physicalWidth":624,"physicalHeight":392,"originX":100,"originY":200});
        assert_eq!(map_coordinate(&frame, 70, true).unwrap(), 187);
        assert_eq!(map_coordinate(&frame, 109, false).unwrap(), 336);
        assert!(map_coordinate(&frame, 499, true).is_err());
        assert!(map_coordinate(&frame, -1, false).is_err());
    }
    #[test]
    fn image_click_geometry_is_consistent_at_dpi_scales_and_monitor_origins() {
        for dpi in [1.0f64, 1.25, 1.5] {
            for scale in [1.0f64, 0.8, 0.5] {
                for origin in [-1920i64, 0, 1920] {
                    let physical = (640.0 * dpi).round() as i64;
                    let width = (physical as f64 * scale).round() as i64;
                    let target = (120.0 * dpi).round() as i64;
                    let image = target * width / physical;
                    let frame = json!({"width":width,"physicalWidth":physical,"originX":origin});
                    let mapped = map_coordinate(&frame, image, true).unwrap();
                    assert!((mapped - (origin + target)).abs() <= physical / width + 1);
                }
            }
        }
    }
}
