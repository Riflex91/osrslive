@echo off
setlocal EnableExtensions
cd /d "%~dp0"
if not exist "logs" mkdir "logs"
set "LOG=%CD%\logs\startup.log"
>> "%LOG%" echo.
>> "%LOG%" echo ===== WINDOWS STARTER %DATE% %TIME% =====
>> "%LOG%" echo [CMD] PowerShell-Launcher wird gestartet ...
echo OSRS Live Diagnostics: starte ...
if not exist "%~dp0START-OSRS-Diagnostics.ps1" goto missing_script
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0START-OSRS-Diagnostics.ps1"
set "RC=%errorlevel%"
if not "%RC%"=="0" goto failure
exit /b 0
:missing_script
>> "%LOG%" echo [ERROR] START-OSRS-Diagnostics.ps1 fehlt.
set "RC=2"
:failure
echo.
echo STARTFEHLER - Fehlercode %RC%
type "%LOG%"
echo Bitte COPY-STARTUP-LOG.cmd starten.
pause
exit /b %RC%
