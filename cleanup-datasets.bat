@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

echo [cleanup] Root: %ROOT%

echo [cleanup] Stopping API process if running...
taskkill /IM InsightEngine.API.exe /F >nul 2>&1

set "REMOVED=0"
set "MISSING=0"
set "FAILED=0"

call :RemoveDir "%ROOT%\src\InsightEngine.API\uploads"
call :RemoveDir "%ROOT%\src\InsightEngine.API\App_Data"
call :RemoveDir "%ROOT%\uploads"
call :RemoveDir "%ROOT%\App_Data"

echo [cleanup] Removing runtime copies under src\InsightEngine.API\bin\Debug\net* ...
for /d %%B in ("%ROOT%\src\InsightEngine.API\bin\Debug\net*") do (
    call :RemoveDir "%%~fB\uploads"
    call :RemoveDir "%%~fB\App_Data"
)

echo [cleanup] Removing runtime copies under src\InsightEngine.API\bin\Release\net* ...
for /d %%B in ("%ROOT%\src\InsightEngine.API\bin\Release\net*") do (
    call :RemoveDir "%%~fB\uploads"
    call :RemoveDir "%%~fB\App_Data"
)

echo [cleanup] Removing runtime copies under src\InsightEngine.API\obj\Debug\net* ...
for /d %%B in ("%ROOT%\src\InsightEngine.API\obj\Debug\net*") do (
    call :RemoveDir "%%~fB\uploads"
    call :RemoveDir "%%~fB\App_Data"
)

echo [cleanup] Removing runtime copies under src\InsightEngine.API\obj\Release\net* ...
for /d %%B in ("%ROOT%\src\InsightEngine.API\obj\Release\net*") do (
    call :RemoveDir "%%~fB\uploads"
    call :RemoveDir "%%~fB\App_Data"
)

echo [cleanup] Removing loose SQLite files named insightengine-metadata.db ...
for /r "%ROOT%" %%F in (insightengine-metadata.db) do (
    if exist "%%~fF" (
        del /f /q "%%~fF" >nul 2>&1
        if exist "%%~fF" (
            echo [warn] Could not remove file: %%~fF
            set /a FAILED+=1
        ) else (
            echo [ok] Removed file: %%~fF
            set /a REMOVED+=1
        )
    )
)

echo.
echo [cleanup] Done. Removed: !REMOVED!  Missing: !MISSING!  Failed: !FAILED!
if !FAILED! GTR 0 (
    echo [cleanup] Some files were locked. Stop any running API/debug session and run this script again.
)
exit /b 0

:RemoveDir
set "TARGET=%~1"
if not exist "%TARGET%" (
    echo [skip] Missing: %TARGET%
    set /a MISSING+=1
    exit /b 0
)

rmdir /s /q "%TARGET%" >nul 2>&1
if exist "%TARGET%" (
    echo [warn] Could not remove: %TARGET%
    set /a FAILED+=1
) else (
    echo [ok] Removed: %TARGET%
    set /a REMOVED+=1
)
exit /b 0
