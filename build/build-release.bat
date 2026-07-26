@echo off
setlocal
rem ---------------------------------------------------------------
rem  PPT PNG Exporter - build a full release (portable + installer
rem  + update manifest for the in-app updater).
rem
rem  Pure ASCII on purpose. All localized output comes from the
rem  PowerShell script. Never add a UTF-8 BOM to this file.
rem ---------------------------------------------------------------
title PPT PNG Exporter - release

where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: Windows PowerShell was not found.
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-release.ps1"
set "RC=%ERRORLEVEL%"

echo.
if not "%RC%"=="0" (
  echo Build FAILED. Please read the messages above.
) else (
  echo Build finished. Output is in the "artifacts" folder.
)
echo.
pause
exit /b %RC%
