<#
    Builds SoundFlow.exe with the .NET Framework 4.8 compiler that ships with
    Windows, so no Visual Studio or SDK installation is required.

    Usage:  powershell -ExecutionPolicy Bypass -File .\build.ps1
#>
[CmdletBinding()]
param(
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $root 'src'
$toolsDir = Join-Path $root 'tools'
$assetsDir = Join-Path $root 'assets'
$manifest = Join-Path $root 'app.manifest'
$output = Join-Path $root 'SoundFlow.exe'

$frameworkDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $frameworkDir 'csc.exe'
if (-not (Test-Path $csc)) {
    throw "C# compiler not found: $csc"
}

$winmdDir = Join-Path $env:WINDIR 'System32\WinMetadata'

New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$icon = Join-Path $assetsDir 'app.ico'
if (-not $SkipIcon) {
    $iconGen = Join-Path $env:TEMP 'sf-icongen.exe'
    & $csc /nologo /target:exe /optimize+ "/out:$iconGen" (Join-Path $toolsDir 'IconGen.cs')
    if ($LASTEXITCODE -ne 0) { throw 'icon generator compilation failed' }
    & $iconGen $icon
    if ($LASTEXITCODE -ne 0) { throw 'icon generation failed' }
}

$references = @(
    (Join-Path $frameworkDir 'System.dll'),
    (Join-Path $frameworkDir 'System.Core.dll'),
    (Join-Path $frameworkDir 'System.Drawing.dll'),
    (Join-Path $frameworkDir 'System.Windows.Forms.dll'),
    (Join-Path $frameworkDir 'System.Runtime.dll'),
    (Join-Path $frameworkDir 'System.Runtime.InteropServices.WindowsRuntime.dll'),
    (Join-Path $frameworkDir 'System.Runtime.WindowsRuntime.dll'),
    (Join-Path $winmdDir 'Windows.Foundation.winmd'),
    (Join-Path $winmdDir 'Windows.Media.winmd'),
    (Join-Path $winmdDir 'Windows.Devices.winmd'),
    (Join-Path $winmdDir 'Windows.Storage.winmd'),
    (Join-Path $winmdDir 'Windows.ApplicationModel.winmd')
)

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "missing reference: $reference"
    }
}

$sources = Get-ChildItem -Path $sourceDir -Filter '*.cs' | Sort-Object Name | ForEach-Object { $_.FullName }

$arguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    "/out:$output",
    "/win32icon:$icon",
    "/win32manifest:$manifest"
)
foreach ($reference in $references) {
    $arguments += "/r:$reference"
}
$arguments += $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) {
    throw "build failed (exit code $LASTEXITCODE)"
}

$info = Get-Item -LiteralPath $output
'built {0} ({1:N1} KB)' -f $info.FullName, ($info.Length / 1KB)


