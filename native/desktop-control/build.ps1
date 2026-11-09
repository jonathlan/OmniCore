param([string]$Cargo = 'cargo', [string]$OcrRuntime = $env:OMNICORE_DESKTOP_OCR_RUNTIME, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'Cargo.toml'
if (!$SkipTests) {
    & $Cargo test --locked --manifest-path (Join-Path $PSScriptRoot "../desktop-engine/Cargo.toml") --target-dir (Join-Path $PSScriptRoot "target")
    if ($LASTEXITCODE -ne 0) { throw 'Shared Rust engine tests failed.' }
    & $Cargo test --locked --manifest-path $manifest
    if ($LASTEXITCODE -ne 0) { throw 'Rust tests failed.' }
}
& $Cargo build --release --locked --manifest-path $manifest
if ($LASTEXITCODE -ne 0) { throw 'Rust build failed.' }
$executable = Join-Path $PSScriptRoot 'target/release/omnicore-desktop.exe'
$hash = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'desktop.sha256'), $hash, [Text.UTF8Encoding]::new($false))
$metadataText = & $Cargo metadata --locked --format-version 1 --manifest-path $manifest
if ($LASTEXITCODE -ne 0) { throw 'Cannot collect dependency notices.' }
$metadata = $metadataText | ConvertFrom-Json
$notices = Join-Path $PSScriptRoot 'licenses'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
$dependencies = @($metadata.packages | Where-Object { $_.source } | ForEach-Object {
    $package = $_
    $destination = Join-Path $notices ($package.name + '-' + $package.version)
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Get-ChildItem -LiteralPath (Split-Path $package.manifest_path) -File |
        Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|UNICODE_LICENSE)' } |
        Copy-Item -Destination $destination
    [ordered]@{name=$package.name; version=$package.version; license=$package.license; source=$package.source}
})
[IO.File]::WriteAllText((Join-Path $notices 'dependencies.json'), ($dependencies | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
if ($OcrRuntime) {
    $runtime = (Resolve-Path -LiteralPath $OcrRuntime).Path
    $ocrTarget = Join-Path $PSScriptRoot 'target/release/ocr'
    $pin = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ocr.sha256.json') -Raw | ConvertFrom-Json -AsHashtable
    foreach ($item in $pin.GetEnumerator()) {
        $source = Join-Path $runtime $item.Key
        if (!(Test-Path -LiteralPath $source -PathType Leaf) -or
            (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ine $item.Value) {
            throw "El runtime OCR no coincide con el archivo fijado: $($item.Key)"
        }
    }
    foreach ($relative in $pin.Keys) {
        $destination = Join-Path $ocrTarget $relative
        New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $runtime $relative) -Destination $destination
    }
    Copy-Item -LiteralPath (Join-Path $runtime 'licenses') -Destination $ocrTarget -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $runtime 'tessdata/LICENSE') -Destination (Join-Path $ocrTarget 'tessdata')
    Copy-Item -LiteralPath (Join-Path $runtime 'ocr-runtime.lock.json') -Destination $ocrTarget
}
Write-Output "Native desktop built and pinned: $hash"
