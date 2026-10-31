@echo off
setlocal

powershell -NoProfile -ExecutionPolicy Bypass -Command "$shortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup\LLM Control Center.lnk'; if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }"
if errorlevel 1 (
    echo Could not remove LLM Control Center from Windows startup.
    exit /b 1
)

echo LLM Control Center will no longer start automatically at Windows sign-in.
exit /b 0
