# Windows 標準の .NET Framework 4.x のコンパイラで bin\VolumeKeeper.exe をビルドする（追加のSDKは不要）
param(
    # exe のファイルバージョン（例: 1.2.0）。GitHub Actions ではタグから渡す
    [string]$Version = '0.0.0'
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe が見つかりません: $csc" }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version は 1.2.3 の形式で指定してください: $Version" }

$bin = Join-Path $here 'bin'
$obj = Join-Path $here 'obj'
New-Item -ItemType Directory -Force $bin, $obj | Out-Null

# バージョン情報（エクスプローラーのプロパティに表示される）
$assemblyInfo = Join-Path $obj 'AssemblyInfo.cs'
@"
using System.Reflection;
[assembly: AssemblyTitle("VolumeKeeper")]
[assembly: AssemblyProduct("VolumeKeeper")]
[assembly: AssemblyDescription("アプリ別の音量を決めた値に保つタスクトレイ常駐ツール")]
[assembly: AssemblyCopyright("Copyright (c) 2026 HAL")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
"@ | Set-Content $assemblyInfo -Encoding UTF8

$ico = Join-Path $here 'assets\VolumeKeeper.ico'
$sources = @(Get-ChildItem (Join-Path $here 'src') -Filter *.cs | ForEach-Object FullName) + $assemblyInfo

& $csc /nologo /target:winexe /optimize+ /utf8output /codepage:65001 `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    "/out:$(Join-Path $bin 'VolumeKeeper.exe')" `
    "/win32icon:$ico" "/resource:$ico,VolumeKeeper.ico" `
    $sources
exit $LASTEXITCODE
