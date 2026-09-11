[CmdletBinding()]
param([Parameter(Mandatory = $true)] [string] $ArtifactRoot)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $ArtifactRoot).Path)
$directory = Join-Path $root 'FaultWitness-0.9.0-beta.1-win-x64'
$zip = Join-Path $root 'FaultWitness-0.9.0-beta.1-win-x64.zip'
$checksum = "$zip.sha256"
if (-not (Test-Path $directory -PathType Container)) { throw 'Versioned product directory is missing.' }
if (-not (Test-Path $zip -PathType Leaf)) { throw 'Versioned ZIP is missing.' }
if (-not (Test-Path $checksum -PathType Leaf)) { throw 'SHA-256 sidecar is missing.' }
$manifestPath = Join-Path $directory 'release-manifest.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.product -ne 'FaultWitness' -or $manifest.version -ne '0.9.0-beta.1' -or $manifest.rid -ne 'win-x64') { throw 'Release manifest identity is invalid.' }
if ([string]::IsNullOrWhiteSpace($manifest.sourceRevision)) { throw 'Release manifest has no source revision.' }
if ($manifest.sourceRevision -notmatch '^[0-9a-f]{7,64}$') { throw 'Release manifest source revision is invalid.' }
foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.txt', 'README.md', 'QUICKSTART.md')) {
    if (-not (Test-Path (Join-Path $directory $required) -PathType Leaf)) { throw "Required release document is missing: $required" }
}
foreach ($pattern in @('third-party/*skiasharp*/*', 'third-party/*angle*/*', 'third-party/*runtimepack*/*')) {
    if (@(Get-ChildItem $directory -File -Recurse | Where-Object { ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') -like $pattern }).Count -eq 0) { throw "Required dependency notice payload is missing: $pattern" }
}
$forbidden = '(?i)(^|[\\/])(\.git|tests?|fixtures?|reports?|private|source)([\\/]|$)|\.(pdb|dmp|mdmp|log|cs|fs|vb|slnx|csproj)$'
foreach ($file in Get-ChildItem $directory -File -Recurse) {
    $relative = $file.FullName.Substring($directory.Length).TrimStart('\','/')
    if ($relative -match $forbidden) { throw "Forbidden artifact content: $relative" }
}
$manifestPaths = @($manifest.files | ForEach-Object { [string]$_.path })
$actualPaths = @(Get-ChildItem $directory -File -Recurse | ForEach-Object { ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') } | Where-Object { $_ -ne 'release-manifest.json' })
if ($manifestPaths.Count -ne $actualPaths.Count -or @(Compare-Object $manifestPaths $actualPaths).Count -ne 0) { throw 'Manifest inventory does not exactly match packaged files.' }
foreach ($item in @($manifest.files)) {
    $manifestFile = Join-Path $directory ([string]$item.path)
    if (-not (Test-Path $manifestFile -PathType Leaf)) { throw "Manifest file is missing: $($item.path)" }
    if ((Get-FileHash $manifestFile -Algorithm SHA256).Hash.ToLowerInvariant() -ne [string]$item.sha256) { throw "Manifest hash mismatch: $($item.path)" }
    if ((Get-Item $manifestFile).Length -ne [long]$item.bytes) { throw "Manifest byte count mismatch: $($item.path)" }
}
if ($manifest.filesExcludeManifest -ne $true -or $manifest.manifestFile -ne 'release-manifest.json') { throw 'Manifest self-exclusion contract is invalid.' }
$expectedHash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$recordedHash = ((Get-Content $checksum -Raw) -split '\s+')[0].ToLowerInvariant()
if ($expectedHash -ne $recordedHash) { throw 'ZIP checksum does not match.' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    if (@($archive.Entries | Where-Object FullName -eq 'FaultWitness-0.9.0-beta.1-win-x64/release-manifest.json').Count -ne 1) { throw 'ZIP root directory or manifest is invalid.' }
    foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.txt', 'README.md', 'QUICKSTART.md')) {
        if (@($archive.Entries | Where-Object FullName -eq "FaultWitness-0.9.0-beta.1-win-x64/$required").Count -ne 1) { throw "ZIP is missing $required." }
    }
    $zipPaths = @($archive.Entries | ForEach-Object FullName)
    $expectedZipPaths = @((Get-ChildItem $directory -File -Recurse | ForEach-Object { 'FaultWitness-0.9.0-beta.1-win-x64/' + ($_.FullName.Substring($directory.Length).TrimStart('\','/') -replace '\\', '/') }))
    if ($zipPaths.Count -ne $expectedZipPaths.Count -or @(Compare-Object $zipPaths $expectedZipPaths).Count -ne 0) { throw 'ZIP inventory does not exactly match the staged product directory.' }
    foreach ($entry in $archive.Entries) { if ($entry.FullName -match $forbidden) { throw "Forbidden ZIP content: $($entry.FullName)" } }
} finally { $archive.Dispose() }
Write-Output 'Release artifact content audit passed.'
