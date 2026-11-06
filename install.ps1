# Windows x64 bootstrap: install a published GitHub package without a source checkout.
[CmdletBinding()]
param(
    [ValidateSet('Stable', 'Preview')][string]$Channel = 'Stable',
    [string]$Version,
    [string]$Prefix,
    [switch]$NoPathPersistence
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
    "El archivo no coincide con su tama\u00f1o o hash: {0}",
    "File size or hash does not match: {0}",
    "La taille ou le hash du fichier ne correspond pas : {0}",
    "Dimensione o hash del file non corrisponde: {0}"
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
  "dependencyInstall": [
    "Instalando {0} desde su distribuci\u00f3n oficial...",
    "Installing {0} from its official distribution...",
    "Installation de {0} depuis sa distribution officielle...",
    "Installazione di {0} dalla distribuzione ufficiale..."
  ],
  "dependencyMetadata": [
    "Los metadatos oficiales de {0} no contienen un instalador x64 v\u00e1lido.",
    "Official metadata for {0} has no valid x64 installer.",
    "Les m\u00e9tadonn\u00e9es officielles de {0} ne contiennent pas d'installateur x64 valide.",
    "I metadati ufficiali di {0} non contengono un installer x64 valido."
  ],
  "dependencySignature": [
    "La firma de Windows del instalador no es v\u00e1lida: {0}",
    "The installer's Windows signature is not valid: {0}",
    "La signature Windows de l'installateur n'est pas valide : {0}",
    "La firma Windows dell'installer non \u00e8 valida: {0}"
  ],
  "dependencyFailed": [
    "La instalaci\u00f3n de {0} fall\u00f3 o se cancel\u00f3 (c\u00f3digo {1}).",
    "Installation of {0} failed or was cancelled (code {1}).",
    "L'installation de {0} a \u00e9chou\u00e9 ou a \u00e9t\u00e9 annul\u00e9e (code {1}).",
    "L'installazione di {0} non \u00e8 riuscita o \u00e8 stata annullata (codice {1})."
  ],
  "dependencyUnavailable": [
    "{0} sigue sin estar disponible despu\u00e9s de instalarlo. No se ha instalado OmniCore.",
    "{0} is still unavailable after installation. OmniCore has not been installed.",
    "{0} reste indisponible apr\u00e8s l'installation. OmniCore n'a pas \u00e9t\u00e9 install\u00e9.",
    "{0} non \u00e8 ancora disponibile dopo l'installazione. OmniCore non \u00e8 stato installato."
  ],
  "administratorConsent": [
    "Windows puede pedir autorizaci\u00f3n de administrador para instalar {0}.",
    "Windows may request administrator approval to install {0}.",
    "Windows peut demander une autorisation administrateur pour installer {0}.",
    "Windows pu\u00f2 richiedere l'autorizzazione amministratore per installare {0}."
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
    param([string]$Path, [string]$ExpectedHash, [long]$Size = 0,
        [ValidateSet('SHA256', 'SHA512')][string]$Algorithm = 'SHA256')
    $file = Get-Item -LiteralPath $Path -ErrorAction Stop
    if (($Size -gt 0 -and $file.Length -ne $Size) -or
        (Get-FileHash -LiteralPath $Path -Algorithm $Algorithm).Hash.ToLowerInvariant() -cne $ExpectedHash.ToLowerInvariant()) {
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

function Get-OmniNodeInstaller {
    param([object[]]$Index)
    $selected = $Index | Where-Object {
        $_.lts -is [string] -and $_.lts -and $_.version -cmatch '^v\d+\.\d+\.\d+$' -and
        [version]$_.version.Substring(1) -ge [version]'20.0.0' -and $_.files -contains 'win-x64-msi'
    } | Sort-Object { [version]$_.version.Substring(1) } -Descending | Select-Object -First 1
    if ($null -eq $selected) { throw ((Get-OmniInstallText 'dependencyMetadata') -f 'Node.js LTS') }
    $name = 'node-' + $selected.version + '-x64.msi'
    return [pscustomobject]@{ Version = $selected.version; Name = $name;
        Url = "https://nodejs.org/dist/$($selected.version)/$name";
        ChecksumsUrl = "https://nodejs.org/dist/$($selected.version)/SHASUMS256.txt" }
}

function Get-OmniNodeChecksum {
    param([string]$Checksums, [string]$Name)
    $matches = @($Checksums -split '\r?\n' | ForEach-Object {
        $entry = [regex]::Match($_, '^([a-fA-F0-9]{64})\s+\*?' + [regex]::Escape($Name) + '$')
        if ($entry.Success) { $entry.Groups[1].Value.ToLowerInvariant() }
    })
    if ($matches.Count -ne 1) { throw ((Get-OmniInstallText 'dependencyMetadata') -f 'Node.js LTS') }
    return $matches[0]
}

function Get-OmniDotnetInstaller {
    param($Metadata)
    $version = $Metadata.'latest-runtime'
    if ($Metadata.'channel-version' -cne '10.0' -or $version -cnotmatch '^10\.0\.\d+$') {
        throw ((Get-OmniInstallText 'dependencyMetadata') -f '.NET 10')
    }
    $releases = @($Metadata.releases | Where-Object { $_.runtime.version -ceq $version })
    if ($releases.Count -ne 1) { throw ((Get-OmniInstallText 'dependencyMetadata') -f '.NET 10') }
    $files = @($releases[0].runtime.files | Where-Object { $_.rid -ceq 'win-x64' -and $_.name -ceq 'dotnet-runtime-win-x64.exe' })
    $url = "https://builds.dotnet.microsoft.com/dotnet/Runtime/$version/dotnet-runtime-$version-win-x64.exe"
    if ($files.Count -ne 1 -or $files[0].url -cne $url -or $files[0].hash -notmatch '^[a-fA-F0-9]{128}$') {
        throw ((Get-OmniInstallText 'dependencyMetadata') -f '.NET 10')
    }
    return [pscustomobject]@{ Version = $version; Name = "dotnet-runtime-$version-win-x64.exe";
        Url = $url; Hash = $files[0].hash.ToLowerInvariant() }
}

function Test-OmniNodePrerequisite {
    $ErrorActionPreference = 'Continue'
    try {
        $node = Get-Command node.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        $npm = Get-Command npm.cmd -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $node -or $null -eq $npm) { return $false }
        $probe = & $node.Source -p "JSON.stringify({version:process.versions.node,arch:process.arch,platform:process.platform})" 2>$null
        if ($LASTEXITCODE -ne 0) { return $false }
        $data = ConvertFrom-Json -InputObject ($probe -join "`n") -ErrorAction Stop
        if ($data.version -notmatch '^\d+\.\d+\.\d+' -or [version]$data.version -lt [version]'20.0.0' -or
            $data.arch -cne 'x64' -or $data.platform -cne 'win32') { return $false }
        $npmVersion = & $npm.Source --version 2>$null
        return $LASTEXITCODE -eq 0 -and "$npmVersion" -match '^\d+\.\d+\.\d+'
    } catch { return $false }
}

function Test-OmniX64Executable {
    param([string]$Path)
    $reader = $null
    try {
        $reader = New-Object IO.BinaryReader([IO.File]::OpenRead($Path))
        if ($reader.BaseStream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5a4d) { return $false }
        $reader.BaseStream.Position = 0x3c
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $reader.BaseStream.Length - 6) { return $false }
        $reader.BaseStream.Position = $offset
        return $reader.ReadUInt32() -eq 0x4550 -and $reader.ReadUInt16() -eq 0x8664
    } catch { return $false }
    finally { if ($null -ne $reader) { $reader.Dispose() } }
}

function Test-OmniDotnetPrerequisite {
    $ErrorActionPreference = 'Continue'
    try {
        $dotnet = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -eq $dotnet -or -not (Test-OmniX64Executable $dotnet.Source)) { return $false }
        $runtimes = & $dotnet.Source --list-runtimes 2>$null
        return $LASTEXITCODE -eq 0 -and @($runtimes -match '^Microsoft\.NETCore\.App 10\.\d+\.\d+\s').Count -gt 0
    } catch { return $false }
}

function Update-OmniProcessPath {
    # Read registry changes made by the official installers without dropping custom session entries.
    $programFiles = $env:ProgramW6432
    if (-not $programFiles) { $programFiles = $env:ProgramFiles }
    $paths = @((Join-Path $programFiles 'nodejs'), (Join-Path $programFiles 'dotnet'))
    $paths += [Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';'
    $paths += [Environment]::GetEnvironmentVariable('Path', 'User') -split ';'
    $paths += $env:PATH -split ';'
    $seen = @{}
    $env:PATH = (@($paths | Where-Object {
        if (-not $_ -or $seen.ContainsKey($_)) { return $false }
        $seen[$_] = $true
        return $true
    })) -join ';'
}

function Invoke-OmniDependencyInstaller {
    param([string]$Name, [string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid') { throw ((Get-OmniInstallText 'dependencySignature') -f $Name) }
    $executable = $Path
    $arguments = @('/install', '/quiet', '/norestart')
    if ([IO.Path]::GetExtension($Path) -ieq '.msi') {
        $executable = Join-Path $env:SYSTEMROOT 'System32/msiexec.exe'
        $arguments = @('/i', $Path, '/quiet', '/norestart')
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
    finally { $identity.Dispose() }
    $options = @{ FilePath = $executable; ArgumentList = (($arguments | ForEach-Object { ConvertTo-OmniNativeArgument $_ }) -join ' ');
        Wait = $true; PassThru = $true; WindowStyle = 'Hidden'; ErrorAction = 'Stop' }
    if (-not $admin) {
        Write-Host ((Get-OmniInstallText 'administratorConsent') -f $Name)
        $options.Verb = 'RunAs'
    }
    $process = Start-Process @options
    try {
        if ($process.ExitCode -notin @(0, 3010)) { throw ((Get-OmniInstallText 'dependencyFailed') -f $Name, $process.ExitCode) }
    } finally { $process.Dispose() }
}

function Install-OmniNodePrerequisite {
    param([string]$Staging)
    $index = Invoke-RestMethod -Uri 'https://nodejs.org/dist/index.json' -TimeoutSec 60
    $plan = Get-OmniNodeInstaller @($index)
    $sums = Join-Path $Staging 'node-SHASUMS256.txt'
    Save-OmniDownload $plan.ChecksumsUrl $sums
    $hash = Get-OmniNodeChecksum (Get-Content -LiteralPath $sums -Raw) $plan.Name
    $file = Join-Path $Staging $plan.Name
    Save-OmniDownload $plan.Url $file 128MB
    Assert-OmniFile $file $hash
    Write-Host ((Get-OmniInstallText 'dependencyInstall') -f "Node.js LTS $($plan.Version)")
    Invoke-OmniDependencyInstaller 'Node.js LTS' $file
}

function Install-OmniDotnetPrerequisite {
    param([string]$Staging)
    $metadata = Invoke-RestMethod -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json' -TimeoutSec 60
    $plan = Get-OmniDotnetInstaller $metadata
    $file = Join-Path $Staging $plan.Name
    Save-OmniDownload $plan.Url $file 128MB
    Assert-OmniFile $file $plan.Hash 0 SHA512
    Write-Host ((Get-OmniInstallText 'dependencyInstall') -f ".NET Runtime $($plan.Version)")
    Invoke-OmniDependencyInstaller '.NET 10' $file
}

function Initialize-OmniPrerequisites {
    param([string]$Staging)
    if (-not (Test-OmniNodePrerequisite)) {
        Install-OmniNodePrerequisite $Staging
        Update-OmniProcessPath
        if (-not (Test-OmniNodePrerequisite)) { throw ((Get-OmniInstallText 'dependencyUnavailable') -f 'Node.js / npm') }
    }
    if (-not (Test-OmniDotnetPrerequisite)) {
        Install-OmniDotnetPrerequisite $Staging
        Update-OmniProcessPath
        if (-not (Test-OmniDotnetPrerequisite)) { throw ((Get-OmniInstallText 'dependencyUnavailable') -f '.NET 10 x64') }
    }
}

function Get-OmniUpdatedUserPath {
    param([string]$Current, [string]$Machine, [string]$Directory)
    $expanded = [Environment]::ExpandEnvironmentVariables($Current)
    if ((($Machine + ';' + $expanded) -split ';') -icontains $Directory) { return $Current }
    if (-not $Current.TrimEnd(';')) { return $Directory }
    return $Current.TrimEnd(';') + ';' + $Directory
}

function Add-OmniUserCommandPath {
    param([string]$Directory)
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Environment')
    try {
        $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
        $original = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if ($key.GetValueNames() -contains 'Path') { $kind = $key.GetValueKind('Path') }
        $machine = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        $updated = Get-OmniUpdatedUserPath $original $machine $Directory
        if ($updated -ceq $original) { return }
        # The API broadcasts WM_SETTINGCHANGE. Preserve the original registry value kind as well.
        [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
        $key.SetValue('Path', $updated, $kind)
    } finally { $key.Dispose() }
}

function Install-OmniCore {
    [CmdletBinding()]
    param([ValidateSet('Stable', 'Preview')][string]$Channel = 'Stable', [string]$Version, [string]$Prefix,
        [switch]$NoPathPersistence)
    $ErrorActionPreference = 'Stop'
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitOperatingSystem) {
        throw (Get-OmniInstallText 'windowsRequired')
    }
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

        # Verify the selected OmniCore package before installing any missing prerequisite.
        Initialize-OmniPrerequisites $staging
        $npmExecutable = (Get-Command npm.cmd -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source

        $prefixArgs = @()
        if ($Prefix) { $prefixArgs = @('--prefix', [IO.Path]::GetFullPath($Prefix)) }
        Write-Host ((Get-OmniInstallText 'installing') -f $manifest.version)
        # No lifecycle script, source compilation, Git checkout or provider request is involved.
        $ErrorActionPreference = 'Continue'
        & $npmExecutable install --global --ignore-scripts --no-audit --no-fund @prefixArgs $archive
        $installExit = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($installExit -ne 0) { throw ((Get-OmniInstallText 'npmFailed') -f $installExit) }
        $commandDirectory = & $npmExecutable prefix --global @prefixArgs
        if ($LASTEXITCODE -ne 0) { throw (Get-OmniInstallText 'prefixMissing') }
        $commandDirectory = "$commandDirectory".Trim()
        $installed = Get-Content -LiteralPath (Join-Path $commandDirectory 'node_modules/omnicore-cli/package.json') -Raw | ConvertFrom-Json
        if ($installed.name -cne 'omnicore-cli' -or $installed.version -cne $manifest.version) { throw (Get-OmniInstallText 'installedMismatch') }
        if (($env:PATH -split ';') -inotcontains $commandDirectory) { $env:PATH = $commandDirectory + ';' + $env:PATH }
        if (-not $NoPathPersistence) { Add-OmniUserCommandPath $commandDirectory }
        Write-Host ((Get-OmniInstallText 'installed') -f $manifest.version)
        Write-Host ((Get-OmniInstallText 'command') -f (Join-Path $commandDirectory 'omni.cmd'))
        if ($Prefix -and $NoPathPersistence) { Write-Host (Get-OmniInstallText 'customPath') }
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
