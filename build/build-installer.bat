@echo off
setlocal
rem ---------------------------------------------------------------
rem  PPT PNG Exporter - build launcher
rem
rem  This file is intentionally pure ASCII and does NOT change the
rem  console code page. All localized output is produced by the
rem  PowerShell script, which writes to the console via WriteConsoleW
rem  and is therefore unaffected by the active code page.
rem
rem  Do not add a UTF-8 BOM to this file: cmd.exe would treat the BOM
rem  bytes as part of the first command.
rem ---------------------------------------------------------------
title PPT PNG Exporter - installer

where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: Windows PowerShell was not found.
  echo Please run the script manually:  build\publish-installer-payload.ps1
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-installer-payload.ps1"
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
