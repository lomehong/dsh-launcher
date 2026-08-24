@echo off
rem dsh 完全卸载（ASCII-only on purpose: any Windows codepage can parse this file）
rem 双击：默认保留 ~/.dsh 用户 profile；如需一并清空，把 --yes 改为 --purge --yes
cd /d "%~dp0"
if not exist "dsh-launcher.exe" goto missing
echo ==============================================================
echo   即将删除：
echo     [1] %LOCALAPPDATA%\dsh-launcher  (便携 Node + dsh + 启动器数据)
echo     [2] ~/.dsh  (用户 profile + 插件)  ← 仅当 launcher 传 --purge
echo   关闭所有 dsh web 窗口和后台进程后再继续。
echo ==============================================================
echo.
"dsh-launcher.exe" --uninstall
goto end
::missing
echo [ERROR] dsh-launcher.exe was not found next to this file.
echo If you renamed the program, edit this .bat so it points to the new name.
pause
::end
