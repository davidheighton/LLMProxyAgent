@echo off
setlocal

set "LAUNCHER=%~dp0Start-LLMControlCenter.bat"
set "APP_EXE=%~dp0LLMControlCenter\bin\Debug\net10.0\MTC.exe"
if not exist "%LAUNCHER%" (
    echo LLM Control Center launcher was not found:
    echo "%LAUNCHER%"
    exit /b 1
)
if not exist "%APP_EXE%" (
    echo LLM Control Center was not built at:
    echo "%APP_EXE%"
    echo Build it before installing the startup shortcut.
    exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "$startup = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'; $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut((Join-Path $startup 'LLM Control Center.lnk')); $shortcut.TargetPath = '%LAUNCHER%'; $shortcut.WorkingDirectory = '%~dp0'; $shortcut.WindowStyle = 7; $shortcut.Description = 'Start LLM Control Center when Windows signs in'; $shortcut.Save()"
if errorlevel 1 (
    echo Could not add LLM Control Center to Windows startup.
    exit /b 1
)

echo LLM Control Center will start when you sign in to Windows.
exit /b 0
