[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $OutputRoot,

    [string] $SourceRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path)
}

function Test-PathWithin([string] $Path, [string] $Parent) {
    $child = [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $root = [IO.Path]::GetFullPath($Parent).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    return $child.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or $child.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Copy-IsolatedSource([string] $Source, [string] $Destination) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Push-Location $Source
    try {
        $tracked = @(git ls-files --cached --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'git ls-files failed.' }
    }
    finally { Pop-Location }

    foreach ($relative in $tracked) {
        if ([string]::IsNullOrWhiteSpace($relative) -or $relative -match '(^|[\/])(bin|obj)([\/]|$)') { continue }
        $sourcePath = Join-Path $Source $relative
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) { continue }
        $targetPath = Join-Path $Destination $relative
        $targetParent = Split-Path -Parent $targetPath
        New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
    }
}

function Invoke-Publish([string] $Source, [string] $Output, [string] $Rid, [string] $LogPath) {
    New-Item -ItemType Directory -Path $Output -Force | Out-Null
    $project = Join-Path $Source 'src/FaultWitness.App/FaultWitness.App.csproj'
    $publishArgs = @(
        'publish', $project,
        '--configuration', 'Release',
        '--runtime', $Rid,
        '--self-contained', 'true',
        '-p:PublishTrimmed=false',
        '-p:PublishSingleFile=false',
        '-p:DebugType=None',
        "-p:SourceRevisionId=$($script:SourceRevision)",
        "-p:PublishDir=$([IO.Path]::GetFullPath($Output) + [IO.Path]::DirectorySeparatorChar)"
    )
    & dotnet @publishArgs *> $LogPath
    return [int]$LASTEXITCODE
}

function Read-PeMachine([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try {
        $reader = [IO.BinaryReader]::new($stream)
        if ($reader.ReadUInt16() -ne 0x5A4D) { throw "Not a PE file: $Path" }
        $stream.Position = 0x3c
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) { throw "Invalid PE signature: $Path" }
        return ('0x{0:X4}' -f $reader.ReadUInt16())
    }
    finally { $stream.Dispose() }
}

function Assert-RequiredFile([string] $Path, [System.Collections.Generic.List[string]] $Errors) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { [void]$Errors.Add("Missing required file: $Path") }
}

function Test-RuntimeConfig([string] $Path, [string] $Label, [System.Collections.Generic.List[string]] $Errors) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    try { $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { [void]$Errors.Add("Invalid runtimeconfig JSON ($Label): $($_.Exception.Message)"); return }
    $frameworks = $null
    if ($null -ne $json.runtimeOptions -and $null -ne $json.runtimeOptions.PSObject.Properties['includedFrameworks']) { $frameworks = $json.runtimeOptions.includedFrameworks }
    if ($null -eq $frameworks -or @($frameworks).Count -eq 0) { [void]$Errors.Add("runtimeconfig has no includedFrameworks ($Label).") }
    foreach ($runtimeHost in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
        $folder = Split-Path -Parent $Path
        Assert-RequiredFile (Join-Path $folder $runtimeHost) $Errors
    }
}

function Test-Dependencies([string] $Path, [string] $Rid, [string] $Label, [System.Collections.Generic.List[string]] $Errors) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return }
    try { $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json }
    catch { [void]$Errors.Add("Invalid deps JSON ($Label): $($_.Exception.Message)"); return }
    $runtimeTarget = ''
    if ($null -ne $json.runtimeTarget -and $null -ne $json.runtimeTarget.PSObject.Properties['name']) { $runtimeTarget = [string]$json.runtimeTarget.name }
    if ($runtimeTarget -notmatch [regex]::Escape($Rid) + '$') { [void]$Errors.Add("Deps runtimeTarget does not match $Rid ($Label): $runtimeTarget") }
}

function Test-Version([string] $ExePath, [string] $Label, [string] $Rid, [System.Collections.Generic.List[string]] $Errors) {
    if (-not (Test-Path -LiteralPath $ExePath -PathType Leaf)) { return $null }
    $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($ExePath)
    $expectedInformational = '0.9.0-beta.1+' + $script:SourceRevision
    if ($info.ProductName -ne 'FaultWitness') { [void]$Errors.Add("Product mismatch ($Label): $($info.ProductName)") }
    if ([string]::IsNullOrWhiteSpace($info.FileVersion)) { [void]$Errors.Add("Missing file version ($Label).") }
    if ([string]::IsNullOrWhiteSpace($info.ProductVersion) -or $info.ProductVersion -notlike '*beta*' -or $info.ProductVersion -notlike "*$($script:SourceRevision)*") {
        [void]$Errors.Add("Informational/product version mismatch ($Label): $($info.ProductVersion); expected $expectedInformational")
    }
    try {
        $machine = Read-PeMachine $ExePath
        $expectedMachine = if ($Rid -eq 'win-x64') { '0x8664' } else { '0xAA64' }
        if ($machine -ne $expectedMachine) { [void]$Errors.Add("PE machine mismatch ($Label): $machine; expected $expectedMachine") }
    }
    catch { [void]$Errors.Add($_.Exception.Message) }
    return [pscustomobject]@{ Label = $Label; FileVersion = $info.FileVersion; ProductVersion = $info.ProductVersion; ProductName = $info.ProductName }
}

function Test-Inventory([string] $PublishRoot, [System.Collections.Generic.List[string]] $Errors) {
    $allowedJson = @('FaultWitness.App.deps.json', 'FaultWitness.App.runtimeconfig.json', 'FaultWitness.ElevatedHelper.deps.json', 'FaultWitness.ElevatedHelper.runtimeconfig.json', 'FaultWitness.ElevatedHelper.payload.json')
    $badPatterns = '\.(pdb|db|log|dmp|dump|evtx|wer|png|jpg|jpeg|gif|sln|csproj|cs|fs|vb)$|(^|[\\/])(tests?|fixtures?|source|private|developer|tools?)([\\/]|$)|(^|[\\/])(dotnet|csc|msbuild|vstest|testhost)(\.exe)?$'
    foreach ($file in Get-ChildItem -LiteralPath $PublishRoot -File -Recurse) {
        $relative = $file.FullName.Substring($PublishRoot.Length).TrimStart('\','/')
        if ($file.Extension -ieq '.json' -and $file.Name -notin $allowedJson) { [void]$Errors.Add("Unapproved JSON in payload: $relative") }
        if ($relative -match $badPatterns) { [void]$Errors.Add("Disallowed payload artifact: $relative") }
    }
}

function Test-Publish([string] $PublishRoot, [string] $Rid) {
    $errors = [System.Collections.Generic.List[string]]::new()
    $app = Join-Path $PublishRoot 'FaultWitness.App'
    $helper = Join-Path $PublishRoot 'helper'
    $appExe = Join-Path $PublishRoot 'FaultWitness.App.exe'
    $helperExe = Join-Path $helper 'FaultWitness.ElevatedHelper.exe'
    foreach ($file in @('FaultWitness.App.exe','FaultWitness.App.dll','FaultWitness.App.deps.json','FaultWitness.App.runtimeconfig.json')) { Assert-RequiredFile (Join-Path $PublishRoot $file) $errors }
    foreach ($file in @('FaultWitness.ElevatedHelper.exe','FaultWitness.ElevatedHelper.dll','FaultWitness.ElevatedHelper.deps.json','FaultWitness.ElevatedHelper.runtimeconfig.json','FaultWitness.ElevatedHelper.payload.json')) { Assert-RequiredFile (Join-Path $helper $file) $errors }
    Test-Dependencies (Join-Path $PublishRoot 'FaultWitness.App.deps.json') $Rid 'app' $errors
    Test-Dependencies (Join-Path $helper 'FaultWitness.ElevatedHelper.deps.json') $Rid 'helper' $errors
    Test-RuntimeConfig (Join-Path $PublishRoot 'FaultWitness.App.runtimeconfig.json') 'app' $errors
    Test-RuntimeConfig (Join-Path $helper 'FaultWitness.ElevatedHelper.runtimeconfig.json') 'helper' $errors
    $versions = @((Test-Version $appExe 'app' $Rid $errors), (Test-Version $helperExe 'helper' $Rid $errors)) | Where-Object { $null -ne $_ }
    foreach ($locale in @('de','es','fr','it','pl','pt','ru')) { Assert-RequiredFile (Join-Path (Join-Path $PublishRoot $locale) 'FaultWitness.Localization.resources.dll') $errors }
    try {
        $payload = Get-Content (Join-Path $helper 'FaultWitness.ElevatedHelper.payload.json') -Raw | ConvertFrom-Json
        $payloadRid = if ($null -ne $payload.PSObject.Properties['Rid']) { [string]$payload.Rid } else { '' }
        $payloadBuild = if ($null -ne $payload.PSObject.Properties['Build']) { [string]$payload.Build } else { '' }
        if ($payloadRid -ne $Rid) { [void]$errors.Add("Helper payload RID mismatch: $payloadRid; expected $Rid") }
        if ($payloadBuild -notlike "*$($script:SourceRevision)*") { [void]$errors.Add("Helper payload build does not contain SourceRevisionId: $payloadBuild") }
    }
    catch { [void]$errors.Add("Invalid helper payload JSON: $($_.Exception.Message)") }
    Test-Inventory $PublishRoot $errors
    $files = @(Get-ChildItem -LiteralPath $PublishRoot -File -Recurse)
    [pscustomobject]@{ Rid = $Rid; Size = [long](($files | Measure-Object Length -Sum).Sum); Count = $files.Count; Version = $versions; Errors = @($errors); Passed = ($errors.Count -eq 0) }
}

function Invoke-NegativeTests([string] $RunRoot) {
    $results = [System.Collections.Generic.List[object]]::new()
    $staleSource = Join-Path $RunRoot 'negative-stale-helper'
    Copy-IsolatedSource $script:SourceRoot $staleSource
    $staleBin = Join-Path $staleSource 'src/FaultWitness.ElevatedHelper/bin/Release/net10.0-windows'
    New-Item -ItemType Directory -Path $staleBin -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $staleBin 'FaultWitness.ElevatedHelper.exe') -Value 'stale'
    Remove-Item -LiteralPath (Join-Path $staleSource 'src/FaultWitness.ElevatedHelper/FaultWitness.ElevatedHelper.csproj') -Force
    $log1 = Join-Path $RunRoot 'negative-stale-helper.log'
    $exit1 = Invoke-Publish $staleSource (Join-Path $RunRoot 'negative-stale-helper-output') 'win-x64' $log1
    $results.Add([pscustomobject]@{ Name = 'stale-helper-project'; ExitCode = $exit1; Passed = ($exit1 -ne 0); Log = $log1 })
    if ($exit1 -eq 0) { throw 'Negative stale-helper test unexpectedly succeeded.' }

    $deleteSource = Join-Path $RunRoot 'negative-delete-helper'
    Copy-IsolatedSource $script:SourceRoot $deleteSource
    $helperProject = Join-Path $deleteSource 'src/FaultWitness.ElevatedHelper/FaultWitness.ElevatedHelper.csproj'
    $xml = Get-Content -LiteralPath $helperProject -Raw
    $xml = $xml -replace '</Project>', '<Target Name="TestDeleteHelperDll" BeforeTargets="GetCaptureHelperPayload"><Delete Files="$(TargetPath)" /></Target></Project>'
    Set-Content -LiteralPath $helperProject -Value $xml -Encoding UTF8
    $log2 = Join-Path $RunRoot 'negative-delete-helper.log'
    $exit2 = Invoke-Publish $deleteSource (Join-Path $RunRoot 'negative-delete-helper-output') 'win-x64' $log2
    $results.Add([pscustomobject]@{ Name = 'delete-helper-dll'; ExitCode = $exit2; Passed = ($exit2 -ne 0); Log = $log2 })
    if ($exit2 -eq 0) { throw 'Negative delete-helper test unexpectedly succeeded.' }
    return @($results)
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Split-Path -Parent $PSScriptRoot }
$source = Resolve-FullPath $SourceRoot
$sourceRootPath = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $sourceRootPath) { $sourceRootPath = Resolve-FullPath $sourceRootPath }
else { New-Item -ItemType Directory -Path $sourceRootPath -Force | Out-Null; $sourceRootPath = Resolve-FullPath $sourceRootPath }
if (Test-PathWithin $sourceRootPath $source) { throw 'OutputRoot must be outside SourceRoot.' }
$script:SourceRoot = $source
Push-Location $source
try { $script:SourceRevision = (git rev-parse HEAD).Trim() } finally { Pop-Location }
if ([string]::IsNullOrWhiteSpace($script:SourceRevision)) { throw 'Could not determine SourceRevisionId.' }
$runRoot = Join-Path $sourceRootPath ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$summaryPath = Join-Path $runRoot 'summary.json'
$audits = [System.Collections.Generic.List[object]]::new()
$negative = @()
try {
    foreach ($rid in @('win-x64','win-arm64')) {
        $isolated = Join-Path $runRoot ("source-$rid")
        $publish = Join-Path $runRoot ("publish-$rid")
        Copy-IsolatedSource $source $isolated
        $log = Join-Path $runRoot ("publish-$rid.log")
        $exitCode = Invoke-Publish $isolated $publish $rid $log
        if ($exitCode -ne 0) { throw "Publish failed for $rid; see $log" }
        $audit = Test-Publish $publish $rid
        $audits.Add($audit)
        if (-not $audit.Passed) { throw "Payload audit failed for ${rid}: $($audit.Errors -join '; ')" }
    }
    $negative = Invoke-NegativeTests $runRoot
    $summary = [pscustomobject]@{ SourceRevisionId = $script:SourceRevision; RunRoot = $runRoot; Audits = @($audits); NegativeBuildTests = $negative; Passed = $true }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    Write-Output $summaryPath
}
catch {
    $summary = [pscustomobject]@{ SourceRevisionId = $script:SourceRevision; RunRoot = $runRoot; Audits = @($audits); NegativeBuildTests = $negative; Passed = $false; Error = $_.Exception.Message }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding UTF8
    throw
}
