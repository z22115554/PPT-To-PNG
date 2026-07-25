<#
.SYNOPSIS
    產生「免安裝版」PPT PNG 匯出工具。

.DESCRIPTION
    輸出單一 .exe（自帶 .NET 執行階段），使用者解壓縮後雙擊即可執行，
    不需要安裝 .NET、不需要系統管理員權限。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\publish-portable.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Configuration = 'Release',

    # 設為 $false 會輸出資料夾版（啟動較快、檔案較多）
    [bool]$SingleFile = $true
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot

# 環境檢查：先確認 .NET 8 SDK 存在，否則後面的錯誤訊息會很難懂
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host '找不到 dotnet 指令。' -ForegroundColor Red
    Write-Host '請安裝 .NET 8 SDK（要 SDK，不是只有 Runtime）：https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Red
    Write-Host '裝完請「重新開啟」PowerShell 視窗再試一次。' -ForegroundColor Red
    exit 1
}
if (-not (& dotnet --list-sdks | Where-Object { $_ -match '^8\.' })) {
    Write-Host '偵測到 dotnet，但沒有 .NET 8 SDK。' -ForegroundColor Red
    Write-Host '請安裝：https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Red
    exit 1
}
$project  = Join-Path $repoRoot 'src\PptPngExporter.App\PptPngExporter.App.csproj'
$stageDir = Join-Path $repoRoot "artifacts\portable-$Runtime"
$zipPath  = Join-Path $repoRoot "artifacts\PPT-PNG-匯出工具-免安裝版-$Runtime.zip"

Write-Host "==> 清理舊的輸出" -ForegroundColor Cyan
if (Test-Path $stageDir) { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $zipPath)  { Remove-Item $zipPath -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

Write-Host "==> 執行測試" -ForegroundColor Cyan
dotnet test (Join-Path $repoRoot 'PptPngExporter.sln') -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw '測試沒有全部通過，已停止建置。' }

Write-Host "==> 發佈 $Runtime（自帶執行階段）" -ForegroundColor Cyan
$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-o', $stageDir,
    '--nologo',
    # 必須用字串內插。在陣列常值中，逗號的優先順序高於 +，
    # 寫成 '-p:PublishSingleFile=' + $x 會被解讀成「陣列 + 元素」，
    # 結果是 '-p:PublishSingleFile=' 與 'true' 兩個獨立參數，MSBuild 會回報 MSB1008。
    "-p:PublishSingleFile=$($SingleFile.ToString().ToLower())",
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '-p:DebugSymbols=false'
)
dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { Write-Host '發佈失敗。' -ForegroundColor Red; exit 1 }

# 原生相依套件可能仍會帶入 .pdb，這裡一併移除
Get-ChildItem $stageDir -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "==> 加入使用說明" -ForegroundColor Cyan
Copy-Item (Join-Path $repoRoot 'README.md') (Join-Path $stageDir 'README.md') -Force
@'
PPT PNG 匯出工具（免安裝版）

使用方式
  1. 把整個資料夾解壓縮到任何位置（例如桌面）。
  2. 雙擊「PPT PNG 匯出工具.exe」即可使用。

不需要安裝 .NET，也不需要系統管理員權限。
沒有安裝 PowerPoint 的電腦請另外安裝 LibreOffice：https://zh-tw.libreoffice.org/
詳細說明請看 README.md。
'@ | Set-Content -Path (Join-Path $stageDir '請先看我.txt') -Encoding UTF8

Write-Host "==> 壓縮成 ZIP" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $zipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host ''
Write-Host "完成：$zipPath（$sizeMb MB）" -ForegroundColor Green
Write-Host "解壓縮後執行資料夾：$stageDir" -ForegroundColor Green
