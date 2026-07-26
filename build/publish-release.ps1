<#
.SYNOPSIS
    產生一次完整的發行：免安裝版 .exe、安裝程式 .exe，以及自動更新用的 update-manifest.json。

.DESCRIPTION
    版本號來自 Directory.Build.props，是唯一來源。
    產出全部放在 artifacts\ 底下，之後把這些檔案上傳到 GitHub Release 即可。

    上傳時「一定要」包含 update-manifest.json，舊版程式才能驗證更新檔的雜湊。
    沒有這個檔案時，舊版會退回「請手動下載」而不會自動更新。

.PARAMETER MajorChange
    這一版有重大架構變更，不允許程式內更新，使用者必須手動下載安裝。

.PARAMETER MinimumInAppUpdateFrom
    低於這個版本的使用者必須手動重裝。例如 -MinimumInAppUpdateFrom 1.2.0

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\publish-release.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\publish-release.ps1 -MajorChange
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [string]$Configuration = 'Release',
    [switch]$MajorChange,
    [string]$MinimumInAppUpdateFrom = '',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

function Write-Step($t) { Write-Host "`n==> $t" -ForegroundColor Cyan }
function Write-Ok($t)   { Write-Host "    $t" -ForegroundColor Green }
function Write-Bad($t)  { Write-Host "    $t" -ForegroundColor Red }

$repoRoot  = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $repoRoot 'artifacts'

Write-Step '讀取版本號'
$appVersion = ([xml](Get-Content (Join-Path $repoRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version |
              Where-Object { $_ } | Select-Object -First 1
if (-not $appVersion) { Write-Bad '無法從 Directory.Build.props 讀出 <Version>。'; exit 1 }
Write-Ok "版本 $appVersion"

Write-Step '建置免安裝版'
& (Join-Path $PSScriptRoot 'publish-portable.ps1') -Runtime $Runtime -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { Write-Bad '免安裝版建置失敗。'; exit 1 }

if (-not $SkipInstaller) {
    Write-Step '建置安裝版'
    & (Join-Path $PSScriptRoot 'publish-installer-payload.ps1') -Runtime $Runtime -Configuration $Configuration -SkipTests
    if ($LASTEXITCODE -ne 0) { Write-Bad '安裝版建置失敗。'; exit 1 }
}

Write-Step '產生 update-manifest.json'

# 接受 Get-ChildItem 回傳的 FileInfo 或路徑字串。
#
# 注意不要寫成 Test-Path $file：Windows PowerShell 5.1 把 FileInfo 轉字串時
# 只會得到「檔名」而不是完整路徑，於是變成拿相對路徑去比對目前工作目錄，
# 只要不是剛好站在 artifacts 底下執行就一律判定為不存在，靜靜回傳 $null。
function New-AssetEntry($file) {
    if (-not $file) { return $null }

    $item = if ($file -is [System.IO.FileInfo]) { $file }
            else { Get-Item -LiteralPath ([string]$file) -ErrorAction SilentlyContinue }

    if (-not $item -or -not (Test-Path -LiteralPath $item.FullName)) { return $null }

    [ordered]@{
        fileName = $item.Name
        sha256   = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size     = $item.Length
    }
}

$portableExe = Get-ChildItem $artifacts -Filter "*Portable-$Runtime.exe" -ErrorAction SilentlyContinue |
               Sort-Object LastWriteTime -Descending | Select-Object -First 1
$installerExe = Get-ChildItem $artifacts -Filter '*Setup.exe' -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending | Select-Object -First 1

$portable  = New-AssetEntry $portableExe
$installer = New-AssetEntry $installerExe

if (-not $portable -and -not $installer) {
    Write-Bad "在 $artifacts 找不到任何可發行的檔案。"
    exit 1
}

$config = $null
$configPath = Join-Path $repoRoot 'update.config.json'
if (Test-Path $configPath) { $config = Get-Content $configPath -Raw | ConvertFrom-Json }

$releaseUrl = $null
if ($config -and $config.owner -and $config.repository) {
    $releaseUrl = "https://github.com/$($config.owner)/$($config.repository)/releases/tag/v$appVersion"
}

$manifest = [ordered]@{
    schemaVersion          = 1
    version                = $appVersion
    releasedAt             = (Get-Date).ToString('yyyy-MM-dd')
    releaseUrl             = $releaseUrl
    requiresManualDownload = [bool]$MajorChange
}

if ($MinimumInAppUpdateFrom) { $manifest.minimumInAppUpdateFrom = $MinimumInAppUpdateFrom }
if ($portable)  { $manifest.portable  = $portable }
if ($installer) { $manifest.installer = $installer }

$manifestPath = Join-Path $artifacts 'update-manifest.json'
$json = $manifest | ConvertTo-Json -Depth 5

# 清單由程式讀取，不需要 BOM；統一用 UTF-8 無 BOM
[System.IO.File]::WriteAllText($manifestPath, $json, (New-Object System.Text.UTF8Encoding($false)))

Write-Ok "已寫入 $manifestPath"
if ($portable)  { Write-Ok "免安裝版 $($portable.fileName)  $([math]::Round($portable.size/1MB,1)) MB" }
if ($installer) { Write-Ok "安裝程式 $($installer.fileName)  $([math]::Round($installer.size/1MB,1)) MB" }
if ($MajorChange) { Write-Host '    注意：已標記為重大變更，使用者必須手動下載。' -ForegroundColor Yellow }

Write-Step '完成'
Write-Host ''
Write-Host '  接下來請到 GitHub 建立 Release：' -ForegroundColor White
Write-Host "    1. 標籤（tag）填 v$appVersion" -ForegroundColor White
Write-Host '    2. 上傳 artifacts 資料夾中的這些檔案：' -ForegroundColor White
if ($portable)  { Write-Host "         $($portable.fileName)" -ForegroundColor Gray }
if ($installer) { Write-Host "         $($installer.fileName)" -ForegroundColor Gray }
Write-Host '         update-manifest.json          <-- 必要，沒有它舊版無法自動更新' -ForegroundColor Yellow
Write-Host '    3. 發佈為 Latest release' -ForegroundColor White
Write-Host ''
