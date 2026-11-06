# Windows x64 bootstrap: install a published GitHub package without a source checkout.
[CmdletBinding()]
param(
    [ValidateSet('Stable', 'Preview')][string]$Channel = 'Stable',
    [string]$Version,
    [string]$Prefix
)

$script:OmniInstallMessages = ConvertFrom-Json @'
{
  "noRelease": [
    "No hay una release publicada para esa selecci\u00f3n. Para las candidatas usa -Channel Preview.",
    "No published release matches. For candidates use -Channel Preview.",
    "Aucune version publi\u00e9e ne correspond. Pour les candidates, utilisez -Channel Preview.",
    "Nessuna versione pubblicata corrisponde. Per le candidate usa -Channel Preview."
  ],
  "tagInvalid": [
    "El tag de la release no tiene un formato admitido.",
    "Unsupported release tag format.",
    "Format de tag non pris en charge.",
    "Formato del tag non supportato."
  ],
  "assetMissing": [
    "La release no contiene un asset oficial inequ\u00edvoco: {0}",
    "The release has no unique official asset: {0}",
    "La version ne contient pas de fichier officiel unique : {0}",
    "La versione non contiene un file ufficiale univoco: {0}"
  ],
  "manifestInvalid": [
    "La identidad o revisi\u00f3n del manifest no corresponde a la release elegida.",
    "Manifest identity or revision does not match the selected release.",
    "L'identit\u00e9 ou la r\u00e9vision du manifeste ne correspond pas \u00e0 la version choisie.",
    "Identit\u00e0 o revisione del manifest non corrisponde alla versione scelta."
  ],
  "packageInvalid": [
    "El manifest no contiene un paquete Windows x64 v\u00e1lido.",
    "The manifest has no valid Windows x64 package.",
    "Le manifeste ne contient pas de paquet Windows x64 valide.",
    "Il manifest non contiene un pacchetto Windows x64 valido."
  ],
  "fileMismatch": [
    "El archivo no coincide con su tama\u00f1o o SHA-256: {0}",
    "File size or SHA-256 does not match: {0}",
    "La taille ou le SHA-256 du fichier ne correspond pas : {0}",
    "Dimensione o SHA-256 del file non corrisponde: {0}"
  ],
  "publisherInvalid": [
    "La attestation no acredita la identidad permanente del repositorio y el archivo esperado.",
    "The attestation does not verify the permanent repository identity and expected file.",
    "L'attestation ne v\u00e9rifie pas l'identit\u00e9 permanente du d\u00e9p\u00f4t et le fichier attendu.",
    "L'attestazione non verifica l'identit\u00e0 permanente del repository e il file atteso."
  ],
  "verifierStart": [
    "No se pudo iniciar el verificador.",
    "Could not start the verifier.",
    "Impossible de d\u00e9marrer le v\u00e9rificateur.",
    "Impossibile avviare il verificatore."
  ],
  "verifierTimeout": [
    "El verificador agot\u00f3 su tiempo de ejecuci\u00f3n.",
    "Verifier timed out.",
    "Le v\u00e9rificateur a d\u00e9pass\u00e9 le d\u00e9lai.",
    "Il verificatore ha superato il tempo limite."
  ],
  "verifierFailed": [
    "No se pudo verificar el publicador: {0}",
    "Publisher verification failed: {0}",
    "\u00c9chec de la v\u00e9rification de l'\u00e9diteur : {0}",
    "Verifica dell'autore non riuscita: {0}"
  ],
  "downloadSize": [
    "El archivo descargado excede el tama\u00f1o admitido.",
    "Downloaded file size is outside the allowed range.",
    "La taille du fichier t\u00e9l\u00e9charg\u00e9 d\u00e9passe les limites autoris\u00e9es.",
    "La dimensione del file scaricato supera i limiti consentiti."
  ],
  "windowsRequired": [
    "Este instalador requiere Windows x64.",
    "This installer requires Windows x64.",
    "Cet installateur n\u00e9cessite Windows x64.",
    "Questo installer richiede Windows x64."
  ],
  "nodeRequired": [
    "Se requiere Node.js 20 o posterior con npm.",
    "Node.js 20 or later with npm is required.",
    "Node.js 20 ou version ult\u00e9rieure avec npm est requis.",
    "\u00c8 richiesto Node.js 20 o successivo con npm."
  ],
  "archRequired": [
    "Se requiere Node.js para Windows x64.",
    "Node.js for Windows x64 is required.",
    "Node.js pour Windows x64 est requis.",
    "\u00c8 richiesto Node.js per Windows x64."
  ],
  "runtimeRequired": [
    "Instala el runtime .NET 10 para Windows x64.",
    "Install the .NET 10 runtime for Windows x64.",
    "Installez le runtime .NET 10 pour Windows x64.",
    "Installa il runtime .NET 10 per Windows x64."
  ],
  "versionInvalid": [
    "La versi\u00f3n solicitada no es v\u00e1lida.",
    "Invalid requested version.",
    "La version demand\u00e9e n'est pas valide.",
    "La versione richiesta non \u00e8 valida."
  ],
  "sizeMismatch": [
    "El tama\u00f1o publicado del paquete no coincide con el manifest.",
    "Published package size does not match the manifest.",
    "La taille publi\u00e9e du paquet ne correspond pas au manifeste.",
    "La dimensione pubblicata del pacchetto non corrisponde al manifest."
  ],
  "installing": [
    "Instalando OmniCore {0}, paquete y publicador verificados...",
    "Installing OmniCore {0}, package and publisher verified...",
    "Installation d'OmniCore {0}, paquet et \u00e9diteur v\u00e9rifi\u00e9s...",
    "Installazione di OmniCore {0}, pacchetto e autore verificati..."
  ],
  "npmFailed": [
    "npm no complet\u00f3 la instalaci\u00f3n (c\u00f3digo {0}).",
    "npm did not complete installation (exit code {0}).",
    "npm n'a pas termin\u00e9 l'installation (code {0}).",
    "npm non ha completato l'installazione (codice {0})."
  ],
  "prefixMissing": [
    "No se pudo consultar el prefijo instalado.",
    "Could not query the installed prefix.",
    "Impossible de consulter le pr\u00e9fixe install\u00e9.",
    "Impossibile leggere il prefisso installato."
  ],
  "installedMismatch": [
    "La identidad instalada no coincide con la release.",
    "Installed identity does not match the release.",
    "L'identit\u00e9 install\u00e9e ne correspond pas \u00e0 la version.",
    "L'identit\u00e0 installata non corrisponde alla versione."
  ],
  "installed": [
    "OmniCore {0} instalado. Ejecuta omni.cmd para abrirlo en esta terminal.",
    "OmniCore {0} installed. Run omni.cmd to open it in this terminal.",
    "OmniCore {0} install\u00e9. Ex\u00e9cutez omni.cmd pour l'ouvrir dans ce terminal.",
    "OmniCore {0} installato. Esegui omni.cmd per aprirlo in questo terminale."
  ],
  "command": [
    "Comando: {0}",
    "Command: {0}",
    "Commande : {0}",
    "Comando: {0}"
  ],
  "customPath": [
    "Para pr\u00f3ximas terminales, agrega ese prefijo personalizado al PATH del usuario.",
    "For future terminals, add this custom prefix to your user PATH.",
    "Pour les prochains terminaux, ajoutez ce pr\u00e9fixe personnalis\u00e9 au PATH utilisateur.",
    "Per i prossimi terminali, aggiungi questo prefisso personalizzato al PATH utente."
  ]
}
'@

function Get-OmniInstallText {
    param([string]$Key)
    $language = $env:OMNI_LOCALE
    if (-not $language) { $language = [Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName }
    $language = ($language -split '-')[0].ToLowerInvariant()
    $indexes = @{ es = 0; en = 1; fr = 2; it = 3 }
    if (-not $indexes.ContainsKey($language)) { $language = 'en' }
    return $script:OmniInstallMessages.$Key[$indexes[$language]]
}

function Select-OmniRelease {
    param([object[]]$Releases, [string]$Channel, [string]$Version)
    $candidates = @($Releases | Where-Object { -not $_.draft -and $_.published_at })
    if ($Version) {
        $tag = 'v' + $Version.TrimStart('v')
        $candidates = @($candidates | Where-Object { $_.tag_name -ceq $tag })
    } elseif ($Channel -eq 'Stable') {
        $candidates = @($candidates | Where-Object { -not $_.prerelease })
    }
    $release = $candidates | Sort-Object published_at -Descending | Select-Object -First 1
    if ($null -eq $release) {
        throw (Get-OmniInstallText 'noRelease')
    }
    if ($release.tag_name -cnotmatch '^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-[0-9A-Za-z.-]+)?$') {
        throw (Get-OmniInstallText 'tagInvalid')
    }
    return $release
}

function Get-OmniReleaseAsset {
    param($Release, [string]$Name)
    $assets = @($Release.assets | Where-Object { $_.name -ceq $Name -and $_.state -eq 'uploaded' })
    $expected = 'https://github.com/juancgomezm-s/OmniCore/releases/download/' + $Release.tag_name + '/' + $Name
    if ($assets.Count -ne 1 -or $assets[0].browser_download_url -cne $expected) {
        throw ((Get-OmniInstallText 'assetMissing') -f $Name)
    }
    return $assets[0]
}

function Get-OmniManifestAsset {
    param($Manifest, [string]$Tag)
    $version = $Tag.Substring(1)
    if ($Manifest.schemaVersion -ne 1 -or $Manifest.repository -cne 'juancgomezm-s/OmniCore' -or
        $Manifest.tag -cne $Tag -or $Manifest.version -cne $version -or
        $Manifest.commitSha -cnotmatch '^[0-9a-f]{40}$') {
        throw (Get-OmniInstallText 'manifestInvalid')
    }
    $assets = @($Manifest.assets | Where-Object { $_.rid -ceq 'win-x64' })
    if ($assets.Count -ne 1 -or $assets[0].name -cne "omnicore-cli-$version-win-x64.tgz" -or
        $assets[0].sha256 -cnotmatch '^[0-9a-f]{64}$' -or
        [long]$assets[0].size -lt 1 -or [long]$assets[0].size -gt 512MB) {
        throw (Get-OmniInstallText 'packageInvalid')
    }
    return $assets[0]
}

function Assert-OmniFile {
    param([string]$Path, [string]$Sha256, [long]$Size = 0)
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if (($Size -gt 0 -and $file.Length -ne $Size) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() -cne $Sha256) {
        throw ((Get-OmniInstallText 'fileMismatch') -f $file.Name)
    }
}

function ConvertTo-OmniNativeArgument {
    param([string]$Value)
    # Windows CommandLineToArgvW quoting, including spaces, quotes and trailing slashes.
    $escaped = [regex]::Replace($Value, '(\\*)"', '$1$1\"')
    return '"' + [regex]::Replace($escaped, '(\\+)$', '$1$1') + '"'
}

function New-OmniVerifierProcess {
    param([string]$Executable, [string]$File, [string]$Commit, [string]$Tag, [string]$Staging)
    $arguments = @('attestation', 'verify', $File, '--bundle', "$File.sigstore.jsonl",
        '--repo', 'juancgomezm-s/OmniCore',
        '--signer-workflow', 'juancgomezm-s/OmniCore/.github/workflows/release.yml',
        '--signer-digest', $Commit, '--source-digest', $Commit, '--source-ref', "refs/tags/$Tag",
        '--deny-self-hosted-runners', '--predicate-type', 'https://slsa.dev/provenance/v1', '--format', 'json')
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = ($arguments | ForEach-Object { ConvertTo-OmniNativeArgument $_ }) -join ' '
    $info.WorkingDirectory = $Staging
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.EnvironmentVariables.Clear()
    foreach ($name in @('SYSTEMROOT', 'WINDIR', 'PATH', 'PATHEXT', 'COMSPEC', 'TEMP', 'TMP',
        'SSL_CERT_FILE', 'SSL_CERT_DIR')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $info.EnvironmentVariables[$name] = $value }
    }
    $info.EnvironmentVariables['GH_CONFIG_DIR'] = Join-Path $Staging 'gh-config'
    $info.EnvironmentVariables['GH_PROMPT_DISABLED'] = '1'
    $info.EnvironmentVariables['GH_NO_UPDATE_NOTIFIER'] = '1'
    return $info
}

function Assert-OmniPublisherResult {
    param([object[]]$Results, [string]$Sha256)
    foreach ($result in $Results) {
        $verification = $result.verificationResult
        if ($verification.signature.certificate.sourceRepositoryIdentifier -cne '1386758159') { continue }
        foreach ($subject in $verification.statement.subject) {
            if ($subject.digest.sha256 -ceq $Sha256) { return }
        }
    }
    throw (Get-OmniInstallText 'publisherInvalid')
}

function Invoke-OmniPublisherVerification {
    param([string]$Executable, [string]$File, [string]$Commit, [string]$Tag, [string]$Staging)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = New-OmniVerifierProcess $Executable $File $Commit $Tag $Staging
    try {
        if (-not $process.Start()) { throw (Get-OmniInstallText 'verifierStart') }
        $output = $process.StandardOutput.ReadToEndAsync()
        $errors = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(120000)) {
            $process.Kill()
            throw (Get-OmniInstallText 'verifierTimeout')
        }
        $stdout = $output.GetAwaiter().GetResult()
        $stderr = $errors.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) { throw ((Get-OmniInstallText 'verifierFailed') -f $stderr) }
        Assert-OmniPublisherResult @(ConvertFrom-Json -InputObject $stdout) (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant()
    } finally { $process.Dispose() }
}

function Save-OmniDownload {
    param([string]$Uri, [string]$Path, [long]$MaximumBytes = 1MB)
    Invoke-WebRequest -UseBasicParsing -Uri $Uri -OutFile $Path -TimeoutSec 120 -MaximumRedirection 3
    $size = (Get-Item -LiteralPath $Path).Length
    if ($size -lt 1 -or $size -gt $MaximumBytes) { throw (Get-OmniInstallText 'downloadSize') }
}

function Install-OmniCore {
    [CmdletBinding()]
    param([ValidateSet('Stable', 'Preview')][string]$Channel = 'Stable', [string]$Version, [string]$Prefix)
    $ErrorActionPreference = 'Stop'
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw (Get-OmniInstallText 'windowsRequired') }
    Get-Command node, npm.cmd, dotnet -ErrorAction Stop | Out-Null
    $nodeVersion = & node -p "process.versions.node"
    if ($LASTEXITCODE -ne 0 -or [int]($nodeVersion -split '\.')[0] -lt 20) { throw (Get-OmniInstallText 'nodeRequired') }
    $architecture = & node -p "process.platform + '-' + process.arch"
    if ($LASTEXITCODE -ne 0 -or $architecture -cne 'win32-x64') { throw (Get-OmniInstallText 'archRequired') }
    $runtimes = & dotnet --list-runtimes
    if ($LASTEXITCODE -ne 0 -or -not ($runtimes -match '^Microsoft\.NETCore\.App 10\.')) { throw (Get-OmniInstallText 'runtimeRequired') }
    if ($Version -and $Version -cnotmatch '^v?\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') { throw (Get-OmniInstallText 'versionInvalid') }

    $tls = [Net.ServicePointManager]::SecurityProtocol
    $staging = Join-Path ([IO.Path]::GetTempPath()) ('omnicore-bootstrap-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $staging | Out-Null
    try {
        [Net.ServicePointManager]::SecurityProtocol = $tls -bor [Net.SecurityProtocolType]::Tls12
        $api = 'https://api.github.com/repos/juancgomezm-s/OmniCore/releases'
        if ($Version) { $api += '/tags/v' + $Version.TrimStart('v') } else { $api += '?per_page=100' }
        $releases = Invoke-RestMethod -Uri $api -Headers @{ 'User-Agent' = 'OmniCore-Installer' } -TimeoutSec 60
        $release = Select-OmniRelease @($releases) $Channel $Version
        $manifestName = 'omnicore-release-manifest.json'
        $manifestFile = Join-Path $staging $manifestName
        foreach ($name in @($manifestName, "$manifestName.sigstore.jsonl")) {
            $asset = Get-OmniReleaseAsset $release $name
            Save-OmniDownload $asset.browser_download_url (Join-Path $staging $name)
        }
        $manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
        $package = Get-OmniManifestAsset $manifest $release.tag_name
        $packageAsset = Get-OmniReleaseAsset $release $package.name
        if ([long]$packageAsset.size -ne [long]$package.size) { throw (Get-OmniInstallText 'sizeMismatch') }

        # This verifier is downloaded from its official release and pinned independently of the package.
        $ghZip = Join-Path $staging 'gh.zip'
        Save-OmniDownload 'https://github.com/cli/cli/releases/download/v2.103.3/gh_2.103.3_windows_amd64.zip' $ghZip 64MB
        Assert-OmniFile $ghZip '98d0cfa794721665d7973dd728c78aff7bd836d58f10f6d7a03568eccb0a22e0'
        Expand-Archive -LiteralPath $ghZip -DestinationPath (Join-Path $staging 'verifier')
        $gh = Join-Path $staging 'verifier/bin/gh.exe'
        Assert-OmniFile $gh 'd303d8ac3f5e9438bf7c977b64dd6425513eae46218f8881d30694f28c1d0dfd'
        Invoke-OmniPublisherVerification $gh $manifestFile $manifest.commitSha $release.tag_name $staging

        $archive = Join-Path $staging $package.name
        Save-OmniDownload $packageAsset.browser_download_url $archive 512MB
        $bundle = Get-OmniReleaseAsset $release ($package.name + '.sigstore.jsonl')
        Save-OmniDownload $bundle.browser_download_url ($archive + '.sigstore.jsonl')
        Assert-OmniFile $archive $package.sha256 ([long]$package.size)
        Invoke-OmniPublisherVerification $gh $archive $manifest.commitSha $release.tag_name $staging

        $prefixArgs = @()
        if ($Prefix) { $prefixArgs = @('--prefix', [IO.Path]::GetFullPath($Prefix)) }
        Write-Host ((Get-OmniInstallText 'installing') -f $manifest.version)
        # No lifecycle script, source compilation, Git checkout or provider request is involved.
        $ErrorActionPreference = 'Continue'
        & npm.cmd install --global --ignore-scripts --no-audit --no-fund @prefixArgs $archive
        $installExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($installExit -ne 0) { throw ((Get-OmniInstallText 'npmFailed') -f $installExit) }
        $commandDirectory = & npm.cmd prefix --global @prefixArgs
        if ($LASTEXITCODE -ne 0) { throw (Get-OmniInstallText 'prefixMissing') }
        $commandDirectory = "$commandDirectory".Trim()
        $installed = Get-Content -LiteralPath (Join-Path $commandDirectory 'node_modules/omnicore-cli/package.json') -Raw | ConvertFrom-Json
        if ($installed.name -cne 'omnicore-cli' -or $installed.version -cne $manifest.version) { throw (Get-OmniInstallText 'installedMismatch') }
        if (($env:PATH -split ';') -inotcontains $commandDirectory) { $env:PATH = $commandDirectory + ';' + $env:PATH }
        Write-Host ((Get-OmniInstallText 'installed') -f $manifest.version)
        Write-Host ((Get-OmniInstallText 'command') -f (Join-Path $commandDirectory 'omni.cmd'))
        if ($Prefix) { Write-Host (Get-OmniInstallText 'customPath') }
    } finally {
        [Net.ServicePointManager]::SecurityProtocol = $tls
        # Only remove the unique direct child this invocation created beneath the resolved temp directory.
        $resolved = [IO.Path]::GetFullPath($staging)
        $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
        if ([IO.Path]::GetDirectoryName($resolved).TrimEnd('\', '/') -ieq $tempRoot -and
            [IO.Path]::GetFileName($resolved) -match '^omnicore-bootstrap-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($MyInvocation.InvocationName -ne '.') { Install-OmniCore @PSBoundParameters }
