@echo off
setlocal DisableDelayedExpansion
chcp 65001 >nul 2>&1
title Sephiria Optimizer Uninstaller

where pwsh.exe >nul 2>&1
if not errorlevel 1 goto :RunPwsh
if exist "%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" goto :RunWinPs
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
exit /b %ERRORLEVEL%

:RunPwsh
pwsh.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
exit /b %ERRORLEVEL%

:RunWinPs
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Uninstall %*
exit /b %ERRORLEVEL%
