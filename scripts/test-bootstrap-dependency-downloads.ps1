# Opt-in verification of real official dependency installers. Never executes them or elevates.
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../install.ps1')
$staging = Join-Path ([IO.Path]::GetTempPath()) ('omnicore-bootstrap-dependencies-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$script:verifiedDependencies = New-Object 'Collections.Generic.List[object]'
function Invoke-OmniDependencyInstaller {
    param([string]$Name, [string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') { throw "Invalid real dependency signature: $Name ($($signature.Status))" }
    $script:verifiedDependencies.Add([pscustomobject]@{ name = $Name; file = [IO.Path]::GetFileName($Path);
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash;
        signer = $signature.SignerCertificate.Subject; status = 'Valid'; executed = $false })
}
$previousTls = [Net.ServicePointManager]::SecurityProtocol
try {
    [Net.ServicePointManager]::SecurityProtocol = $previousTls -bor [Net.SecurityProtocolType]::Tls12
    Install-OmniNodePrerequisite $staging
    Install-OmniDotnetPrerequisite $staging
    $receipt = Join-Path $staging 'receipt.json'
    $script:verifiedDependencies | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receipt -Encoding UTF8
    Write-Output "Official installer metadata, download hashes and Authenticode verified: $receipt"
} finally { [Net.ServicePointManager]::SecurityProtocol = $previousTls }
