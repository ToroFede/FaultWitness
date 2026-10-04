[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $OutputRoot,
    [Parameter(Mandatory = $true)] [string] $SourceRevision
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ($SourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'SourceRevision must be the exact source commit.' }
$repo = Split-Path -Parent $PSScriptRoot
[xml]$props = Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props')
$version = [string]$props.Project.PropertyGroup.Version
$output = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ Build Tools to build the Windows launcher.' }
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $installation) { throw 'Visual Studio C++ x64 tools are required.' }
$dev = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
$resource = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'launcher/launcher.rc.in') -Raw).Replace('@VERSION@', $version).Replace('@REVISION@', $SourceRevision)
Set-Content -LiteralPath (Join-Path $output 'launcher.rc') -Value $resource -Encoding utf8
$source = Join-Path $PSScriptRoot 'launcher/launcher.c'
$manifest = Join-Path $PSScriptRoot 'launcher/launcher.manifest'
# cmd is used only by the compiler environment; the shipping launcher calls CreateProcessW directly.
$build = @"
@echo off
call "$dev" -arch=x64 -host_arch=x64 >nul
if errorlevel 1 exit /b 1
rc /nologo /fo "$output\launcher.res" "$output\launcher.rc"
if errorlevel 1 exit /b 1
cl /nologo /W4 /WX /O2 /MT /utf-8 /Fo"$output\launcher.obj" /Fe"$output\FaultWitness.exe" "$source" "$output\launcher.res" user32.lib /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED /MANIFESTINPUT:"$manifest" /DYNAMICBASE /NXCOMPAT
exit /b %errorlevel%
"@
$batch = Join-Path $output 'build-launcher.cmd'
Set-Content -LiteralPath $batch -Value $build -Encoding ascii
& $env:ComSpec /d /c $batch
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
$info = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'FaultWitness.exe'))
if ($info.ProductVersion -ne "$version+$SourceRevision") { throw 'Launcher source/version metadata mismatch.' }
Write-Output (Join-Path $output 'FaultWitness.exe')
