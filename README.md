# PPT PNG 匯出工具

批次把多份 PowerPoint 簡報的每一頁，轉存成高畫質 PNG 圖片。

介面全繁體中文，設計給不熟悉技術的一般使用者：拖進來、按開始，就會在指定資料夾裡看到一份份分好的圖片。

---

## 下載

到 [Releases](https://github.com/z22115554/PPT-To-PNG/releases/latest) 取得最新版本：

| 檔案 | 說明 |
|---|---|
| `PPT-PNG-Exporter-v1.4.0-Setup.exe` | 安裝版，會建立開始功能表捷徑與右鍵選單 |
| `PPT-PNG-Exporter-v1.4.0-Portable-win-x64.exe` | 免安裝版，下載後直接雙擊執行 |

兩種都自帶 .NET 執行階段，不需要另外安裝。首次執行時 Windows SmartScreen 可能會因為沒有程式碼簽章而攔截，選「其他資訊 → 仍要執行」即可。

> 發行檔名一律用 ASCII。GitHub 上傳 Release 附件時會把非 ASCII 字元換成句點（中文檔名會變成 `PPT-PNG-.-.-win-x64.exe`），而自動更新是拿 `update-manifest.json` 裡的 `fileName` 去比對附件名稱的，被改名就會找不到下載網址。

---

## 快速開始（使用者）

### 免安裝版

下載 `PPT-PNG-Exporter-v1.4.0-Portable-win-x64.exe`，放到任何位置後雙擊執行。

不需要解壓縮，不需要安裝 .NET，不需要系統管理員權限。整個程式（含 .NET 執行階段）就是這一個檔案，第一次啟動會自我解壓到暫存資料夾，因此會比安裝版慢幾秒。

### 安裝版

執行 `PPT-PNG-Exporter-v1.4.0-Setup.exe`，依畫面指示完成。安裝後可從開始功能表開啟，也可以在 `.ppt` / `.pptx` 檔案上按右鍵選「用 PPT PNG 匯出工具開啟」。

### 操作流程

1. **加入簡報** — 把檔案或整個資料夾拖進左側清單，或按「加入簡報」。拖資料夾會自動找出裡面所有簡報（含子資料夾）。
2. **勾選要處理的檔案** — 預設全部勾選，可用「全選 / 取消全選」快速切換。
3. **設定右側選項**
   - **要轉換哪些頁面**：三選一 —— 全部頁面、輸入頁碼範圍（如 `1-5,8,12-15`）、或**看縮圖逐頁勾選**
   - **編號方式**：連續編號或沿用原始頁碼（見下節）
   - **圖片寬度**：直接輸入，或點 1280 / 1920 / 2560 / 3840 快捷鍵。高度依簡報比例自動計算，不會變形
   - **檔名前綴**：例如 `投影片_` 會產生 `投影片_01.png`、`投影片_02.png`…
   - **存放位置**：每份簡報會存到以檔名命名的獨立子資料夾
4. **按「開始轉換」** — 過程中可隨時按「停止」；已完成的檔案會保留。
5. **按「開啟輸出資料夾」** 檢視結果。也可以在清單任一列按兩下，直接開啟那份簡報的輸出資料夾。

### 頁碼輸入支援的寫法

| 輸入 | 意思 |
|---|---|
| `1-5,8,12-15` | 第 1～5、第 8、第 12～15 頁 |
| `3` | 只轉第 3 頁 |
| `5-` | 第 5 頁到最後一頁 |
| `-8` | 第 1 頁到第 8 頁 |
| `9-3` | 會自動修正成 3～9 頁 |
| `１－５，８` | 全形數字、全形逗號、破折號都能辨識 |

超出簡報實際頁數的部分會自動忽略；如果整段範圍都超出，該檔案會標示失敗並說明實際頁數。

### 看縮圖挑選頁面

不想數頁碼時，選「看縮圖逐頁勾選」再按**開啟縮圖挑選頁面**，程式會把勾選的簡報整份轉成縮圖排出來，點一下就切換勾選。

- 依簡報分組，每組可以「本份全選 / 全不選 / 反選」，上方也有全域的全部選取 / 全部取消
- 第一次預覽需要轉換整份簡報（會顯示進度，可中途停止）；縮圖會**快取**在 `%LOCALAPPDATA%\PptPngExporter\preview`，之後再開啟同一份簡報是即時的
- 簡報檔一旦被修改，快取自動失效並重新產生（快取鍵包含路徑、最後修改時間、大小、縮圖寬度、產生用的引擎與程式版本）
- 快取會自動清理：啟動時在背景清掉超過 14 天沒用到的，總量超過 2 GB 時再從最久沒用到的開始刪
- 大量投影片也不會拖垮介面：挑選視窗只會建立目前看得見的縮圖（實測 3010 張只具現化 25–33 個）
- 每份簡報記住自己的挑選結果，主畫面清單上會顯示「已挑 3 / 10 頁」
- 挑選後又加入的新簡報若沒挑過，會輸出全部頁面

### 編號方式

挑選不連續的頁面時，兩種編號方式的差別就出來了。以**挑選第 1、5、7 頁**為例：

| 編號方式 | 輸出檔名 |
|---|---|
| 依輸出順序連續編號（預設） | `投影片_001.png`、`投影片_002.png`、`投影片_003.png` |
| 沿用原始頁碼 | `投影片_001.png`、`投影片_005.png`、`投影片_007.png` |

補零位數可選「自動 / 01 / 001 / 0001」，預設為三位。自動模式下，連續編號看總張數、原始頁碼看最大頁碼來決定位數（最少兩位）。設定的位數若小於實際數字不會截斷，只是不再補零。

設定區會即時顯示預覽，直接用第 1、5、7 頁當例子，因為差別只有在跳頁時才看得出來。

---

## 自動更新

程式會在啟動時向 GitHub 查詢是否有新版本（每 20 小時最多一次），也可以隨時按右上角的**檢查更新**。

有新版時畫面上方會出現橫幅：

- **立即更新** — 下載、驗證、替換、重新啟動，全程在程式內完成
- **查看說明** — 開啟 GitHub 的發行頁面看更新內容
- **略過此版** — 這一版不再提醒（之後有更新版還是會提醒）

### 更新怎麼進行

| 安裝方式 | 更新方式 |
|---|---|
| 免安裝版 | 下載新的 .exe，就地替換（舊檔改名備份）後自動重新啟動 |
| 安裝版 | 下載新的安裝程式並以靜默模式執行，由 Inno Setup 覆蓋安裝 |
| 從原始碼執行 | 不自動更新，只提示有新版 |

免安裝版的替換用的是「改名」而不是「覆寫」：Windows 不允許覆寫執行中的 `.exe`，但允許改名。舊版會被改成 `.exe.old`，下次啟動時自動刪除。這比另外寫一支等待用的批次檔可靠，不會被防毒攔截，也不會有視窗閃爍。

### 安全性

- 更新資訊只從 HTTPS 取得
- 每個更新檔都會比對 **SHA-256**，雜湊來自隨發行上傳的 `update-manifest.json`
- 驗證失敗就中止，絕不替換執行檔
- 沒有 `update-manifest.json` 的發行（例如更早的版本）會被視為「必須手動下載」，因為沒有雜湊可以驗證

需要說明的限制：這套機制能擋下傳輸損毀與網路中間人竄改，但**擋不住 GitHub 帳號被入侵**。要防到那個層級需要程式碼簽章憑證，目前沒有。

### 重大變更時不自動更新

發行時加上 `-MajorChange` 參數，該版本的清單會標記 `requiresManualDownload`，使用者只會看到「請手動下載」的提示，不會出現「立即更新」按鈕。

也可以用 `-MinimumInAppUpdateFrom 1.2.0` 指定「低於 1.2.0 的使用者必須手動重裝」，比全面禁止更精細。

### 設定更新來源

儲存庫位置寫在專案根目錄的 `update.config.json`，發佈時會複製到執行檔旁邊：

```json
{
  "owner": "你的 GitHub 帳號",
  "repository": "PptPngExporter",
  "checkOnStartup": true,
  "minimumHoursBetweenChecks": 20
}
```

放在執行檔旁邊代表**改儲存庫不必重新編譯**。使用者也可以把 `checkOnStartup` 設為 `false` 關閉自動檢查。

---

## 發行新版本

```powershell
# 1. 改 Directory.Build.props 的 <Version>
# 2. 雙擊 build\build-release.bat（或執行下列指令）
powershell -ExecutionPolicy Bypass -File build\publish-release.ps1

# 有重大架構變更、不希望使用者自動更新時：
powershell -ExecutionPolicy Bypass -File build\publish-release.ps1 -MajorChange
```

腳本會依序：讀版本號 → 跑測試 → 建置免安裝版 → 建置安裝版 → 計算 SHA-256 → 產生 `update-manifest.json`，全部放進 `artifacts\`。

接著到 GitHub 建立 Release：

1. 標籤填 `v1.4.0`（要和 `Directory.Build.props` 的版本一致）
2. 上傳 `artifacts\` 裡的三個檔案：
   - `PPT-PNG-Exporter-v1.4.0-Portable-win-x64.exe`
   - `PPT-PNG-Exporter-v1.4.0-Setup.exe`
   - **`update-manifest.json`** ← 沒有這個，舊版就無法自動更新

   附件檔名不可以改，`update-manifest.json` 裡的 `fileName` 是照著這些名字寫的。
3. 發佈為 **Latest release**（程式查的是 `releases/latest`）

> **v1.1.0 的使用者無法自動更新。** 更新功能是 1.3.0 才加入的，1.1.0 的程式裡沒有任何檢查更新的程式碼。那批使用者必須手動下載一次。從 1.3.0 開始，之後的 1.4.0、1.5.0 都能在程式內更新。

---

## 系統需求

- **Windows 10 版本 1607（build 14393）以上，或 Windows 11**，64 位元
- **不支援 Windows 7 / 8 / 8.1 / Vista**：本程式基於 .NET 8，微軟已不再有任何支援這些系統的 .NET 版本
- 僅提供 x64 版本；32 位元 Windows 無法執行（`build\publish-portable.ps1 -Runtime win-arm64` 可產生 ARM64 版，但未經測試）
- **Microsoft PowerPoint**（選用，有的話還原度最高）
- **LibreOffice**（選用，沒有 PowerPoint 時的替代方案）— <https://zh-tw.libreoffice.org/>

至少要有其中一套。兩套都沒有時程式仍可開啟，但按下開始轉換會明確告知需要先安裝。

程式啟動時會偵測環境，並在「轉換方式」區塊直接顯示偵測結果。

---

## 轉檔流程

程式有兩套引擎，預設「自動」模式會依序嘗試：

### 1. PowerPoint（優先）

透過 COM 自動化呼叫本機安裝的 PowerPoint：

```
Presentations.Open(檔案, ReadOnly:=True, WithWindow:=False)
  → 讀取 PageSetup 算出正確長寬比
  → Slide.Export(路徑, "PNG", 寬, 高)
  → Presentation.Close() / Application.Quit()
```

還原度最高：字型、SmartArt、圖表、漸層、陰影都與 PowerPoint 畫面一致。

**如果你已經開著 PowerPoint，程式會借用它，而且不會關閉它。** PowerPoint 是單一執行個體的 COM 伺服器 —— 建立 COM 物件時拿到的是使用者正在用的那一個，因此程式在轉檔前會先確認 PowerPoint 是否已在執行：

| 情況 | 結束時的行為 |
|---|---|
| PowerPoint 原本沒開 | 由程式啟動，轉完一併關閉 |
| PowerPoint 原本就開著 | 借用，**不呼叫 Quit、不強制關閉程序**，並還原被改動的 `DisplayAlerts` 設定 |
| 目標簡報原本就開著 | 直接沿用該簡報，**不關閉它** |

**巨集一律停用。** Office 自動化的巨集安全性預設是 `msoAutomationSecurityLow`，也就是「直接執行巨集不詢問」。本程式在開啟任何外部簡報**之前**會先把 `Application.AutomationSecurity` 設為 `msoAutomationSecurityForceDisable`，結束時還原成原值。`.ppt` 與 `.pps` 這類舊格式可能夾帶巨集，這一步是必要的防護。（`.pptm` 不在支援的副檔名內，加不進清單。）

**強制收尾只針對確定屬於自己的程序。** 需要強制關閉時，PowerPoint 的 PID 由 `Application.HWND` 反查取得，LibreOffice 則以 Windows Job Object 綁定整棵程序樹。不做程序名稱掃描，因此不可能誤殺使用者在轉檔期間自己開啟的 Office 或 LibreOffice。

採用**晚期繫結**（`Type.GetTypeFromProgID` + `IDispatch`），因此：

- 專案不需要參考 `Microsoft.Office.Interop.PowerPoint`
- 建置機器不需要安裝 Office
- 同一份執行檔相容各種 Office 版本

### 2. LibreOffice（後備）

沒有 PowerPoint、或 PowerPoint 轉換失敗時自動接手：

```
soffice --headless --convert-to pdf:impress_pdf_Export
  → 用內建的 PDFium 把指定頁面算繪成 PNG（依設定寬度）
```

**為什麼要繞道 PDF？** LibreOffice 的 `--convert-to png` 只會輸出第一頁，而且無法指定輸出寬度。先轉 PDF 再算繪，才能同時支援頁碼範圍與自訂解析度。

LibreOffice 會以**獨立的暫存設定檔目錄**啟動（`-env:UserInstallation=...`），因此：

- 不會和使用者已經開著的 LibreOffice 互搶設定檔鎖
- 不會留下常駐的快速啟動程序

### 背景程序清理

只會關閉**明確登記過、確定由本程式啟動**的程序（含子程序）：PowerPoint 的 PID 由 `Application.HWND` 反查，LibreOffice 則以 Windows Job Object 綁住整棵程序樹。不做程序名稱掃描，也不比對 PID 差集，因此不可能誤殺使用者在轉檔期間自己開啟的 PowerPoint 或 LibreOffice。取消或逾時時同樣會強制收尾。

LibreOffice 的逾時**隨來源檔大小調整**（5 分鐘起跳，每 MB 加 6 秒，上限 30 分鐘）。固定值會讓大型簡報無論如何都轉不完——300 張含大量圖片的簡報在一般辦公室機器上很容易超過 5 分鐘。

---

## 錯誤處理策略

- **單一檔案失敗不會中斷整批工作。** 失敗的檔案標記為「失敗」並在該列顯示原因，其餘檔案照常繼續。
- **引擎逐級後備。** PowerPoint 丟出例外時記錄原因、清掉半成品，再交給 LibreOffice；兩者都失敗才判定該檔失敗，錯誤訊息會同時列出兩邊的原因。
- **引擎之間完全隔離。** 每個引擎都輸出到自己的暫存資料夾（建立在輸出根目錄底下，確保同磁碟區），成功後才整個搬到正式位置。即使前一個引擎留下半成品且因檔案被鎖住而刪不掉，下一個引擎也絕對不會寫進同一個資料夾。
- **開始前就擋下不可能成功的組合。** 若選了「只用 PowerPoint」但機器沒有 PowerPoint，開始按鈕會直接停用並說明原因，不會讓使用者按下去之後看到一整排失敗。介面與批次服務共用 `EngineAvailability` 的同一份規則。
- **絕不覆蓋。** 輸出資料夾與圖片檔同名時自動變成 `名稱 (2)`、`名稱 (3)`…；檢查時同時比對檔案與資料夾，避免同名衝突。
- **失敗不留垃圾。** 失敗或取消時會刪除還沒有任何圖片的空資料夾。
- **例外訊息中文化。** COM 錯誤碼會轉譯成使用者看得懂的說明（例如「PowerPoint 目前正忙碌或有對話視窗開啟」）。
- 完整技術細節寫入記錄檔：`%LOCALAPPDATA%\PptPngExporter\logs\`（介面右上角「轉換記錄」可直接開啟，只保留最近 14 天）。

---

## 中文、空白與長路徑

- 檔名與資料夾名稱經過 `FileNameSanitizer` 處理：移除 `< > : " / \ | ? *` 與控制字元、去除結尾空白與句點、避開 Windows 保留裝置名（`CON`、`NUL`、`COM1`…），**保留中文、日文與 emoji**。
- 資訊清單已開啟 `longPathAware`，另外在檔案操作時會視需要加上 `\\?\` 前綴，因此不依賴群組原則設定也能寫入超過 260 字元的路徑。
- PowerPoint 的 `Export` 對超長路徑支援不佳，因此路徑過長時會先輸出到短暫存路徑再搬移到目的地。
- LibreOffice 對長路徑同樣不穩定，來源檔一律先複製到短暫存路徑再轉換。

---

## 建置（開發者）

### 需要

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows（執行需要；**建置可在 Linux / macOS 進行**，專案已設定 `EnableWindowsTargeting`）
- [Inno Setup 6](https://jrsoftware.org/isdl.php)（只有要產生安裝程式時需要）

### 指令

#### 最簡單：雙擊 .bat

解壓縮原始碼後，打開 `build` 資料夾，雙擊：

- **`build-portable.bat`** → 產出單一 .exe 與 ZIP
- **`build-installer.bat`** → 產出 setup.exe

不需要開 Visual Studio，不需要動 PowerShell 執行原則。產出都在 `artifacts\` 資料夾。

#### 或用指令

```powershell
# 還原與建置
dotnet build PptPngExporter.sln -c Release

# 執行測試
dotnet test PptPngExporter.sln -c Release

# 直接執行（開發用）
dotnet run --project src\PptPngExporter.App

# 產生免安裝版 ZIP → artifacts\
powershell -ExecutionPolicy Bypass -File build\publish-portable.ps1

# 產生安裝程式 → artifacts\
powershell -ExecutionPolicy Bypass -File build\publish-installer-payload.ps1
```

> **編輯建置腳本時請注意編碼。**
> `.ps1` 與 `.iss` 必須存成 **UTF-8 with BOM**。Windows PowerShell 5.1 與 Inno Setup 在沒有 BOM 時會改用系統 ANSI 編碼（繁體中文 Windows 為 CP950/Big5）讀取檔案，中文內容會變成亂碼並破壞語法。
> `.bat` 則**必須是純 ASCII 且不能加 BOM**：所有中文訊息一律由 PowerShell 輸出（PowerShell 以 WriteConsoleW 寫主控台，不受 codepage 影響），批次檔本身不含任何非 ASCII 位元組，從根本上排除編碼問題。
> 測試專案的 `BuildScriptEncodingTests` 會自動檢查這些規則。

兩個發佈腳本都會**先跑測試**，測試沒過就不會產出檔案；也會先檢查 .NET 8 SDK 是否存在，缺少時直接給出下載連結而不是丟一堆紅字。

安裝程式的介面語言預設是英文，因為 Inno Setup 沒有內建繁體中文語系檔。想要中文介面，到 <https://jrsoftware.org/files/istrans/> 下載 `ChineseTraditional.isl`，放進 Inno Setup 安裝目錄的 `Languages\` 資料夾，再重新執行建置腳本即可自動切換。

---

## 專案結構

```
PptPngExporter/
├─ PptPngExporter.sln
├─ Directory.Build.props            共用建置設定（語言版本、版本號、Windows 目標）
├─ README.md
│
├─ src/
│  ├─ PptPngExporter.Core/          轉檔核心，無 UI 相依，可獨立測試
│  │  ├─ Models/Models.cs           ExportOptions / ExportResult / ProgressReport…
│  │  ├─ Parsing/PageRange.cs       頁碼字串解析與展開
│  │  ├─ IO/
│  │  │  ├─ FileNameSanitizer.cs    檔名清理
│  │  │  └─ PathSafety.cs           防覆蓋路徑 + 長路徑處理
│  │  ├─ Interop/ComObject.cs       晚期繫結 COM 包裝（含忙碌重試）
│  │  ├─ Converters/
│  │  │  ├─ ISlideConverter.cs      引擎介面與例外型別
│  │  │  ├─ EngineAvailability.cs   引擎可用性的單一判斷來源
│  │  │  ├─ OfficeSettingsGuard.cs  Office 設定的暫時覆寫與還原
│  │  │  ├─ PowerPointConverter.cs  PowerPoint COM 引擎
│  │  │  ├─ PowerPointSessionPolicy.cs 何時可以關閉 PowerPoint 的判斷
│  │  │  ├─ LibreOfficeConverter.cs LibreOffice + PDFium 引擎
│  │  │  └─ LibreOfficeLocator.cs   soffice.exe 探測
│  │  ├─ Updates/
│  │  │  ├─ UpdateModels.cs        版本比較、清單模型、更新策略判斷
│  │  │  ├─ UpdateSource.cs        安裝方式偵測、GitHub 發行來源、雜湊驗證
│  │  │  └─ UpdateService.cs       檢查、下載、就地替換
│  │  └─ Services/
│  │     ├─ BatchExportService.cs   批次流程、引擎後備、錯誤隔離
│  │     ├─ SlidePreviewService.cs  縮圖產生與快取
│  │     ├─ ProcessSupervision.cs   Job Object 與已登記程序的收尾
│  │     ├─ PresentationScanner.cs  資料夾遞迴掃描
│  │     └─ FileLogger.cs           記錄檔
│  │
│  └─ PptPngExporter.App/           WPF 介面
│     ├─ App.xaml(.cs)              啟動、全域例外處理
│     ├─ app.manifest               DPI 感知、長路徑、UTF-8
│     ├─ Resources/Theme.xaml       設計語彙（色彩、字體、控制項樣式）
│     ├─ Resources/app.ico
│     ├─ Views/MainWindow.xaml(.cs) 主畫面、拖放
│     ├─ Views/PageSelectionWindow.xaml(.cs) 縮圖挑選視窗
│     ├─ ViewModels/                MainViewModel / PresentationItem
│     └─ Infrastructure/            MVVM、Shell、STA 執行緒、設定、值轉換器
│
├─ tests/PptPngExporter.Tests/      xUnit，267 個測試
│  ├─ Assets/                      端到端測試用的真實簡報（6 頁與 10 頁）
│  ├─ PageRangeParserTests.cs
│  ├─ FileNameAndPathTests.cs
│  ├─ BatchExportServiceTests.cs
│  ├─ OutputIntegrityTests.cs      迴歸測試、中文長路徑、PowerPoint 工作階段策略
│  ├─ BuildScriptEncodingTests.cs 建置腳本的編碼與版本號規則
│  ├─ HardeningTests.cs           安全性與穩定性強化的測試
│  ├─ UpdateTests.cs              自動更新（68 個測試）
│  ├─ PageSelectionTests.cs        編號規則與挑選頁面的端到端驗證
│  └─ LibreOfficeIntegrationTests.cs
│
└─ build/
   ├─ build-release.bat             一鍵產生完整發行（含更新清單）
   ├─ publish-release.ps1           發行流程與 update-manifest.json 產生
   ├─ build-installer.bat           一鍵建置安裝版（雙擊即可）
   ├─ build-portable.bat            一鍵建置免安裝版（雙擊即可）
   ├─ publish-portable.ps1          免安裝版
   ├─ publish-installer-payload.ps1 安裝程式
   ├─ installer.iss                 Inno Setup 指令碼
   └─ run-tests.ps1
```

---

## 依賴項目

| 套件 | 版本 | 授權 | 用途 |
|---|---|---|---|
| .NET | 8.0 | MIT | 執行階段與 WPF |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) | 5.2.1 | MPL-2.0 | 把 LibreOffice 產生的 PDF 算繪成點陣圖 |
| SkiaSharp | （PDFtoImage 相依） | MIT | 影像編碼 |
| PDFium (bblanchon build) | （PDFtoImage 相依） | Apache-2.0 / BSD-3 | PDF 算繪引擎 |
| Microsoft.Win32.Registry | 5.0.0 | MIT | 從登錄檔尋找 LibreOffice |
| xUnit | 2.9.2 | Apache-2.0 | 測試（不隨程式散布） |

**沒有**使用 Office PIA、Aspose、GhostScript 等需要授權或額外安裝的元件。

PowerPoint 與 LibreOffice 是**執行期的外部程式**，本專案不散布它們。

> MPL-2.0 屬於檔案層級 copyleft：只要不修改 PDFtoImage 本身的原始碼，商業散布沒有問題，但需在說明文件保留上述授權標示。

---

## 測試

```
dotnet test PptPngExporter.sln
```

267 個測試，涵蓋：

| 測試檔 | 涵蓋範圍 |
|---|---|
| `PageRangeParserTests` | 空白視為全部、範例格式、單頁、重疊合併、亂序排序、顛倒修正、開放式區間、超出頁數裁切、全形字元容錯、各種不合法輸入 |
| `FileNameAndPathTests` | 非法字元、控制字元、結尾句點空白、Windows 保留名、空值退回預設、長度截斷、中文/日文/emoji 保留、前綴清理、補零規則；資料夾與檔案防覆蓋、遞增編號、檔案與資料夾同名衝突、既有檔案內容不被改寫 |
| `PageSelectionTests` | 連續編號 vs 原始頁碼、位數設定與自動位數、位數不足不截斷、由勾選頁碼建立範圍、每份簡報各自的頁面選擇；**端到端**：真的挑第 1、5、7 頁匯出，驗證檔名為 001/002/003 且**內容與單獨匯出的第 1、5、7 張位元組相同**；縮圖產生、排序與快取重用 |
| `UpdateTests` | 版本號解析與比較（含預發行版）；更新策略的 10 種分支；安裝方式偵測；就地替換、備份清理、ZIP 取出執行檔；SHA-256 驗證；GitHub API 解析與無清單時的相容退回；服務層的網路失敗、未設定、雜湊不符；**端到端**：以發行腳本實際產生的 JSON 格式走完下載→驗證→替換，並確認雜湊被竄改時執行檔不會被動到 |
| `HardeningTests` | 巨集安全性覆寫與還原（含多項逆序還原、不支援時的行為、重複 Dispose）；只結束登記過的程序、Job Object 平台行為；引擎可用性判斷的 10 組組合與介面／批次服務訊息一致性；暫存隔離（半成品鎖住仍不混入、成功不留暫存、失敗不留資料夾、取消保留已完成圖片）；資料夾遞迴掃描、上限、去重、排序、取消、無權限目錄；快取鍵含引擎與版本 |
| `BuildScriptEncodingTests` | `.ps1` / `.iss` 必須含 UTF-8 BOM、`.bat` 不可含 BOM、所有腳本必須是合法 UTF-8、一鍵批次檔存在且使用 `%~dp0` |
| `BatchExportServiceTests` | 副檔名判斷、**單檔失敗不中斷整批**、未預期例外的隔離、PowerPoint 失敗自動改用 LibreOffice、成功時不呼叫後備引擎、未安裝時跳過、兩者皆失敗的合併訊息、僅用單一引擎模式、失敗不留空資料夾、每份簡報獨立資料夾、重複執行不覆蓋、同名不同來源分開輸出、檔案不存在的訊息、取消行為、頁碼與前綴傳遞、進度回報、輸出根目錄自動建立、寬度上下限 |

轉檔引擎透過 `ISlideConverter` 介面注入，因此在**沒有安裝 PowerPoint 或 LibreOffice 的環境**（包含 Linux CI）也能完整驗證批次流程與錯誤處理。

---

## 已知限制

1. **借用中的 PowerPoint 若跳出對話框會擋住轉換。** 程式借用使用者已開啟的 PowerPoint 時，若該視窗有未關閉的對話框，自動化呼叫會被拒絕（已內建重試）。批次量大時，建議先關閉 PowerPoint 讓程式自行啟動一個乾淨的執行個體。
2. **PowerPoint 引擎需要互動式桌面工作階段。** 在 Windows 服務或某些工作排程器設定下（無使用者工作階段）COM 自動化會失敗，此時會自動改用 LibreOffice。
3. **取消時會保留已完成的圖片。** 中途停止時，該檔案已產生的圖片仍會搬到正式資料夾，狀態標記為「已取消」，不會整批丟棄。
4. **PowerPoint 一次只處理一份簡報。** COM 自動化不適合平行呼叫，因此批次是循序處理。大量檔案時 LibreOffice 路徑通常反而較快。
5. **LibreOffice 的字型還原度取決於系統字型。** 簡報使用的字型若未安裝，LibreOffice 會替換成相近字型，排版可能略有位移。需要 100% 還原請使用有安裝 PowerPoint 的電腦。
6. **受密碼保護的簡報無法轉換。** 程式不會跳出輸入密碼的對話框（那會卡住批次流程），會直接標記失敗並說明原因。
7. **動畫只會輸出第一個狀態。** 每頁輸出一張靜態圖，不會展開逐步動畫。
8. **免安裝版體積較大**（.exe 約 71 MB、ZIP 約 66 MB），因為內含完整的 .NET 執行階段。若確定使用者電腦已安裝 .NET 8 Desktop Runtime，可改用 framework-dependent 發佈縮到約 18 MB（實測值，其中大部分是 `libSkiaSharp.dll` 與 `pdfium.dll` 兩個原生元件）：
   ```powershell
   dotnet publish src\PptPngExporter.App -c Release -r win-x64 --self-contained false `
       -p:PublishSingleFile=true -p:DebugType=none -o artifacts\portable-lite
   ```
9. **僅支援 x64 / ARM64 Windows。** 未提供 32 位元版本。
10. **超長路徑仍受單一路徑段 255 字元限制。** 這是 NTFS 本身的限制，非本程式可繞過。
11. **縮圖預覽第一次需要完整轉換一次簡報。** 大型簡報可能要等幾秒到幾十秒（有進度顯示、可中止）。之後會走快取。
12. **投影片數量非常多時縮圖會吃記憶體。** 縮圖以 220 像素寬解碼，約每張 100 KB；上千張投影片時建議分批挑選。快取可在需要時手動刪除 `%LOCALAPPDATA%\PptPngExporter\preview`。
13. **免安裝版的單一 .exe** 首次啟動時需要自我解壓縮，比資料夾版慢 1～2 秒。介意的話可用 `publish-portable.ps1 -SingleFile $false`。

---

## 疑難排解

**「找不到 PowerPoint 或 LibreOffice」**
安裝 LibreOffice 即可（免費）。若已安裝在非標準位置，可設定環境變數 `LIBREOFFICE_PATH` 指向 `soffice.exe`。

**「PowerPoint 不認得自動化指令 …」（錯誤碼 0x80020003）**
代表 PowerPoint 的 COM 介面缺少預期的成員。記錄檔會寫出是哪一個成員。可先試著修復 Office（控制台 → 程式與功能 → Microsoft Office → 變更 → 修復），或在「轉換方式」改選「只用 LibreOffice」。

**轉換後我的 PowerPoint 被關掉了**
1.0.1 已修正。舊版無條件呼叫 `Quit()`，而 PowerPoint 是單一執行個體伺服器，等於關掉使用者自己的視窗。現在只會關閉由本程式啟動的執行個體。

**「PowerPoint 目前正忙碌或有對話視窗開啟」**
PowerPoint 有未關閉的對話框時會拒絕自動化呼叫。關閉所有 PowerPoint 視窗後重試；程式本身已內建重試機制（每 250 毫秒，最多 10 秒）。

**轉出來的字型跑掉**
代表該檔是用 LibreOffice 轉的，且系統缺少簡報使用的字型。安裝字型，或改用有 PowerPoint 的電腦。

**圖片檔名不是我想要的**
檔名由「前綴 + 頁碼」組成，補零位數依該次匯出的最大頁碼決定（最少兩位）。右側設定區會即時顯示預覽。

**想知道詳細錯誤原因**
右上角「轉換記錄」會開啟記錄檔資料夾。

---

## 驗證狀態

本專案在交付前經過下列實際驗證（非僅靜態檢查）：

| 項目 | 結果 |
|---|---|
| `dotnet build PptPngExporter.sln -c Release` | 0 錯誤、0 警告（含 XAML 編譯） |
| `dotnet test` | 267 個測試全數通過 |
| 端到端轉檔 | 以真實 6 頁 16:9 `.pptx` 實際跑完 LibreOffice → PDF → PDFium → PNG |
| 輸出尺寸 | 指定 1920 得到 1920×1080、指定 3840 得到 3840×2160（讀 PNG 檔頭驗證） |
| 頁碼範圍 | `2-3,6` 實際只產生 `02.png`、`03.png`、`06.png` |
| 中文／空白路徑 | `我的 簡報 資料夾\２０２５年 度 報告書.pptx` 實際轉換成功 |
| 防覆蓋 | 同一份簡報連跑兩次，得到 `重複\` 與 `重複 (2)\`，第一次的圖片位元組完全未變 |
| 單檔失敗隔離 | 正常／毀損／正常三份混合批次，結果為成功 2、失敗 1，且毀損檔不留下空資料夾 |
| 背景程序 | 轉檔前後比對 `soffice` / `soffice.bin` PID，實測無殘留 |
| 挑選頁面編號 | 實際挑第 1、5、7 頁匯出，檔名為 `投影片_001/002/003`，且三張圖與單獨匯出的第 1、5、7 張**位元組完全相同** |
| 縮圖服務 | 10 頁簡報產生 10 張縮圖、依頁序排列、第二次呼叫命中快取 |
| 免安裝版發佈 | `win-x64` 自帶執行階段發佈成功，`libSkiaSharp.dll` 與 `pdfium.dll` 正確嵌入 |

### 實機驗證記錄

測試環境：**Windows 10 64-bit，有安裝 Microsoft PowerPoint，未安裝 LibreOffice**（即「只有 PowerPoint」組態）。

| 日期 | 測試內容 | 引擎 | 結果 |
|---|---|---|---|
| 2026-07 | 安裝版：安裝、啟動、完整輸出 | PowerPoint | 通過 |
| 2026-07 | 安裝版：縮圖挑選頁面輸出 | PowerPoint | 通過 |
| 2026-07 | 免安裝版：啟動、完整輸出 | PowerPoint | 通過 |
| 2026-07 | 免安裝版：縮圖挑選頁面輸出 | PowerPoint | 通過 |

**因此已確認可用的功能**：

- PowerPoint COM 轉檔路徑（含 1.0.1 修正的 `Slides.Item` 呼叫方式，先前會回報 `0x80020003`）
- 縮圖預覽產生（同樣走 PowerPoint COM 路徑）
- 挑選頁面後的連續編號輸出
- WPF 介面在真實螢幕上的操作
- Inno Setup 安裝程式的安裝與啟動

**仍未實機驗證的環境／情境**：

- Windows 11、Windows 10 32-bit、ARM64
- 轉檔時 PowerPoint 正開著的「借用執行個體」路徑（1.0.1 的重點修正）
- LibreOffice 後備路徑（測試機未安裝；已在 Linux 容器完整驗證）
- PowerPoint 與 LibreOffice 都沒有的機器
- 32 位元 Office（x64 應用程式跨位元自動化 32 位元 Office，理論上可行但未實測）
- 受密碼保護、含巨集、毀損的簡報
- 超長路徑與網路磁碟機路徑
- 解除安裝流程與右鍵選單

**尚未在容器環境驗證的部分**（需要實體 Windows 機器）：

- PowerPoint COM 路徑：邏輯已完成且錯誤處理與 LibreOffice 路徑共用同一套流程，但容器內沒有 Office 可實測。首次在有 PowerPoint 的機器上使用時建議先用單一檔案試跑。
- WPF 視窗的實際外觀與互動：XAML 已通過編譯器驗證、所有按鈕都已接上命令，但未在真實螢幕上目視確認。
- Inno Setup 安裝程式：指令碼已備妥，但容器內沒有 Inno Setup 可編譯。

---

## 變更記錄

### 1.4.0

針對「大量投影片」與極端情境的一輪強化。起因是檢視一份 300 張投影片的簡報會踩到哪些問題。

1. **LibreOffice 的逾時改為隨檔案大小調整。** 原本固定 5 分鐘且與投影片數量無關，300 張含大量圖片的簡報在一般辦公室機器上很容易超過，結果整份失敗而使用者無處可調。現在是「5 分鐘 + 每 MB 6 秒」，上限 30 分鐘；逾時訊息也會建議拆檔或改用 PowerPoint。

2. **轉檔中途失敗不再丟掉已完成的圖片。** 原本任何例外都會刪掉暫存資料夾——在第 280 張失敗等於前面 279 張白做。現在會保留「產出最多的那一次嘗試」，等所有引擎都失敗後才交給使用者，狀態仍為失敗但會附上張數說明。後備引擎成功時半成品仍然會被清掉，不會出現兩個輸出資料夾。

3. **PDF 算繪改為一次載入。** 原本每頁都重設串流位置再呼叫一次 `ToImage`，PDFium 每次都重新解析整份文件（實測 300 頁的合成 PDF：逐頁 1247 ms、單次載入 271 ms，且每頁平均成本從 1.8 ms 漲到 4.2 ms）。改用 `Conversion.ToImages` 搭配頁碼子集，仍然是惰性序列，逐頁進度與取消都不受影響。

4. **COM 忙碌重試改為可中斷。** `ComObject` 本來就會在 `RPC_E_CALL_REJECTED` 等錯誤時重試 10 秒，但不理會取消權杖——使用者按下停止後，每一頁都還要等重試跑完。現在權杖會從 `Application` 往下繼承到 `Slides` / `Slide`。這三個 HRESULT 也補進了錯誤說明，不再只顯示十六進位碼。

5. **整批共用同一個 PowerPoint 執行個體。** 工作階段原本是「每個檔案」建立一次，使用者沒開 PowerPoint 時，每個檔案都要啟動再關閉一次，100 份簡報光冷啟動就要好幾分鐘。抽出 `PowerPointSession` 並新增 `ISlideConverter.BeginBatch`，整批只啟動一次。「不關閉使用者自己開著的 PowerPoint」的判斷邏輯原封不動。

6. **挑選視窗改為虛擬化。** 原本是巢狀 `ItemsControl` + `WrapPanel` 包在 `ScrollViewer` 裡，三者都不虛擬化，10 份各 300 頁會一次建立 3000 個視覺元素並解碼 3000 張圖。新增 `SlideBoardPanel`（實作 `IScrollInfo`）與純函式的 `BoardLayout`，實測 3010 個項目只具現化 25–33 個。

   > 這裡有個不容易發現的陷阱：`ScrollViewer` 必須放在 `ItemsControl` 的 **範本裡面**。包在外面的話面板不是 `ScrollContentPresenter` 的直接子項，WPF 不會採用它的 `IScrollInfo`，面板會拿到無限高度、所有項目都算「看得到」——虛擬化完全失效，但畫面看起來完全正常。第一版就是這樣寫的，靠煙霧測試才抓到。

7. **預覽會顯示逐頁進度。** `SlidePreviewService.GetPreview` 原本傳 `null` 給轉換器，只回報「第幾份檔案」。單一 300 頁的簡報按下預覽後，進度條會整段停在 0%、文字停在「（1 / 1）」好幾分鐘。

8. **縮圖快取會自動清理。** 快取鍵包含簡報的最後修改時間與程式版本，所以每改一次簡報、每更新一次程式就多一整套，舊的永遠不會再命中，而且只能靠手動「清除快取」。新增 `SweepCache`：啟動時在背景清掉超過 14 天沒用到的，總量超過 2 GB 時再從最久沒用到的開始刪。

9. **磁碟空間。** 開始前會檢查輸出位置，空間過低時提醒、低到放不下時直接說明而不是讓每個檔案各自撞上寫入失敗；寫入途中真的滿了也會翻譯成看得懂的訊息，而不是「發生未預期的錯誤」。

10. **免安裝版放在受保護資料夾時的更新訊息。** 放在 `Program Files` 之類位置時，就地替換會因權限失敗。現在會明確說明要換到有寫入權限的資料夾，或改用安裝版。

11. **`InstallationInfo.Detect` 取不出執行檔名稱時改判為開發建置**，而不是落到免安裝版分支去替換不該動的檔案。

測試從 273 增加到 305 項。挑選視窗的版面計算與 PowerPoint 工作階段另外用煙霧測試在真實環境驗證過（實際跑 PowerPoint 轉檔、離線 Measure/Arrange 檢查虛擬化）。

### 1.0.1

修正實際使用者回報的三個問題：

1. **不再關閉使用者自己開著的 PowerPoint。**
   PowerPoint 是單一執行個體的 COM 伺服器，舊版在 `finally` 中無條件呼叫 `Quit()`，等於幫使用者按下關閉，可能連未存檔的簡報一起帶走。現在轉檔前會先判斷 PowerPoint 是否已在執行，只結束由本程式啟動的執行個體；使用者原本就開著的簡報也不會被關閉。判斷邏輯抽成 `PowerPointSessionPolicy`，已有單元測試涵蓋。

2. **修正 `0x80020003 DISP_E_MEMBERNOTFOUND`。**
   舊版以 `BindingFlags.GetProperty` 呼叫 `Slides.Item(i)`，但 `Item` 在 PowerPoint 型別庫中宣告為**方法**而非屬性，因此名稱查詢失敗（`Count` 是真正的屬性，所以沒事）。現已改用 `InvokeMethod`，並讓通用取值同時帶上 `GetProperty | InvokeMethod` 兩個旗標。此外，COM 例外現在會**附上失敗的成員名稱**，避免記錄檔只寫「找不到成員」而無從判斷。

3. **兩種引擎都沒有時提供可以照做的指引。**
   舊版只顯示一句「請先安裝」，而且偵測結果永久快取 —— 使用者裝好 LibreOffice 回來，程式仍說沒有。現在：
   - 設定面板新增「**重新偵測**」按鈕，安裝後不必重開程式
   - 兩者皆無時顯示醒目卡片與「**下載 LibreOffice**」按鈕
   - 按下開始轉換會先出現說明對話框（含可攜版與 `LIBREOFFICE_PATH` 的替代做法），而不是讓每個檔案都標記失敗
   - 一併看管 `soffice.bin` 子程序

### 1.1.0

依使用者需求新增頁面挑選介面與輸出編號設定。

1. **看縮圖逐頁勾選。**
   新增第三種頁面選擇方式與獨立的挑選視窗：依簡報分組排出所有投影片縮圖，點一下切換勾選，支援本份全選 / 全不選 / 反選與全域全選。縮圖沿用既有的轉換引擎產生（只是寬度調小、輸出到暫存），因此不需要另外維護一套算繪程式碼；結果依「路徑 + 最後修改時間 + 檔案大小 + 寬度」快取，重開挑選視窗即時顯示，簡報被修改則自動失效。

2. **輸出編號可選連續或原始頁碼。**
   挑選第 1、5、7 頁時，預設輸出 `投影片_001/002/003`（連續編號）；需要對回原頁碼時可切換成 `投影片_001/005/007`。補零位數可選自動 / 2 / 3 / 4 位，**預設改為三位**。

3. **每份簡報可以有各自的頁面選擇。**
   `BatchExportService` 改為接受 `ExportJob` 清單，每個工作項目可帶自己的 `PageRangeSpec`；未指定時沿用整批設定。原本以字串清單呼叫的多載仍保留。

4. **建置腳本修正。**
   `publish-installer-payload.ps1` 原本用 `"$env:ProgramFiles(x86)\..."` 尋找 Inno Setup。PowerShell 在字串中只會展開 `$env:ProgramFiles`，後面的 `(x86)` 變成字面文字，結果去找 `C:\Program Files(x86)\`（少一個空格），導致明明裝了 Inno Setup 卻回報找不到。已改用 `${env:ProgramFiles(x86)}` 並加上登錄檔查詢。同時新增 .NET 8 SDK 檢查、`建置安裝版.bat` / `建置免安裝版.bat` 一鍵腳本，並讓 `installer.iss` 相容 Inno Setup 6.0～6.4（`x64compatible` 是 6.3 才引入的）。

5. **修正計數器不遞增的錯誤。**
   `progress?.Report(new SlideProgress(++done, ...))` 在 `progress` 為 null 時，C# 的 null 條件運算子會讓**整個引數不被求值**，`++done` 因此從未執行，導致所有圖片拿到相同序號（被防覆蓋機制改成 `001 (2)`、`001 (3)`）。改為獨立一行遞增。這個問題是在新增編號測試時才浮現的 —— 舊版的序號不依賴這個變數，所以一直沒有症狀。

### 1.1.1

修正建置腳本的檔案編碼問題（程式本體無變更）。

`publish-installer-payload.ps1`、`publish-portable.ps1`、`run-tests.ps1` 與 `installer.iss` 原本存成「UTF-8 不含 BOM」。在繁體中文 Windows 上，Windows PowerShell 5.1（`powershell.exe`）沒有 BOM 時會改用系統 ANSI 編碼（CP950/Big5）讀取 `.ps1`，中文註解與訊息變成亂碼，其中的位元組破壞了字串引號，產生一連串看似無關的語法錯誤：

```
Missing expression after ','.
The string is missing the terminator: ".
Write-Ok "$($_.FullName)嚗?([math]::Round(...
```

四個檔案均已加上 UTF-8 BOM 並統一為 CRLF。`.bat` 維持不含 BOM（cmd.exe 會把 BOM 當成指令的一部分）。新增 `BuildScriptEncodingTests` 自動驗證這些規則，避免日後編輯時再次發生。

### 1.2.0

一次處理外部程式碼審查提出的九個問題（另有一項「50 頁上限未實作」經查證為不存在的需求，未採納）。

**安全性**

1. **開啟簡報前一律停用巨集。** `Application.AutomationSecurity` 在 1.0.1 改寫工作階段邏輯時被遺漏（1.0.0 原本有）。Office 自動化的預設值是「直接執行巨集」，使用者拖入的 `.ppt` / `.pps` 可能夾帶巨集。現已在開檔前設為 `msoAutomationSecurityForceDisable`，並在結束時還原原值；還原邏輯抽成 `OfficeSettingsGuard`，有 8 個單元測試涵蓋。

2. **不再依程序名稱掃描殺除。** 舊的 `ProcessGuard` 用「轉檔前後 PID 差集」判斷殘留程序，使用者若在轉檔途中才開啟 PowerPoint 或 LibreOffice 也會被關掉，先前註解宣稱的「絕不誤殺」並不成立。現改為：LibreOffice 以 Windows **Job Object** 綁定整棵程序樹；PowerPoint 由 `Application.HWND` 反查 PID 後只管理該執行個體。`ProcessGuard` 已刪除，取代為只處理明確登記 PID 的 `OwnedProcessGuard`。

**正確性**

3. **引擎不可用時停用開始按鈕。** 選「只用 PowerPoint」但機器只有 LibreOffice 時，舊版仍可按下開始並整批失敗。新增 `EngineAvailability` 作為單一判斷來源，介面與 `BatchExportService` 共用，訊息一致。

4. **挑頁模式不再靜默輸出整份簡報。** 舊版只要「其中一份」挑過頁面就能開始，其餘未挑選的會退回輸出全部頁面。現在要求每一份已勾選的簡報都必須挑過，並在訊息中列出還缺哪幾份。

5. **後備引擎的輸出完全隔離。** 每個引擎輸出到自己的暫存資料夾，成功後才搬到正式位置。先前若半成品刪除失敗，第二個引擎會寫進同一個資料夾。取消時已完成的圖片會保留。

6. **縮圖快取鍵加入引擎與程式版本。** 先前只用路徑、時間、大小、寬度，LibreOffice 產生的縮圖可能被 PowerPoint 模式沿用，造成預覽與正式輸出不一致。

7. **拖入大型資料夾不再凍結介面。** 掃描改到背景執行緒，並明確指定 `IgnoreInaccessible = true` —— `Directory.EnumerateFiles` 的 `SearchOption` 多載走相容性設定，遇到沒有權限的子資料夾會擲出例外並中斷整個掃描，使用者會靜默地什麼都掃不到。同時加上單次 2000 份的上限與略過數回報。

**一致性**

8. **資訊清單只宣告實際支援的系統。** 移除 Windows 7 / 8 / 8.1 / Vista 的相容性 GUID，只保留涵蓋 Windows 10 與 11 的那一個。

9. **版本號只在一個地方定義。** `installer.iss` 不再寫死版本，改由建置腳本從 `Directory.Build.props` 讀出後以 `/DAppVersion` 傳入；免安裝版 ZIP 的檔名也會自動帶入版本號。

**建置腳本**

批次檔改為**純 ASCII 且檔名也是 ASCII**（`build-installer.bat`、`build-portable.bat`），所有中文訊息交由 PowerShell 輸出。這是繼 1.1.1 的 BOM 問題之後的根本性防護：批次檔完全不含非 ASCII 位元組，就不可能有編碼問題。新增測試驗證這項規則、`.ps1` / `.iss` 的 BOM、以及批次檔指向的腳本確實存在。

### 1.3.0

新增自動更新功能。

1. **程式內更新。** 啟動時（每 20 小時最多一次）與手動按鈕都可以向 GitHub Releases 查詢新版本。免安裝版以「改名替換」方式就地更新並自動重啟；安裝版則下載安裝程式並靜默執行。

2. **更新檔一律驗證 SHA-256。** 雜湊來自隨發行上傳的 `update-manifest.json`。驗證失敗立即中止，不會動到執行檔。沒有清單的發行（更早的版本）會被視為必須手動下載。

3. **重大變更可以擋下自動更新。** 發行時加 `-MajorChange` 會在清單標記 `requiresManualDownload`；也可以用 `-MinimumInAppUpdateFrom` 指定太舊的版本必須手動重裝。

4. **更新來源可設定。** `update.config.json` 隨執行檔一起發佈，換儲存庫或關閉自動檢查都不必重新編譯。免安裝版只發佈單一 .exe，使用者手上不會有這個設定檔，因此 `UpdateConfiguration` 編譯進去的預設值才是實際生效的來源，兩邊都要填。`內建的預設儲存庫必須是可用的` 這個測試會擋下把佔位字串發出去。

5. **新增 `build-release.bat` / `publish-release.ps1`**：一次產生免安裝版、安裝程式與更新清單，並列出上傳到 GitHub 的步驟。

6. **免安裝版改為直接發佈單一 .exe，不再包 ZIP。** 本來就是自帶執行階段的單一檔案，多包一層只是讓使用者多一個步驟。`PortableUpdateInstaller.ResolveExecutable` 因此同時支援 `.exe` 與 `.zip`——舊版發行的是 ZIP，那批使用者仍要能更新上來。

7. **發行檔名一律 ASCII。** GitHub 上傳 Release 附件時會把非 ASCII 字元換成句點，`PPT-PNG-匯出工具-安裝程式-1.3.0.exe` 會變成 `PPT-PNG-.-.-1.3.0.exe`。自動更新是拿清單裡的 `fileName` 去比對附件名稱，一旦被改名就找不到下載網址，更新會停在「這個更新檔沒有可用的下載網址」。

8. **修正 `publish-release.ps1` 產不出更新清單的問題。** `New-AssetEntry` 用 `Test-Path $file` 檢查存在，但 Windows PowerShell 5.1 把 `Get-ChildItem` 回傳的 `FileInfo` 轉成字串時只會得到檔名，等於拿相對路徑去比對目前工作目錄。除非剛好站在 `artifacts` 底下執行，否則兩個資產都會被判定為不存在，腳本以「找不到任何可發行的檔案」結束——`update-manifest.json` 從來沒有成功產生過。已改為以 `FullName` 判斷。

9. **修正 `publish-portable.ps1` 的 `dotnet publish` 參數組裝。** 陣列常值中逗號的優先順序高於 `+`，`'-p:PublishSingleFile=' + $x` 會被拆成兩個參數，MSBuild 回報 `MSB1008: 只能指定一個專案`。已改用字串內插。

10. **`InstallationInfo.Detect` 取不出執行檔名稱時改判為開發建置。** 原本只在名稱為 `dotnet` 時判定開發建置，空字串會落到免安裝版分支。對應的測試本來傳 `null`（意思是「沿用目前程序」），結果取決於測試主機叫 `dotnet.exe` 還是 `testhost.exe`，換機器就會紅；已改成傳空字串，測的是真正想測的分支。
