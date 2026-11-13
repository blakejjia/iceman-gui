@echo off
set "CLIENT=%~dp0"
set "HOME=%APPDATA%\iceman-gui\client-home"
if not exist "%HOME%" mkdir "%HOME%"
set "QT_PLUGIN_PATH=%CLIENT%libs\"
set "QT_QPA_PLATFORM_PLUGIN_PATH=%QT_PLUGIN_PATH%"
set "PATH=%QT_PLUGIN_PATH%;%QT_PLUGIN_PATH%shell\;%PATH%"
set MSYSTEM=MINGW64