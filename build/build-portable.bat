@echo off
chcp 65001 >nul
title 建置 PPT PNG 匯出工具（免安裝版）
echo.
echo   正在建置免安裝版，請稍候。第一次執行需要下載相依套件，可能要幾分鐘。
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-portable.ps1"
echo.
if errorlevel 1 (
  echo   建置失敗，請看上方的訊息。
) else (
  echo   建置完成。產出在 artifacts 資料夾。
)
echo.
pause
