@echo off
setlocal
set CFG=%~1
if "%CFG%"=="" set CFG=Release
echo === Building DshLauncher CLI (%CFG%) ===
dotnet publish src/DshLauncher.Cli/DshLauncher.Cli.csproj -c %CFG% -f net10.0-windows -r win-x64 --self-contained false -o "%~dp0dist\cli"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
copy /y "%~dp0dist\cli\dsh-launcher.exe" "%~dp0dsh-launcher.exe" >nul
copy /y "%~dp0dist\cli\dsh-launcher.exe" "%~dp0dsh一键启动.exe" >nul
copy /y "%~dp0dist\cli\dsh-launcher.dll" "%~dp0dsh-launcher.dll" >nul
copy /y "%~dp0dist\cli\DshLauncher.Core.dll" "%~dp0DshLauncher.Core.dll" >nul
copy /y "%~dp0dist\cli\dsh-launcher.deps.json" "%~dp0dsh-launcher.deps.json" >nul
copy /y "%~dp0dist\cli\dsh-launcher.runtimeconfig.json" "%~dp0dsh-launcher.runtimeconfig.json" >nul

echo === Building DshLauncher GUI (%CFG%) ===
dotnet publish src/DshLauncher.Gui/DshLauncher.Gui.csproj -c %CFG% -f net10.0-windows -r win-x64 --self-contained false -o "%~dp0dist\gui"
if errorlevel 1 (
  echo BUILD FAILED
  exit /b 1
)
copy /y "%~dp0dist\gui\dsh-launcher-gui.exe" "%~dp0dsh-launcher-gui.exe" >nul
copy /y "%~dp0dist\gui\dsh-launcher-gui.dll" "%~dp0dsh-launcher-gui.dll" >nul
copy /y "%~dp0dist\gui\dsh-launcher-gui.runtimeconfig.json" "%~dp0dsh-launcher-gui.runtimeconfig.json" >nul
copy /y "%~dp0dist\gui\dsh-launcher-gui.deps.json" "%~dp0dsh-launcher-gui.deps.json" >nul
copy /y "%~dp0src\DshLauncher.Gui\app.ico" "%~dp0app.ico" >nul
copy /y "%~dp0src\DshLauncher.Gui\app.ico" "%~dp0dsh-launcher-gui.ico" >nul

echo BUILD OK - CLI and GUI published.
