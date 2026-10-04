[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $PublishRoot,
    [Parameter(Mandatory = $true)] [string] $OutputRoot,
    [string] $Version = '0.9.0-beta.2',
    [Parameter(Mandatory = $true)] [string] $LauncherPath,
    [string] $SourceRevision = '',
    [string] $Rid = 'win-x64'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Relative([string] $Root, [string] $Path) {
    return $Path.Substring($Root.Length).TrimStart('\', '/') -replace '\\', '/'
}

function Assert-Candidate([string] $Root) {
    $errors = [System.Collections.Generic.List[string]]::new()
    $forbidden = '(?i)(^|/)(\.git|tests?|fixtures?|reports?|private|source)(/|$)|\.(pdb|dmp|mdmp|log|cs|fs|vb|slnx|csproj)$'
    foreach ($file in Get-ChildItem -LiteralPath $Root -File -Recurse) {
        $relative = Get-Relative $Root $file.FullName
        if ($relative -match $forbidden) { [void]$errors.Add("Forbidden release content: $relative") }
        if ($file.Extension -ieq '.json' -and $file.Name -notin @('FaultWitness.deps.json','FaultWitness.runtimeconfig.json','FaultWitness.ElevatedHelper.deps.json','FaultWitness.ElevatedHelper.runtimeconfig.json','FaultWitness.ElevatedHelper.payload.json')) {
            [void]$errors.Add("Unapproved JSON in release content: $relative")
        }
    }
    foreach ($required in @('FaultWitness.exe','FaultWitness.dll','FaultWitness.deps.json','FaultWitness.runtimeconfig.json','helper/FaultWitness.ElevatedHelper.exe','helper/FaultWitness.ElevatedHelper.dll','helper/FaultWitness.ElevatedHelper.payload.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Root $required) -PathType Leaf)) { [void]$errors.Add("Missing release file: $required") }
    }
    if ($errors.Count -gt 0) { throw ($errors -join [Environment]::NewLine) }
}

function New-DeterministicZip([string] $Source, [string] $ZipPath, [string] $EntryPrefix) {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($ZipPath, [IO.FileMode]::Create, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            foreach ($file in @(Get-ChildItem -LiteralPath $Source -File -Recurse | Sort-Object { Get-Relative $Source $_.FullName })) {
                $relative = Get-Relative $Source $file.FullName
                $entry = $archive.CreateEntry("$EntryPrefix/$relative", [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $input = [IO.File]::OpenRead($file.FullName)
                $output = $entry.Open()
                try { $input.CopyTo($output) } finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

$publish = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $PublishRoot).Path)
$output = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null
Assert-Candidate $publish
if ([string]::IsNullOrWhiteSpace($SourceRevision)) { $SourceRevision = (git -C (Split-Path $publish -Parent) rev-parse HEAD).Trim() }
if ($SourceRevision -notmatch '^[0-9a-f]{7,64}$') { throw 'SourceRevision must be a git SHA.' }

$productDirectory = "FaultWitness-$Version-$Rid"
$staging = Join-Path $output $productDirectory
if (Test-Path -LiteralPath $staging) { throw 'Use a fresh OutputRoot; existing release staging is never overwritten.' }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
foreach ($file in @(Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object { Get-Relative $publish $_.FullName })) {
    $relative = Get-Relative $publish $file.FullName
    $target = Join-Path (Join-Path $staging 'app') ($relative -replace '/', [IO.Path]::DirectorySeparatorChar)
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $file.FullName -Destination $target
}
$repoRoot = Split-Path -Parent $PSScriptRoot
$documents = Join-Path $staging 'app/docs'
New-Item -ItemType Directory -Path $documents -Force | Out-Null
Copy-Item -LiteralPath $LauncherPath -Destination (Join-Path $staging 'FaultWitness.exe')
$launcherInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $staging 'FaultWitness.exe'))
if ($launcherInfo.ProductVersion -ne "$Version+$SourceRevision") { throw 'Launcher version/source revision does not match the package.' }
foreach ($document in @('LICENSE', 'THIRD-PARTY-NOTICES.txt', 'SECURITY.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $document) -Destination (Join-Path $documents $document)
}
foreach ($document in @('capture-next-crash-security.md', 'code-signing-policy.md', 'change-history-sources.md', 'release-notes-0.9.0-beta.2.md')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs/$document") -Destination (Join-Path $documents $document)
}
New-Item -ItemType Directory -Path (Join-Path $documents 'images') -Force | Out-Null
foreach ($image in @('incidents-beta2.png', 'incident-detail-beta2.png')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot "docs/images/$image") -Destination (Join-Path $documents "images/$image")
}
$readme = (Get-Content -LiteralPath (Join-Path $repoRoot 'README.md') -Raw).Replace('](docs/', '](app/docs/').Replace('](LICENSE)', '](app/docs/LICENSE)').Replace('](THIRD-PARTY-NOTICES.txt)', '](app/docs/THIRD-PARTY-NOTICES.txt)').Replace('](SECURITY.md)', '](app/docs/SECURITY.md)')
Set-Content -LiteralPath (Join-Path $staging 'README.md') -Value $readme -Encoding UTF8
@'
# FaultWitness Quick Start

## Quick start

1. Extract the full ZIP package to a folder and keep its subfolders in place.
2. Open `FaultWitness.exe` from the extracted folder.
3. Open **Analyze → Analyze** and choose a recent period (24 hours, 7 days, 30 days or a custom range up to 90 days), or investigate around a known incident time.
4. Results depend on the records Windows retained and the sources FaultWitness could examine. A quiet result does not prove that the PC is fault-free.
5. **System → Diagnostic Readiness** is optional source investigation. It helps explain source status and access; it is not a prerequisite health scan.

Use **Analyze → Capture next crash** for the optional capture workflow. The `app/helper` folder is required for administrator-approved crash-capture configuration. Keep the complete `app` folder beside the root launcher; do not launch the helper directly. See `README.md` for diagnostic scope and privacy details.
'@ | Set-Content -LiteralPath (Join-Path $staging 'QUICKSTART.md') -Encoding UTF8
$noticeRoot = Join-Path $documents 'third-party'
$deps = @(Get-ChildItem -LiteralPath $publish -Filter '*.deps.json' -File -Recurse | ForEach-Object { (Get-Content $_.FullName -Raw | ConvertFrom-Json).libraries.PSObject.Properties.Name })
$packageKeys = @($deps | Where-Object { $_ -match '(?i)^(skiasharp|skiasharp\.nativeassets\.win32|avalonia\.angle\.windows\.natives|runtimepack\.microsoft\.netcore\.app\.runtime\.win-x64)/' })
foreach ($key in $packageKeys) {
    $parts = $key -split '/', 2; $package = $parts[0]; $packageVersion = $parts[1]
    $packageRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
    $cachePackage = $package -replace '(?i)^runtimepack\.', ''
    $packagePath = Join-Path (Join-Path $packageRoot $cachePackage.ToLowerInvariant()) $packageVersion
    $licenseFiles = @()
    if (Test-Path $packagePath) { $licenseFiles = @(Get-ChildItem $packagePath -File -Recurse | Where-Object Name -match '(?i)^(license|licence|copying|third-party-notices)(\..*)?$') }
    if ($licenseFiles.Count -eq 0) { throw "No license/notice payload found for $key" }
    $target = Join-Path $noticeRoot (($package + '-' + $packageVersion) -replace '[^A-Za-z0-9.-]', '_')
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    foreach ($license in $licenseFiles | Sort-Object FullName) { Copy-Item $license.FullName (Join-Path $target $license.Name) }
}
$manifestFiles = @(Get-ChildItem -LiteralPath $staging -File -Recurse | Sort-Object { Get-Relative $staging $_.FullName } | ForEach-Object {
    [ordered]@{ path = Get-Relative $staging $_.FullName; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $_.Length }
})
$manifest = [ordered]@{ product = 'FaultWitness'; version = $Version; rid = $Rid; sourceRevision = $SourceRevision; manifestFile = 'app/docs/release-manifest.json'; filesExcludeManifest = $true; files = $manifestFiles }
$manifest | ConvertTo-Json -Depth 8 -Compress | Set-Content -LiteralPath (Join-Path $documents 'release-manifest.json') -Encoding UTF8
$zip = Join-Path $output "$productDirectory.zip"
if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists.' }
New-DeterministicZip $staging $zip $productDirectory
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $output 'SHA256.txt') -Value "$hash  $productDirectory.zip" -Encoding ASCII
@([ordered]@{ artifact = (Split-Path $zip -Leaf); productDirectory = $productDirectory; version = $Version; rid = $Rid; sourceRevision = $SourceRevision; sha256 = $hash; fileCount = $manifestFiles.Count } | ConvertTo-Json -Compress) | Set-Content -LiteralPath (Join-Path $output 'release-summary.json') -Encoding UTF8
Write-Output $zip
