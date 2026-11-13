@echo off
cd "%~dp0client"
call "%~dp0client\setup.bat"
::If you want to force the COM port add it to the line, example:
::bash "%~dp0client\pm3-flash-all" COM3
bash "%~dp0client\pm3-flash-all"
pause