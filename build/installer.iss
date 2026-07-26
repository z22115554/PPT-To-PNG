; PPT PNG 匯出工具 — Inno Setup 安裝程式指令碼
; 需要 Inno Setup 6：https://jrsoftware.org/isdl.php
; 建置前請先執行 build\publish-installer-payload.ps1 產生 artifacts\installer-payload

#define AppName "PPT PNG 匯出工具"
; 版本號由 publish-installer-payload.ps1 從 Directory.Build.props 讀出後以 /DAppVersion 傳入，
; 避免兩個地方各寫一份而忘記同步。手動用 Inno Setup 開啟時才會用到下面的預設值。
#ifndef AppVersion
  #define AppVersion "0.0.0-manual"
#endif
#define AppPublisher "PPT PNG Exporter"
#define AppExeName "PPT PNG 匯出工具.exe"

[Setup]
AppId={{9F2C1E64-4C3B-4A0E-9B4E-5D8A2C7F1A20}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\PptPngExporter
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\artifacts
; 檔名保持純 ASCII：GitHub 上傳 Release 附件會把非 ASCII 字元換成句點，
; 而自動更新要用 update-manifest.json 裡的 fileName 去比對附件名稱。
OutputBaseFilename=PPT-PNG-Exporter-v{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; 一般使用者權限即可安裝到自己的帳號底下
PrivilegesRequiredOverridesAllowed=dialog
#if Ver >= EncodeVer(6,3,0,0)
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
#else
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
#endif
UninstallDisplayIcon={app}\{#AppExeName}
SetupIconFile=..\src\PptPngExporter.App\Resources\app.ico

[Languages]
; Inno Setup 沒有內建繁體中文語系檔。
; build\publish-installer-payload.ps1 偵測到 Languages\ChineseTraditional.isl 時會自動傳入 /DCHINESE。
; 語系檔下載：https://jrsoftware.org/files/istrans/
#ifdef CHINESE
Name: "cht"; MessagesFile: "compiler:Languages\ChineseTraditional.isl"
#else
Name: "en"; MessagesFile: "compiler:Default.isl"
#endif

[Tasks]
Name: "desktopicon"; Description: "建立桌面捷徑"; GroupDescription: "其他選項:"; Flags: checkedonce

[Files]
Source: "..\artifacts\installer-payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\移除 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Registry]
; 在檔案總管的右鍵選單加入「用 PPT PNG 匯出工具開啟」
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pptx\shell\PptPngExporter"; ValueType: string; ValueName: ""; ValueData: "用 {#AppName} 開啟"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.pptx\shell\PptPngExporter\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ppt\shell\PptPngExporter"; ValueType: string; ValueName: ""; ValueData: "用 {#AppName} 開啟"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\SystemFileAssociations\.ppt\shell\PptPngExporter\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#AppExeName}"; Description: "立即開啟 {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 移除程式自己產生的設定與記錄
Type: filesandordirs; Name: "{localappdata}\PptPngExporter"
Type: filesandordirs; Name: "{userappdata}\PptPngExporter"
