@echo off
rem dsh environment self-check (ASCII-only on purpose: any Windows codepage can parse this file)
cd /d "%~dp0"
if not exist "dsh-launcher.exe" goto missing
"dsh-launcher.exe" --check
goto end
:missing
echo [ERROR] dsh-launcher.exe was not found next to this file.
echo If you renamed the program, edit this .bat so it points to the new name.
pause
:end
