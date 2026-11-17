@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0INSTALAR_LAUNCHER.ps1"
if errorlevel 1 (
  echo.
  echo No se pudo completar la instalacion. Revisa el mensaje anterior.
  pause
)
endlocal
