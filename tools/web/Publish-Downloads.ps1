# Publie uniquement downloads/ (setup + versions.json) vers
# /var/www/Xtrem/xtrem/xtremscan/downloads — sans toucher au reste du site.
# Auth : XTREM_SSH_PW. Destination figee (D-SETUP-PUBLISH).
param(
    [string]$WebRoot = (Join-Path $PSScriptRoot "..\..\web"),
    [string]$Password = $env:XTREM_SSH_PW
)

$ErrorActionPreference = "Stop"

$sshHost = "192.168.1.38"
$sshUser = "root"
$sshPort = 22
$hostKey = "SHA256:933FdE4oKnoMMuTVpqoj4ylCO7fj2zYYdlnjv/gHEEE"
$remoteDownloads = "/var/www/Xtrem/xtrem/xtremscan/downloads"
$plink = "C:\Program Files\PuTTY\plink.exe"
$pscp = "C:\Program Files\PuTTY\pscp.exe"

if ($remoteDownloads -notmatch '/xtrem/xtremscan/downloads$') {
    throw "Cible refusee : $remoteDownloads"
}
if ($remoteDownloads -match 'xtremrec') {
    throw "Interdit : dossier downloads d'une autre app."
}

if (-not (Test-Path -LiteralPath $WebRoot)) {
    throw "Dossier web introuvable : $WebRoot"
}
if (-not (Test-Path -LiteralPath $plink) -or -not (Test-Path -LiteralPath $pscp)) {
    throw "PuTTY plink/pscp introuvables."
}
if ([string]::IsNullOrWhiteSpace($Password)) {
    throw "Definir XTREM_SSH_PW (ne pas ecrire le mot de passe dans le depot)."
}

$webRoot = (Resolve-Path -LiteralPath $WebRoot).Path
$localDownloads = Join-Path $webRoot "downloads"
$versions = Join-Path $localDownloads "versions.json"
$filesDir = Join-Path $localDownloads "files"

if (-not (Test-Path -LiteralPath $versions)) {
    throw "versions.json manquant : $versions"
}
if (-not (Test-Path -LiteralPath $filesDir)) {
    throw "Dossier files manquant : $filesDir"
}

$catalog = Get-Content -LiteralPath $versions -Raw -Encoding UTF8 | ConvertFrom-Json
if ($catalog.product -ne "Xtrem Scan") {
    throw "versions.json : produit '$($catalog.product)' au lieu de Xtrem Scan."
}

$latestFile = $null
if ($catalog.versions) {
    $latestEntry = @($catalog.versions) | Where-Object { $_.version -eq $catalog.latest } | Select-Object -First 1
    if ($latestEntry -and $latestEntry.file) {
        $latestFile = Split-Path -Leaf ([string]$latestEntry.file)
    }
}

if (-not $latestFile -or $latestFile -notlike "XtremScan-Setup-*.exe") {
    throw "Fichier latest inattendu : $latestFile"
}

$setupPath = Join-Path $filesDir $latestFile
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Setup latest introuvable : $setupPath (versions.json latest=$($catalog.latest))"
}

$setup = Get-Item -LiteralPath $setupPath
$remote = "${sshUser}@${sshHost}"
Write-Host "      downloads -> $remoteDownloads"

& $plink -ssh -pw $Password -P $sshPort -batch -hostkey $hostKey $remote "mkdir -p $remoteDownloads/files"
if ($LASTEXITCODE -ne 0) { throw "Dossier distant inaccessible ($remoteDownloads)." }

& $pscp -pw $Password -P $sshPort -batch -hostkey $hostKey $versions "${remote}:${remoteDownloads}/versions.json"
if ($LASTEXITCODE -ne 0) { throw "pscp versions.json a echoue." }

Write-Host "      $($setup.Name) ($([math]::Round($setup.Length / 1MB, 1)) Mo)"
& $pscp -pw $Password -P $sshPort -batch -hostkey $hostKey $setup.FullName "${remote}:${remoteDownloads}/files/$($setup.Name)"
if ($LASTEXITCODE -ne 0) { throw "pscp $($setup.Name) a echoue." }

$remoteCheck = "chown -R www-data:www-data $remoteDownloads && chmod 755 $remoteDownloads $remoteDownloads/files && chmod 644 $remoteDownloads/versions.json $remoteDownloads/files/$($setup.Name) && grep -q 'Xtrem Scan' $remoteDownloads/versions.json && ! grep -q 'Xtrem Rec' $remoteDownloads/versions.json && sha256sum $remoteDownloads/files/$($setup.Name) && cat $remoteDownloads/versions.json"
& $plink -ssh -pw $Password -P $sshPort -batch -hostkey $hostKey $remote $remoteCheck
if ($LASTEXITCODE -ne 0) { throw "Finalisation distante a echoue." }

Write-Host "      OK : https://xtrem.valloue.fr/xtremscan/downloads/"
