$ErrorActionPreference = 'Stop'
# Load the edition's own hash cmdlet even when the parallel npm run has no module cache.
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1')
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

$nodePlan = Get-OmniNodeInstaller @(
    [pscustomobject]@{ version = 'v99.0.0'; lts = $false; files = @('win-x64-msi') },
    [pscustomobject]@{ version = 'v24.1.0'; lts = 'LTS'; files = @('win-x64-msi') },
    [pscustomobject]@{ version = 'v26.0.0'; lts = 'LTS'; files = @('win-x64-msi') },
    [pscustomobject]@{ version = 'v27.0.0-rc.1'; lts = 'LTS'; files = @('win-x64-msi') }
)
Assert-Bootstrap ($nodePlan.Version -ceq 'v26.0.0' -and $nodePlan.Url -ceq 'https://nodejs.org/dist/v26.0.0/node-v26.0.0-x64.msi') 'Did not select a stable x64 Node LTS installer.'
Assert-BootstrapReject { Get-OmniNodeInstaller @([pscustomobject]@{ version = 'v26.0.0'; lts = 'LTS'; files = @('win-x86-msi') }) } 'An x86 installer was selected.'
$nodeSum = ('a' * 64) + '  node-v26.0.0-x64.msi'
Assert-Bootstrap ((Get-OmniNodeChecksum $nodeSum $nodePlan.Name) -ceq ('a' * 64)) 'Official checksum lookup failed.'
Assert-BootstrapReject { Get-OmniNodeChecksum $nodeSum 'other.msi' } 'A checksum for another file was accepted.'
Assert-BootstrapReject { Get-OmniNodeChecksum ($nodeSum + "`n" + $nodeSum) $nodePlan.Name } 'Ambiguous dependency checksums were accepted.'
$runtimeMetadata = [pscustomobject]@{
    'channel-version' = '10.0'; 'latest-runtime' = '10.0.1'
    releases = @([pscustomobject]@{ runtime = [pscustomobject]@{ version = '10.0.1'; files = @([pscustomobject]@{
        name = 'dotnet-runtime-win-x64.exe'; rid = 'win-x64'
        url = 'https://builds.dotnet.microsoft.com/dotnet/Runtime/10.0.1/dotnet-runtime-10.0.1-win-x64.exe'
        hash = 'b' * 128
    }) } })
}
Assert-Bootstrap ((Get-OmniDotnetInstaller $runtimeMetadata).Hash -ceq ('b' * 128)) 'Runtime installer metadata rejected.'
$runtimeMetadata.releases[0].runtime.files[0].url = 'https://example.com/runtime.exe'
Assert-BootstrapReject { Get-OmniDotnetInstaller $runtimeMetadata } 'A foreign runtime installer host was accepted.'
$runtimeMetadata.'channel-version' = '11.0'
Assert-BootstrapReject { Get-OmniDotnetInstaller $runtimeMetadata } 'A different .NET channel was selected.'
Assert-Bootstrap ((Get-OmniUpdatedUserPath '%USERPROFILE%\custom;C:\keep' 'C:\Windows' 'C:\OmniCore') -ceq
    '%USERPROFILE%\custom;C:\keep;C:\OmniCore') 'Unrelated PATH entries or expansion tokens were modified.'
Assert-Bootstrap ((Get-OmniUpdatedUserPath 'C:\keep' 'C:\OmniCore' 'c:\omnicore') -ceq 'C:\keep') 'A machine PATH entry was duplicated.'
Assert-Bootstrap ((Get-OmniUpdatedUserPath '' '' 'C:\OmniCore') -ceq 'C:\OmniCore') 'The first user PATH entry included an empty search directory.'

# Simulate missing prerequisites; these tests never launch an OS installer or elevate.
$dependencyFunctions = @{}
foreach ($name in @('Test-OmniNodePrerequisite', 'Test-OmniDotnetPrerequisite', 'Install-OmniNodePrerequisite',
    'Install-OmniDotnetPrerequisite', 'Update-OmniProcessPath')) {
    $dependencyFunctions[$name] = (Get-Item "Function:$name").ScriptBlock
}
try {
    function Test-OmniNodePrerequisite { return $script:fixtureNode }
    function Test-OmniDotnetPrerequisite { return $script:fixtureRuntime }
    function Install-OmniNodePrerequisite {
        param($Staging)
        $script:dependencyCalls.Add('node')
        if ($script:fixtureFailure) { throw 'simulated install cancellation' }
        if (-not $script:fixtureUnusable) { $script:fixtureNode = $true }
    }
    function Install-OmniDotnetPrerequisite { param($Staging) $script:dependencyCalls.Add('runtime'); $script:fixtureRuntime = $true }
    function Update-OmniProcessPath { $script:dependencyCalls.Add('path') }
    foreach ($state in @(@($true, $true, ''), @($false, $true, 'node,path'),
        @($true, $false, 'runtime,path'), @($false, $false, 'node,path,runtime,path'))) {
        $script:fixtureNode = $state[0]; $script:fixtureRuntime = $state[1]
        $script:fixtureFailure = $false; $script:fixtureUnusable = $false
        $script:dependencyCalls = New-Object 'Collections.Generic.List[string]'
        Initialize-OmniPrerequisites 'unused fixture directory'
        Assert-Bootstrap (($script:dependencyCalls -join ',') -ceq $state[2]) 'A compatible prerequisite was reinstalled or a missing prerequisite was skipped.'
    }
    $script:fixtureNode = $false; $script:fixtureRuntime = $false; $script:fixtureFailure = $true
    $script:dependencyCalls.Clear()
    Assert-BootstrapReject { Initialize-OmniPrerequisites 'unused' } 'A cancelled dependency install did not stop setup.'
    Assert-Bootstrap (($script:dependencyCalls -join ',') -ceq 'node') 'Setup continued after a cancelled prerequisite.'
    $script:fixtureFailure = $false; $script:fixtureUnusable = $true
    Assert-BootstrapReject { Initialize-OmniPrerequisites 'unused' } 'An unusable dependency passed the post-install probe.'
} finally {
    foreach ($name in $dependencyFunctions.Keys) { Set-Item "Function:$name" $dependencyFunctions[$name] }
}

$nativeFunctions = @{}
foreach ($name in @('Get-AuthenticodeSignature', 'Start-Process')) {
    $nativeFunctions[$name] = Get-Item "Function:$name" -ErrorAction SilentlyContinue
}
try {
    $script:fixtureSignature = 'Valid'; $script:fixtureInstallerExit = 0; $script:fixtureStarts = 0
    function Get-AuthenticodeSignature { param($LiteralPath) return [pscustomobject]@{ Status = $script:fixtureSignature } }
    function Start-Process {
        param($FilePath, $ArgumentList, $WindowStyle, $Verb, $ErrorAction, [switch]$Wait, [switch]$PassThru)
        $script:fixtureStarts++
        $script:installerArguments = $ArgumentList
        Assert-Bootstrap ($Wait -and $PassThru -and $WindowStyle -eq 'Hidden') 'Installer was not waited for or opened an unnecessary window.'
        $process = [pscustomobject]@{ ExitCode = $script:fixtureInstallerExit }
        $process | Add-Member -MemberType ScriptMethod -Name Dispose -Value {}
        return $process
    }
    Invoke-OmniDependencyInstaller 'fixture Node' 'C:\path with spaces\node.msi'
    Assert-Bootstrap ($script:installerArguments.Contains('"/i"') -and $script:installerArguments.Contains('"/quiet"') -and
        $script:installerArguments.Contains('"/norestart"')) 'Node MSI install arguments were incomplete.'
    $script:fixtureInstallerExit = 3010
    Invoke-OmniDependencyInstaller 'fixture runtime' 'C:\path with spaces\runtime.exe'
    Assert-Bootstrap ($script:installerArguments.Contains('"/install"') -and $script:installerArguments.Contains('"/norestart"')) 'Runtime install could force a restart or omitted install mode.'
    $script:fixtureInstallerExit = 1603
    Assert-BootstrapReject { Invoke-OmniDependencyInstaller 'fixture' 'C:\fixture.exe' } 'An installer failure was reported as success.'
    $before = $script:fixtureStarts; $script:fixtureSignature = 'NotSigned'
    Assert-BootstrapReject { Invoke-OmniDependencyInstaller 'fixture' 'C:\fixture.exe' } 'An unsigned dependency installer was launched.'
    Assert-Bootstrap ($script:fixtureStarts -eq $before) 'An unsigned dependency reached Start-Process.'
} finally {
    foreach ($name in $nativeFunctions.Keys) {
        if ($null -eq $nativeFunctions[$name]) { Remove-Item "Function:$name" }
        else { Set-Item "Function:$name" $nativeFunctions[$name].ScriptBlock }
    }
}

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
    $sha512 = (Get-FileHash -LiteralPath $fixture -Algorithm SHA512).Hash
    Assert-OmniFile $fixture $sha512 3 SHA512
} finally { if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture -Force } }
Write-Output 'Bootstrap contracts passed: channel, identity, tamper, provenance, credentials and quoting.'
