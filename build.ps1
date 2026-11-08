# Builds DeskMeter.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No SDK install required.  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1
# anycpu: 64 ビット Windows では 64 ビットで動く (ARM64 Windows でもネイティブ実行)。32 ビット OS は対象外 (計測の一部が 64 ビット前提)
param([string]$Out = '')   # -Out で出力先を変えられる (確認ビルド用)
$ErrorActionPreference = 'Stop'
$fw  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe not found: $csc" }

$out = if ($Out) { $Out } else { Join-Path $PSScriptRoot 'DeskMeter.exe' }
$src = Get-ChildItem (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }

$ico = Join-Path $PSScriptRoot 'app.ico'
$iconArg = @(); if (Test-Path $ico) { $iconArg = @("/win32icon:$ico") }

& $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /warn:3 `
    "/out:$out" $iconArg `
    "/r:$fw\WPF\PresentationCore.dll" `
    "/r:$fw\WPF\PresentationFramework.dll" `
    "/r:$fw\WPF\WindowsBase.dll" `
    "/r:$fw\System.Xaml.dll" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:Microsoft.VisualBasic.dll /r:System.Management.dll `
    /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
    $src

if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }
Write-Host "Built: $out"
