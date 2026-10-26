[CmdletBinding()]
param([Parameter(Mandatory = $true)] [string] $ArtifactRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ArtifactRoot).Path)
[xml]$productProps = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'Directory.Build.props')
$version = [string]$productProps.Project.PropertyGroup.Version
$productDirectory = "FaultWitness-$version-win-x64"
$directory = Join-Path $root $productDirectory
$zip = Join-Path $root "$productDirectory.zip"
$checksum = Join-Path $root 'SHA256.txt'
$summaryPath = Join-Path $root 'release-summary.json'
if (-not (Test-Path $directory -PathType Container)) { throw 'Versioned product directory is missing.' }
if (-not (Test-Path $zip -PathType Leaf)) { throw 'Versioned ZIP is missing.' }
if (-not (Test-Path $checksum -PathType Leaf)) { throw 'SHA-256 sidecar is missing.' }
if (-not (Test-Path $summaryPath -PathType Leaf)) { throw 'Release summary is missing.' }
$summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
if ($summary.artifact -ne (Split-Path $zip -Leaf) -or $summary.version -ne $version -or $summary.rid -ne 'win-x64') { throw 'Release summary identity is invalid.' }
$manifestPath = Join-Path $directory 'app/docs/release-manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.product -ne 'FaultWitness' -or $manifest.version -ne $version -or $manifest.rid -ne 'win-x64') { throw 'Release manifest identity is invalid.' }
if ([string]::IsNullOrWhiteSpace($manifest.sourceRevision)) { throw 'Release manifest has no source revision.' }
if ($manifest.sourceRevision -notmatch '^[0-9a-f]{7,64}$') { throw 'Release manifest source revision is invalid.' }
foreach ($required in @('app/docs/LICENSE', 'app/docs/THIRD-PARTY-NOTICES.txt', 'README.md', 'QUICKSTART.md', 'FaultWitness.exe', 'app/FaultWitness.exe')) {
    if (-not (Test-Path (Join-Path $directory $required) -PathType Leaf)) { throw "Required release document is missing: $required" }
}
foreach ($pattern in @('app/docs/third-party/*skiasharp*/*', 'app/docs/third-party/*angle*/*', 'app/docs/third-party/*runtimepack*/*')) {
    if (@(Get-ChildItem $directory -File -Recurse | Where-Object { ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') -like $pattern }).Count -eq 0) { throw "Required dependency notice payload is missing: $pattern" }
}
$forbidden = '(?i)(^|[\\/])(\.git|tests?|fixtures?|reports?|private|source)([\\/]|$)|\.(pdb|dmp|mdmp|log|cs|fs|vb|slnx|csproj)$'
foreach ($file in Get-ChildItem $directory -File -Recurse) {
    $relative = $file.FullName.Substring($directory.Length).TrimStart('\','/')
    if ($relative -match $forbidden) { throw "Forbidden artifact content: $relative" }
}
if (@(Compare-Object @('FaultWitness.exe','README.md','QUICKSTART.md','app') @(Get-ChildItem -LiteralPath $directory | ForEach-Object Name)).Count -ne 0) { throw 'Packaging C root topology is invalid.' }
$manifestPaths = @($manifest.files | ForEach-Object { [string]$_.path })
$actualPaths = @(Get-ChildItem $directory -File -Recurse | ForEach-Object { ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') } | Where-Object { $_ -ne 'app/docs/release-manifest.json' })
if ($manifestPaths.Count -ne $actualPaths.Count -or @(Compare-Object $manifestPaths $actualPaths).Count -ne 0) { throw 'Manifest inventory does not exactly match packaged files.' }
foreach ($item in @($manifest.files)) {
    $manifestFile = Join-Path $directory ([string]$item.path)
    if (-not (Test-Path $manifestFile -PathType Leaf)) { throw "Manifest file is missing: $($item.path)" }
    if ((Get-FileHash $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$item.sha256) { throw "Manifest hash mismatch: $($item.path)" }
    if ((Get-Item $manifestFile).Length -ne [long]$item.bytes) { throw "Manifest byte count mismatch: $($item.path)" }
}
if ($manifest.filesExcludeManifest -ne $true -or $manifest.manifestFile -ne 'app/docs/release-manifest.json') { throw 'Manifest self-exclusion contract is invalid.' }
$expectedHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$recordedHash = ((Get-Content $checksum -Raw) -split '\s+')[0].ToLowerInvariant()
if ($expectedHash -ne $recordedHash) { throw 'ZIP checksum does not match.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if (@($archive.Entries | Where-Object FullName -eq "$productDirectory/app/docs/release-manifest.json").Count -ne 1) { throw 'ZIP root directory or manifest is invalid.' }
    foreach ($required in @('app/docs/LICENSE', 'app/docs/THIRD-PARTY-NOTICES.txt', 'README.md', 'QUICKSTART.md', 'FaultWitness.exe', 'app/FaultWitness.exe')) {
        if (@($archive.Entries | Where-Object FullName -eq "$productDirectory/$required").Count -ne 1) { throw "ZIP is missing $required." }
    }
    $zipPaths = @($archive.Entries | ForEach-Object FullName)
    $expectedZipPaths = @((Get-ChildItem $directory -File -Recurse | ForEach-Object { "$productDirectory/" + ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') }))
    if ($zipPaths.Count -ne $expectedZipPaths.Count -or @(Compare-Object $zipPaths $expectedZipPaths).Count -ne 0) { throw 'ZIP inventory does not exactly match the staged product directory.' }
    $zipFileCount = @($archive.Entries | Where-Object { -not $_.FullName.EndsWith('/') }).Count
    if ([long]$summary.fileCount -ne $zipFileCount -or $summary.fileCountBasis -ne 'zip-file-entries') { throw 'Release summary file count does not match the ZIP file entries.' }
    if ([long]$summary.manifestFileCount -ne $manifestPaths.Count -or $summary.manifestExcludesItself -ne $true -or $zipFileCount -ne ($manifestPaths.Count + 1)) { throw 'Release summary manifest count does not match the ZIP inventory.' }
    foreach ($entry in $archive.Entries) { if ($entry.FullName -match $forbidden) { throw "Forbidden ZIP content: $($entry.FullName)" } }
} finally { $archive.Dispose() }
Write-Output 'Release artifact content audit passed.'
