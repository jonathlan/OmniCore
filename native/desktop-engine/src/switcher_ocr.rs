//! Local OCR of the verified shell selector. No image leaves the native helper.
use crate::server::{Error, Result};
use base64::Engine;
use image::imageops;
use serde_json::{Value, json};
use std::{
    collections::BTreeMap,
    io::{Read, Write},
    os::windows::process::CommandExt,
    path::PathBuf,
    process::{Command, Stdio},
    thread,
    time::{Duration, Instant},
};

const MAX_PIXELS: u64 = 20_000_000;

fn engine() -> Result<(PathBuf, PathBuf)> {
    let root = std::env::current_exe()
        .map_err(|_| Error::new("OCR_UNAVAILABLE", "No se encontró el ejecutable nativo."))?
        .with_file_name("ocr");
    let binary = root.join("tesseract.exe");
    let data = root.join("tessdata");
    if !binary.is_file()
        || ["spa", "eng"]
            .iter()
            .any(|l| !data.join(format!("{l}.traineddata")).is_file())
    {
        return Err(fail(
            "OCR_UNAVAILABLE",
            "Instala el motor OCR verificado de OmniCore.",
            json!({}),
        ));
    }
    Ok((binary, data))
}

fn run(args: &[&str], png: Vec<u8>) -> Result<String> {
    let (binary, data) = engine()?;
    let mut child = Command::new(&binary)
        .current_dir(binary.parent().unwrap())
        .args(args)
        .arg("--tessdata-dir")
        .arg(data)
        .env("OMP_THREAD_LIMIT", "1")
        .creation_flags(0x08000000)
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|_| {
            fail(
                "OCR_UNAVAILABLE",
                "The local Tesseract process could not start.",
                json!({}),
            )
        })?;
    let mut input = child.stdin.take().unwrap();
    let output = child.stdout.take().unwrap();
    let writer = thread::spawn(move || input.write_all(&png));
    let reader = thread::spawn(move || {
        let mut bytes = Vec::new();
        output.take(1024 * 1024 + 1).read_to_end(&mut bytes)?;
        Ok::<_, std::io::Error>(bytes)
    });
    let deadline = Instant::now() + Duration::from_secs(4);
    let status = loop {
        match child.try_wait() {
            Ok(Some(status)) => break Ok(status),
            Ok(None) if Instant::now() < deadline && !crate::indicator::cancelled() => {
                thread::sleep(Duration::from_millis(10))
            }
            other => {
                let _ = child.kill();
                let _ = child.wait();
                break Err(if let Err(error) = other {
                    Error::new("OCR_FAILED", &error.to_string())
                } else {
                    fail(
                        "OCR_TIMEOUT",
                        "Se agotó el tiempo del OCR del selector.",
                        json!({}),
                    )
                });
            }
        }
    };
    crate::indicator::check()?;
    let written = writer
        .join()
        .map_err(|_| Error::new("OCR_FAILED", "Falló la entrada del OCR."))?;
    let bytes = reader
        .join()
        .map_err(|_| Error::new("OCR_FAILED", "Falló la salida del OCR."))?
        .map_err(|_| Error::new("OCR_FAILED", "No se pudo leer el OCR."))?;
    if !status?.success() || written.is_err() || bytes.len() > 1024 * 1024 {
        return Err(fail(
            "OCR_FAILED",
            "Tesseract could not read the capture within the output budget.",
            json!({}),
        ));
    }
    String::from_utf8(bytes)
        .map_err(|_| Error::new("OCR_FAILED", "El OCR devolvió texto no válido."))
}

pub fn normalize(text: &str) -> String {
    text.trim_matches(|c: char| c.is_whitespace() || matches!(c, '[' | ']' | '|'))
        .split_whitespace()
        .collect::<Vec<_>>()
        .join(" ")
        .to_lowercase()
}

fn fail(code: &str, message: &str, _context: Value) -> Error {
    Error::new(code, message)
}

pub fn read(state: &Value) -> Result<Value> {
    let invalid = || {
        Error::new(
            "OCR_CAPTURE_REQUIRED",
            "No se pudo leer la captura del selector.",
        )
    };
    let encoded = state["screenshots"][0]["url"]
        .as_str()
        .and_then(|url| url.strip_prefix("data:image/png;base64,"))
        .ok_or_else(invalid)?;
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(encoded)
        .map_err(|_| invalid())?;
    let mut reader =
        image::ImageReader::with_format(std::io::Cursor::new(bytes), image::ImageFormat::Png);
    let mut limits = image::Limits::default();
    limits.max_alloc = Some(MAX_PIXELS * 4);
    reader.limits(limits);
    let mut pixels = reader.decode().map_err(|_| invalid())?.into_luma8();
    if pixels
        .as_raw()
        .iter()
        .step_by(23)
        .filter(|pixel| **pixel < 100)
        .count()
        > pixels.as_raw().len().div_ceil(23) / 2
    {
        imageops::invert(&mut pixels);
    }
    let scale = match pixels.width().max(pixels.height()) {
        0..=512 => 3,
        513..=1024 => 2,
        _ => 1,
    };
    if scale > 1 {
        pixels = imageops::resize(
            &pixels,
            pixels.width() * scale,
            pixels.height() * scale,
            imageops::FilterType::CatmullRom,
        );
    }
    let mut png = std::io::Cursor::new(Vec::new());
    pixels
        .write_to(&mut png, image::ImageFormat::Png)
        .map_err(|_| invalid())?;
    let tsv = run(
        &[
            "stdin",
            "stdout",
            "-l",
            "spa+eng",
            "--psm",
            "11",
            "-c",
            "tessedit_create_tsv=1",
        ],
        png.into_inner(),
    )?;
    let mut lines = BTreeMap::<[u32; 3], Vec<Value>>::new();
    for row in tsv.lines() {
        let fields: Vec<_> = row.splitn(12, '\t').collect();
        if fields.len() != 12 || fields[0] != "5" || fields[11].trim().is_empty() {
            continue;
        }
        let [
            Ok(block),
            Ok(paragraph),
            Ok(line),
            Ok(x),
            Ok(y),
            Ok(w),
            Ok(h),
        ] = [2, 3, 4, 6, 7, 8, 9].map(|i| fields[i].parse::<u32>())
        else {
            continue;
        };
        if fields[10]
            .parse::<f64>()
            .is_ok_and(|confidence| (75.0..=100.0).contains(&confidence))
        {
            lines.entry([block,paragraph,line]).or_default().push(json!({"text":fields[11],
                "bounds":{"x":x/scale,"y":y/scale,"width":w.div_ceil(scale),"height":h.div_ceil(scale)}}));
        }
    }
    Ok(
        json!({"candidates":lines.into_values().map(|words|json!({"words":words})).collect::<Vec<_>>()}),
    )
}
