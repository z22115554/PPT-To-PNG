<#
.SYNOPSIS
    建置「安裝版」PPT PNG 匯出工具。

.DESCRIPTION
    流程：檢查環境 → 執行測試 → 發佈程式 → 用 Inno Setup 編譯安裝程式。
    產出會放在 artifacts\ 資料夾。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\publish-installer-payload.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',

    [string]$Configuration = 'Release',

    # 設為 $true 會改用單一檔案發佈（安裝後佔用較小，但啟動慢 1-3 秒）
    [switch]$SingleFile,

    # 跳過測試（不建議）
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

function Write-Step($text) { Write-Host "`n==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Bad($text)  { Write-Host "    $text" -ForegroundColor Red }

$repoRoot  = Split-Path -Parent $PSScriptRoot
$project   = Join-Path $repoRoot 'src\PptPngExporter.App\PptPngExporter.App.csproj'
$solution  = Join-Path $repoRoot 'PptPngExporter.sln'
$payload   = Join-Path $repoRoot 'artifacts\installer-payload'
$issScript = Join-Path $PSScriptRoot 'installer.iss'

# ---------------------------------------------------------------- 環境檢查

Write-Step '檢查建置環境'

if (-not (Test-Path $solution)) {
    Write-Bad "找不到 $solution"
    Write-Bad '請確認這個腳本位於原始碼的 build 資料夾底下，且原始碼已完整解壓縮。'
    exit 1
}

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Bad '找不到 dotnet 指令。'
    Write-Bad '請安裝 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0'
    Write-Bad '（注意要裝 SDK，不是只有 Runtime。裝完請「重新開啟」PowerShell 視窗。）'
    exit 1
}

$sdks = & dotnet --list-sdks
if (-not ($sdks | Where-Object { $_ -match '^8\.' })) {
    Write-Bad '偵測到 dotnet，但沒有 .NET 8 SDK。目前安裝的版本：'
    $sdks | ForEach-Object { Write-Bad "  $_" }
    Write-Bad '請安裝 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0'
    exit 1
}
Write-Ok ".NET SDK 正常（$(($sdks | Where-Object { $_ -match '^8\.' } | Select-Object -First 1))）"

# 找 Inno Setup：先查登錄檔，再查常見安裝路徑
function Find-InnoSetup {
    foreach ($key in @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )) {
        try {
            $location = (Get-ItemProperty -Path $key -ErrorAction Stop).InstallLocation
            if ($location) {
                $exe = Join-Path $location 'ISCC.exe'
                if (Test-Path $exe) { return $exe }
            }
        } catch { }
    }

    # 注意：必須寫成 ${env:ProgramFiles(x86)}。
    # 寫成 "$env:ProgramFiles(x86)\..." 時，PowerShell 只會展開 $env:ProgramFiles，
    # 後面的 (x86) 變成字面文字，得到錯誤的 "C:\Program Files(x86)\..."。
    foreach ($candidate in @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles        'Inno Setup 6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 5\ISCC.exe')
    )) {
        if ($candidate -and (Test-Path $candidate)) { return $candidate }
    }

    $onPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    return $null
}

$iscc = Find-InnoSetup
if ($iscc) {
    Write-Ok "Inno Setup：$iscc"
} else {
    Write-Host '    找不到 Inno Setup，稍後只會準備好打包檔案，不會編譯安裝程式。' -ForegroundColor Yellow
}

# ---------------------------------------------------------------- 測試

if ($SkipTests) {
    Write-Step '略過測試（--SkipTests）'
} else {
    Write-Step '執行測試'
    & dotnet test $solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Bad '測試沒有全部通過，已停止建置。'
        exit 1
    }
    Write-Ok '全部測試通過'
}

# ---------------------------------------------------------------- 發佈

Write-Step "發佈程式（$Runtime）"

if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'true',
    '-o', $payload,
    '--nologo',
    "-p:PublishSingleFile=$($SingleFile.IsPresent.ToString().ToLower())",
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=none',
    '-p:DebugSymbols=false'
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    Write-Bad '發佈失敗。'
    exit 1
}

Get-ChildItem $payload -Filter *.pdb -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

$payloadSize = [math]::Round(((Get-ChildItem $payload -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Ok "發佈完成（$payloadSize MB）"

# ---------------------------------------------------------------- 安裝程式

if (-not $iscc) {
    Write-Step '未編譯安裝程式'
    Write-Host "    打包檔案已備妥：$payload" -ForegroundColor Yellow
    Write-Host "    安裝 Inno Setup 6 後重新執行本腳本，或用 Inno Setup 手動開啟：$issScript" -ForegroundColor Yellow
    Write-Host '    下載：https://jrsoftware.org/isdl.php' -ForegroundColor Yellow
    exit 0
}

Write-Step '編譯安裝程式'

# Inno Setup 沒有內建繁體中文語系檔。若使用者已自行放入，就切換成中文介面。
$isccDir = Split-Path -Parent $iscc
$chineseIsl = Join-Path $isccDir 'Languages\ChineseTraditional.isl'
$isccArgs = @($issScript)

if (Test-Path $chineseIsl) {
    Write-Ok '偵測到繁體中文語系檔，安裝程式介面將使用繁體中文'
    $isccArgs = @('/DCHINESE') + $isccArgs
} else {
    Write-Host '    未偵測到 ChineseTraditional.isl，安裝程式介面會是英文。' -ForegroundColor Yellow
    Write-Host '    想要中文介面：從 https://jrsoftware.org/files/istrans/ 下載 ChineseTraditional.isl，' -ForegroundColor Yellow
    Write-Host "    放到 $isccDir\Languages\ 後重新執行本腳本。" -ForegroundColor Yellow
}

& $iscc @isccArgs
if ($LASTEXITCODE -ne 0) {
    Write-Bad 'Inno Setup 編譯失敗。'
    exit 1
}

Write-Step '完成'
Get-ChildItem (Join-Path $repoRoot 'artifacts') -Filter *.exe | ForEach-Object {
    Write-Ok "$($_.FullName)（$([math]::Round($_.Length / 1MB, 1)) MB）"
}
