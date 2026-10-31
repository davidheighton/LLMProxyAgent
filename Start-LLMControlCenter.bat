@echo off
setlocal

set "APP_DIR=%~dp0LLMControlCenter\bin\Debug\net10.0"
set "APP_EXE=%APP_DIR%\MTC.exe"
set "SOLUTION=%~dp0LLMControlCenter\LLMControlCenter.sln"
set "BUILD_LOG_DIR=%LOCALAPPDATA%\LLMControlCenter"
set "BUILD_LOG=%BUILD_LOG_DIR%\startup-build.log"

if not exist "%SOLUTION%" (
    echo LLM Control Center solution was not found at:
    echo "%SOLUTION%"
    exit /b 1
)

powershell -NoProfile -Command "if (Get-Process -Name MTC -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"
if not errorlevel 1 (
    echo LLM Control Center is already running.
    exit /b 0
)

if not exist "%BUILD_LOG_DIR%" mkdir "%BUILD_LOG_DIR%"
dotnet build "%SOLUTION%" --no-restore --verbosity minimal > "%BUILD_LOG%" 2>&1
if errorlevel 1 (
    echo LLM Control Center could not be built. See:
    echo "%BUILD_LOG%"
    exit /b 1
)

if not exist "%APP_EXE%" (
    echo Build succeeded but the application was not found at:
    echo "%APP_EXE%"
    exit /b 1
)

powershell -NoProfile -Command "try { $assembly = [System.Reflection.Assembly]::LoadFile('%APP_DIR%\MTC.dll'); if ($assembly.GetManifestResourceNames() -contains '!AvaloniaResources') { exit 0 } else { exit 1 } } catch { exit 1 }"
if errorlevel 1 (
    echo The client build is missing embedded UI assets. Rebuilding without incremental outputs...
    dotnet build "%SOLUTION%" --no-restore --no-incremental --verbosity minimal > "%BUILD_LOG%" 2>&1
    if errorlevel 1 (
        echo LLM Control Center could not be rebuilt. See:
        echo "%BUILD_LOG%"
        exit /b 1
    )
    powershell -NoProfile -Command "try { $assembly = [System.Reflection.Assembly]::LoadFile('%APP_DIR%\MTC.dll'); if ($assembly.GetManifestResourceNames() -contains '!AvaloniaResources') { exit 0 } else { exit 1 } } catch { exit 1 }"
    if errorlevel 1 (
        echo The rebuilt client is still missing embedded UI assets. See:
        echo "%BUILD_LOG%"
        exit /b 1
    )
)

start "" /D "%APP_DIR%" "%APP_EXE%" %*
powershell -NoProfile -Command "Start-Sleep -Seconds 3"
powershell -NoProfile -Command "if (Get-Process -Name MTC -ErrorAction SilentlyContinue) { exit 0 } else { exit 1 }"
if errorlevel 1 (
    echo The client exited during startup. Check the latest entry in:
    echo "%~dp0LLMControlCenter\logs\mtc_debug.log"
    exit /b 1
)
exit /b 0
