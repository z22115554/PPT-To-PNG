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

# 版本號的唯一來源是 Directory.Build.props
$appVersion = '0.0.0'
try {
    $v = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version |
         Where-Object { $_ } | Select-Object -First 1
    if ($v) { $appVersion = $v }
} catch { }

# 發行用的檔名必須是純 ASCII：GitHub 上傳 Release 附件時會把非 ASCII 字元
# 全部換成句點，中文檔名會變成 PPT-PNG-.-.-win-x64.exe，
# 而自動更新是用 update-manifest.json 裡的 fileName 去比對附件名稱的，
# 一旦被改名就永遠找不到下載網址。
$releaseExe = Join-Path $repoRoot "artifacts\PPT-PNG-Exporter-v$appVersion-Portable-$Runtime.exe"

Write-Host "==> 清理舊的輸出" -ForegroundColor Cyan
if (Test-Path $stageDir)   { Remove-Item $stageDir -Recurse -Force }
if (Test-Path $releaseExe) { Remove-Item $releaseExe -Force }
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

if (-not $SingleFile) {
    Write-Host ''
    Write-Host "已輸出資料夾版：$stageDir" -ForegroundColor Green
    Write-Host '資料夾版不會產生發行用的單一檔案，請直接壓縮整個資料夾自行散布。' -ForegroundColor Yellow
    return
}

Write-Host "==> 取出發行用的單一執行檔" -ForegroundColor Cyan

# 單一檔案發佈仍會在輸出資料夾放一份 update.config.json（CopyToOutputDirectory），
# 這裡只取 .exe。使用者手上沒有設定檔時，程式會用編譯進去的預設 GitHub 儲存庫。
$built = Get-ChildItem $stageDir -Filter *.exe |
         Sort-Object Length -Descending | Select-Object -First 1
if (-not $built) { Write-Host '找不到發佈出來的 .exe。' -ForegroundColor Red; exit 1 }

Copy-Item $built.FullName $releaseExe -Force

$sizeMb = [math]::Round((Get-Item $releaseExe).Length / 1MB, 1)
Write-Host ''
Write-Host "完成：$releaseExe（$sizeMb MB）" -ForegroundColor Green
Write-Host '免安裝版就是這一個檔案，下載後直接執行，不需要解壓縮。' -ForegroundColor Green
