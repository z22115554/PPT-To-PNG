# PPT PNG 匯出工具

批次把多份 PowerPoint 簡報的每一頁，轉存成高畫質 PNG 圖片。

介面全繁體中文，設計給不熟悉技術的一般使用者：拖進來、按開始，就會在指定資料夾裡看到一份份分好的圖片。

---

## 下載

到 [Releases](https://github.com/z22115554/PPT-To-PNG/releases/latest) 取得最新版本：

| 檔案 | 說明 |
|---|---|
| `PPT-PNG-匯出工具-安裝程式-1.1.1.exe` | 安裝版，會建立開始功能表捷徑與右鍵選單 |
| `PPT-PNG-匯出工具-免安裝版-win-x64.zip` | 免安裝版，解壓縮就能用 |

兩種都自帶 .NET 執行階段，不需要另外安裝。

---

## 快速開始（使用者）

### 免安裝版

1. 解壓縮 `PPT-PNG-匯出工具-免安裝版-win-x64.zip` 到任何位置。
2. 雙擊 **PPT PNG 匯出工具.exe**。

不需要安裝 .NET，不需要系統管理員權限。

### 安裝版

執行 `PPT-PNG-匯出工具-安裝程式-1.1.1.exe`，依畫面指示完成。安裝後可從開始功能表開啟，也可以在 `.ppt` / `.pptx` 檔案上按右鍵選「用 PPT PNG 匯出工具開啟」。

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
- 簡報檔一旦被修改，快取自動失效並重新產生（快取鍵包含檔案的最後修改時間與大小）
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

## 系統需求

- Windows 10 1607 以上（64 位元），或 Windows 11
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

轉檔前後都會比對 `POWERPNT` / `soffice` 的 PID 清單，**只**關閉本程式產生而未正常結束的程序（含子程序），不會誤殺使用者自己開著的 PowerPoint 或 LibreOffice。取消或逾時（預設 5 分鐘）時同樣會強制收尾。

---

## 錯誤處理策略

- **單一檔案失敗不會中斷整批工作。** 失敗的檔案標記為「失敗」並在該列顯示原因，其餘檔案照常繼續。
- **引擎逐級後備。** PowerPoint 丟出例外時記錄原因、清掉半成品，再交給 LibreOffice；兩者都失敗才判定該檔失敗，錯誤訊息會同時列出兩邊的原因。
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

- **`建置免安裝版.bat`** → 產出單一 .exe 與 ZIP
- **`建置安裝版.bat`** → 產出 setup.exe

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
> `.bat` 則**絕對不能加 BOM**，否則 cmd.exe 會把 BOM 位元組當成第一個指令的一部分。
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
│  │  │  ├─ PowerPointConverter.cs  PowerPoint COM 引擎
│  │  │  ├─ PowerPointSessionPolicy.cs 何時可以關閉 PowerPoint 的判斷
│  │  │  ├─ LibreOfficeConverter.cs LibreOffice + PDFium 引擎
│  │  │  └─ LibreOfficeLocator.cs   soffice.exe 探測
│  │  └─ Services/
│  │     ├─ BatchExportService.cs   批次流程、引擎後備、錯誤隔離
│  │     ├─ SlidePreviewService.cs  縮圖產生與快取
│  │     ├─ ProcessGuard.cs         背景程序清理
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
├─ tests/PptPngExporter.Tests/      xUnit，155 個測試
│  ├─ Assets/                      端到端測試用的真實簡報（6 頁與 10 頁）
│  ├─ PageRangeParserTests.cs
│  ├─ FileNameAndPathTests.cs
│  ├─ BatchExportServiceTests.cs
│  ├─ OutputIntegrityTests.cs      迴歸測試、中文長路徑、PowerPoint 工作階段策略
│  ├─ BuildScriptEncodingTests.cs 建置腳本的 BOM 與 UTF-8 規則
│  ├─ PageSelectionTests.cs        編號規則與挑選頁面的端到端驗證
│  └─ LibreOfficeIntegrationTests.cs
│
└─ build/
   ├─ 建置免安裝版.bat              一鍵建置（雙擊即可）
   ├─ 建置安裝版.bat                一鍵建置（雙擊即可）
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

155 個測試，涵蓋：

| 測試檔 | 涵蓋範圍 |
|---|---|
| `PageRangeParserTests` | 空白視為全部、範例格式、單頁、重疊合併、亂序排序、顛倒修正、開放式區間、超出頁數裁切、全形字元容錯、各種不合法輸入 |
| `FileNameAndPathTests` | 非法字元、控制字元、結尾句點空白、Windows 保留名、空值退回預設、長度截斷、中文/日文/emoji 保留、前綴清理、補零規則；資料夾與檔案防覆蓋、遞增編號、檔案與資料夾同名衝突、既有檔案內容不被改寫 |
| `PageSelectionTests` | 連續編號 vs 原始頁碼、位數設定與自動位數、位數不足不截斷、由勾選頁碼建立範圍、每份簡報各自的頁面選擇；**端到端**：真的挑第 1、5、7 頁匯出，驗證檔名為 001/002/003 且**內容與單獨匯出的第 1、5、7 張位元組相同**；縮圖產生、排序與快取重用 |
| `BuildScriptEncodingTests` | `.ps1` / `.iss` 必須含 UTF-8 BOM、`.bat` 不可含 BOM、所有腳本必須是合法 UTF-8、一鍵批次檔存在且使用 `%~dp0` |
| `BatchExportServiceTests` | 副檔名判斷、**單檔失敗不中斷整批**、未預期例外的隔離、PowerPoint 失敗自動改用 LibreOffice、成功時不呼叫後備引擎、未安裝時跳過、兩者皆失敗的合併訊息、僅用單一引擎模式、失敗不留空資料夾、每份簡報獨立資料夾、重複執行不覆蓋、同名不同來源分開輸出、檔案不存在的訊息、取消行為、頁碼與前綴傳遞、進度回報、輸出根目錄自動建立、寬度上下限 |

轉檔引擎透過 `ISlideConverter` 介面注入，因此在**沒有安裝 PowerPoint 或 LibreOffice 的環境**（包含 Linux CI）也能完整驗證批次流程與錯誤處理。

---

## 已知限制

1. **借用中的 PowerPoint 若跳出對話框會擋住轉換。** 程式借用使用者已開啟的 PowerPoint 時，若該視窗有未關閉的對話框，自動化呼叫會被拒絕（已內建重試）。批次量大時，建議先關閉 PowerPoint 讓程式自行啟動一個乾淨的執行個體。
2. **PowerPoint 引擎需要互動式桌面工作階段。** 在 Windows 服務或某些工作排程器設定下（無使用者工作階段）COM 自動化會失敗，此時會自動改用 LibreOffice。
3. **PowerPoint 一次只處理一份簡報。** COM 自動化不適合平行呼叫，因此批次是循序處理。大量檔案時 LibreOffice 路徑通常反而較快。
4. **LibreOffice 的字型還原度取決於系統字型。** 簡報使用的字型若未安裝，LibreOffice 會替換成相近字型，排版可能略有位移。需要 100% 還原請使用有安裝 PowerPoint 的電腦。
5. **受密碼保護的簡報無法轉換。** 程式不會跳出輸入密碼的對話框（那會卡住批次流程），會直接標記失敗並說明原因。
6. **動畫只會輸出第一個狀態。** 每頁輸出一張靜態圖，不會展開逐步動畫。
7. **免安裝版體積較大**（.exe 約 71 MB、ZIP 約 66 MB），因為內含完整的 .NET 執行階段。若確定使用者電腦已安裝 .NET 8 Desktop Runtime，可改用 framework-dependent 發佈縮到約 18 MB（實測值，其中大部分是 `libSkiaSharp.dll` 與 `pdfium.dll` 兩個原生元件）：
   ```powershell
   dotnet publish src\PptPngExporter.App -c Release -r win-x64 --self-contained false `
       -p:PublishSingleFile=true -p:DebugType=none -o artifacts\portable-lite
   ```
8. **僅支援 x64 / ARM64 Windows。** 未提供 32 位元版本。
9. **超長路徑仍受單一路徑段 255 字元限制。** 這是 NTFS 本身的限制，非本程式可繞過。
10. **縮圖預覽第一次需要完整轉換一次簡報。** 大型簡報可能要等幾秒到幾十秒（有進度顯示、可中止）。之後會走快取。
11. **投影片數量非常多時縮圖會吃記憶體。** 縮圖以 220 像素寬解碼，約每張 100 KB；上千張投影片時建議分批挑選。快取可在需要時手動刪除 `%LOCALAPPDATA%\PptPngExporter\preview`。
12. **免安裝版的單一 .exe** 首次啟動時需要自我解壓縮，比資料夾版慢 1～2 秒。介意的話可用 `publish-portable.ps1 -SingleFile $false`。

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
| `dotnet test` | 155 個測試全數通過 |
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

**尚未在本環境驗證的部分**（需要實體 Windows 機器）：

- PowerPoint COM 路徑：邏輯已完成且錯誤處理與 LibreOffice 路徑共用同一套流程，但容器內沒有 Office 可實測。首次在有 PowerPoint 的機器上使用時建議先用單一檔案試跑。
- WPF 視窗的實際外觀與互動：XAML 已通過編譯器驗證、所有按鈕都已接上命令，但未在真實螢幕上目視確認。
- Inno Setup 安裝程式：指令碼已備妥，但容器內沒有 Inno Setup 可編譯。

---

## 變更記錄

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
