@echo off
rem Rebuild the launcher binaries from src\DshLauncher.cs.
rem Needs the .NET Framework compiler (csc.exe), which ships with Windows 10/11.
rem Builds dsh-launcher.exe; copy it to dsh一键启动.exe to use the Chinese name.
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] csc.exe not found.
    exit /b 1
)
"%CSC%" /nologo /codepage:65001 /optimize+ /target:exe /out:dsh-launcher.exe /r:System.IO.Compression.FileSystem.dll src\DshLauncher.cs
if errorlevel 1 (
    echo [ERROR] build failed.
    exit /b 1
)
echo [OK] built dsh-launcher.exe
echo To use the Chinese filename: copy /y dsh-launcher.exe dsh一键启动.exe
