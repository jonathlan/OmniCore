# Opt-in acceptance of a real published package; never install into the user's prefix.
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [ValidateSet('PowerShell', 'Npm')][string]$Method = 'PowerShell'
)
$ErrorActionPreference = 'Stop'
$bootstrap = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../install.ps1'))
$root = Join-Path ([IO.Path]::GetTempPath()) ('omnicore-bootstrap-live-' + [Guid]::NewGuid().ToString('N'))
$original = @{}
Get-ChildItem Env: | ForEach-Object { $original[$_.Name] = $_.Value }
$allow = @('PATH', 'PATHEXT', 'SYSTEMROOT', 'WINDIR', 'COMSPEC', 'TEMP', 'TMP', 'SYSTEMDRIVE', 'PROCESSOR_ARCHITECTURE')
New-Item -ItemType Directory -Path $root | Out-Null
try {
    Get-ChildItem Env: | Where-Object { $_.Name -notin $allow } | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null) }
    foreach ($name in @('home', 'roaming', 'local', 'config', 'data', 'workspace', 'bin')) {
        New-Item -ItemType Directory -Path (Join-Path $root $name) | Out-Null
    }
    $env:HOME = Join-Path $root 'home'
    $env:USERPROFILE = $env:HOME
    $env:APPDATA = Join-Path $root 'roaming'
    $env:LOCALAPPDATA = Join-Path $root 'local'
    $env:OMNICORE_CONFIG_DIR = Join-Path $root 'config'
    $env:OMNICORE_DATA_DIR = Join-Path $root 'data'
    $env:OMNICORE_AGENT_HOME = Join-Path $root 'agent-home'
    $env:DOTNET_CLI_HOME = $env:HOME
    $env:NPM_CONFIG_CACHE = Join-Path $root 'npm-cache'
    $env:NPM_CONFIG_USERCONFIG = Join-Path $root 'user.npmrc'
    $env:NPM_CONFIG_GLOBALCONFIG = Join-Path $root 'global.npmrc'
    [IO.File]::WriteAllText($env:NPM_CONFIG_USERCONFIG, '')
    [IO.File]::WriteAllText($env:NPM_CONFIG_GLOBALCONFIG, '')
    # Fail if the bootstrap or an npm lifecycle hook attempts a source build/clone.
    foreach ($tool in @('git', 'cargo', 'rustc')) {
        [IO.File]::WriteAllText((Join-Path $root "bin/$tool.cmd"), "@echo off`r`necho called > `"$root\source-build-attempt.txt`"`r`nexit /b 99`r`n")
    }
    $env:PATH = (Join-Path $root 'bin') + ';' + $env:PATH
    $prefix = Join-Path $root 'installation with spaces'
    Push-Location -LiteralPath (Join-Path $root 'workspace')
    try {
        if ($Method -eq 'Npm') {
            $number = $Version.TrimStart('v')
            $url = "https://github.com/juancgomezm-s/OmniCore/releases/download/v$number/omnicore-cli-$number-win-x64.tgz"
            $ErrorActionPreference = 'Continue'
            & npm.cmd install --global --ignore-scripts --no-audit --no-fund --prefix $prefix $url
            $npmExit = $LASTEXITCODE
            $ErrorActionPreference = 'Stop'
            if ($npmExit -ne 0) { throw "Direct npm installation failed: $npmExit" }
        } else {
            # Match the README's downloaded-script invocation: no PSScriptRoot or clone is available.
            & ([scriptblock]::Create([IO.File]::ReadAllText($bootstrap))) -Version $Version -Prefix $prefix
        }
        if (Test-Path -LiteralPath (Join-Path $root 'source-build-attempt.txt')) { throw 'A source tool was invoked.' }
        $package = Join-Path $prefix 'node_modules/omnicore-cli'
        $identity = Get-Content -LiteralPath (Join-Path $package 'package.json') -Raw | ConvertFrom-Json
        if ($identity.version -cne $Version.TrimStart('v')) { throw 'Wrong installed version.' }
        foreach ($name in @('dist/npm/native/omnicore-desktop.exe', 'dist/npm/verifier/gh.exe', 'dist/npm/skills/desktop-control/skill.yaml')) {
            if (-not (Test-Path -LiteralPath (Join-Path $package $name) -PathType Leaf)) { throw "Missing $name" }
        }
        & (Join-Path $prefix 'omni.cmd') --help
        if ($LASTEXITCODE -ne 0) { throw 'Installed command failed.' }
        $receipt = [ordered]@{ version = $identity.version; method = $Method; prefix = $prefix; commandExitCode = 0;
            gitOrRustInvoked = $false; userPrefixModified = $false; providerCredentialsPassed = $false;
            nativeDesktopIncluded = $true; directoryWithSpaces = $true }
        $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'receipt.json') -Encoding UTF8
        Write-Host "Verified live bootstrap receipt: $(Join-Path $root 'receipt.json')"
    } finally { Pop-Location }
} finally {
    Get-ChildItem Env: | ForEach-Object { [Environment]::SetEnvironmentVariable($_.Name, $null) }
    foreach ($name in $original.Keys) { [Environment]::SetEnvironmentVariable($name, $original[$name]) }
}
