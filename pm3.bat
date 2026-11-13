@echo off
title Proxmark3 RRG/Iceman Console
color 0a

echo ===================================================
echo     Starting Proxmark3 Iceman Client...
echo ===================================================
echo.

cd /d "%~dp0client"
call "%~dp0client\setup.bat"

:: If a port argument was given (e.g. pm3.bat COM8), use that port directly
if not "%~1"=="" (
    echo [*] Connecting to %~1 ...
    "%~dp0client\proxmark3.exe" %~1 -w
    goto :DONE
)

:: Connect directly to COM9
echo [*] Connecting to Proxmark3 on COM9 ...
"%~dp0client\proxmark3.exe" COM9 -w

:: If COM9 fails, run auto-detection through bash pm3
if errorlevel 1 (
    echo.
    echo [*] COM9 not responding, starting auto-detection...
    bash "%~dp0client\pm3"
)

:DONE
echo.
echo ===================================================
echo     Proxmark3 session ended.
echo ===================================================
pause