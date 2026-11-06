$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../install.ps1')

function Assert-Bootstrap([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}
function Assert-BootstrapReject([scriptblock]$Action, [string]$Message) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-Bootstrap $rejected $Message
}

$previousLocale = $env:OMNI_LOCALE
try {
    foreach ($locale in @('es', 'en', 'fr', 'it')) {
        $env:OMNI_LOCALE = $locale
        $message = (Get-OmniInstallText 'installed') -f '1.0.0'
        Assert-Bootstrap ($message.Contains('1.0.0') -and $message.Contains('omni')) "Missing localized install message: $locale"
    }
    $env:OMNI_LOCALE = 'de'
    Assert-Bootstrap ((Get-OmniInstallText 'windowsRequired') -ceq 'This installer requires Windows x64.') 'Unsupported language did not fall back to English.'
} finally { $env:OMNI_LOCALE = $previousLocale }

$preview = [pscustomobject]@{ tag_name = 'v1.1.0-rc.1'; draft = $false; prerelease = $true; published_at = '2026-02-02T00:00:00Z' }
$stable = [pscustomobject]@{ tag_name = 'v1.0.0'; draft = $false; prerelease = $false; published_at = '2026-02-01T00:00:00Z' }
$draft = [pscustomobject]@{ tag_name = 'v9.0.0'; draft = $true; prerelease = $false; published_at = '2026-03-01T00:00:00Z' }
Assert-Bootstrap ((Select-OmniRelease @($preview, $stable, $draft) Stable '').tag_name -eq 'v1.0.0') 'Stable selected a preview or draft.'
Assert-Bootstrap ((Select-OmniRelease @($preview, $stable) Preview '').tag_name -eq 'v1.1.0-rc.1') 'Preview did not select the latest published release.'
Assert-Bootstrap ((Select-OmniRelease @($preview, $stable) Stable '1.1.0-rc.1').tag_name -eq $preview.tag_name) 'An explicitly selected candidate was lost.'
Assert-BootstrapReject { Select-OmniRelease @($preview) Stable '' } 'Stable silently fell back to preview.'
Assert-BootstrapReject { Select-OmniRelease @($draft) Preview '9.0.0' } 'A draft was installable.'

$digest = 'a' * 64
$manifest = [pscustomobject]@{
    schemaVersion = 1; repository = 'juancgomezm-s/OmniCore'; tag = 'v1.0.0'; version = '1.0.0'; commitSha = 'b' * 40
    assets = @([pscustomobject]@{ rid = 'win-x64'; name = 'omnicore-cli-1.0.0-win-x64.tgz'; sha256 = $digest; size = 100 })
}
Assert-Bootstrap ((Get-OmniManifestAsset $manifest 'v1.0.0').size -eq 100) 'Valid manifest rejected.'
$manifest.repository = 'someone/OmniCore'
Assert-BootstrapReject { Get-OmniManifestAsset $manifest 'v1.0.0' } 'A foreign repository manifest was accepted.'
$manifest.repository = 'juancgomezm-s/OmniCore'
Assert-BootstrapReject { Get-OmniManifestAsset $manifest 'v2.0.0' } 'A version/tag mismatch was accepted.'
$manifest.assets[0].name = '../outside.tgz'
Assert-BootstrapReject { Get-OmniManifestAsset $manifest 'v1.0.0' } 'A traversal filename was accepted.'
$manifest.assets[0].name = 'omnicore-cli-1.0.0-win-x64.tgz'
$manifest.assets[0].size = 513MB
Assert-BootstrapReject { Get-OmniManifestAsset $manifest 'v1.0.0' } 'An oversized package was accepted.'

$release = [pscustomobject]@{ tag_name = 'v1.0.0'; assets = @([pscustomobject]@{
    name = 'manifest.json'; state = 'uploaded'; browser_download_url = 'https://example.com/manifest.json'
}) }
Assert-BootstrapReject { Get-OmniReleaseAsset $release 'manifest.json' } 'A foreign download host was accepted.'
$release.assets[0].browser_download_url = 'https://github.com/juancgomezm-s/OmniCore/releases/download/v1.0.0/manifest.json'
Assert-Bootstrap ((Get-OmniReleaseAsset $release 'manifest.json').name -eq 'manifest.json') 'Official asset rejected.'
$release.assets += $release.assets[0]
Assert-BootstrapReject { Get-OmniReleaseAsset $release 'manifest.json' } 'Ambiguous assets were accepted.'

$result = [pscustomobject]@{ verificationResult = [pscustomobject]@{
    signature = [pscustomobject]@{ certificate = [pscustomobject]@{ sourceRepositoryIdentifier = '1386758159' } }
    statement = [pscustomobject]@{ subject = @([pscustomobject]@{ digest = [pscustomobject]@{ sha256 = $digest } }) }
} }
Assert-OmniPublisherResult @($result) $digest
$result.verificationResult.signature.certificate.sourceRepositoryIdentifier = '123'
Assert-BootstrapReject { Assert-OmniPublisherResult @($result) $digest } 'The permanent repository identity was not checked.'
$result.verificationResult.signature.certificate.sourceRepositoryIdentifier = '1386758159'
Assert-BootstrapReject { Assert-OmniPublisherResult @($result) ('c' * 64) } 'The attested file digest was not checked.'
Assert-BootstrapReject { Assert-OmniPublisherResult @() $digest } 'An empty attestation result was accepted.'

$info = New-OmniVerifierProcess 'C:\verifier\gh.exe' 'C:\test directory\package.tgz' ('b' * 40) 'v1.0.0' ([IO.Path]::GetTempPath())
Assert-Bootstrap ($info.Arguments.Contains('--deny-self-hosted-runners') -and $info.Arguments.Contains('--source-digest') -and
    $info.Arguments.Contains('refs/tags/v1.0.0') -and $info.Arguments.Contains('https://slsa.dev/provenance/v1')) 'The provenance policy is incomplete.'
Assert-Bootstrap (-not $info.EnvironmentVariables.ContainsKey('GH_TOKEN') -and
    -not $info.EnvironmentVariables.ContainsKey('OPENAI_API_KEY') -and -not $info.EnvironmentVariables.ContainsKey('GITHUB_TOKEN')) 'The verifier inherited credentials.'
Assert-Bootstrap ((ConvertTo-OmniNativeArgument 'C:\test directory\') -ceq '"C:\test directory\\"') 'Trailing path slash was not quoted correctly.'

$fixture = Join-Path ([IO.Path]::GetTempPath()) ('omnicore-bootstrap-test-' + [Guid]::NewGuid().ToString('N') + '.bin')
try {
    [IO.File]::WriteAllBytes($fixture, [byte[]]@(1, 2, 3))
    $hash = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-OmniFile $fixture $hash 3
    [IO.File]::WriteAllBytes($fixture, [byte[]]@(3, 2, 1))
    Assert-BootstrapReject { Assert-OmniFile $fixture $hash 3 } 'A same-size tampered package passed validation.'
} finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Force } }
Write-Output 'Bootstrap contracts passed: channel, identity, tamper, provenance, credentials and quoting.'
