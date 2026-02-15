@echo off
setlocal enabledelayedexpansion

echo.
echo ========================================
echo  InsightEngine - Setup e Inicializacao
echo ========================================
echo.

REM Verificar Node.js
where node >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] Node.js nao encontrado!
    echo Por favor, instale Node.js 18+ de https://nodejs.org/
    pause
    exit /b 1
)

echo [OK] Node.js encontrado: 
node --version

REM Verificar .NET
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo [ERRO] .NET SDK nao encontrado!
    echo Por favor, instale .NET 8 SDK de https://dotnet.microsoft.com/download
    pause
    exit /b 1
)

echo [OK] .NET SDK encontrado: 
dotnet --version

REM Verificar Angular CLI
where ng >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [AVISO] Angular CLI nao encontrado!
    echo Deseja instalar o Angular CLI globalmente? (S/N)
    set /p INSTALL_NG=
    if /i "!INSTALL_NG!"=="S" (
        echo Instalando Angular CLI...
        npm install -g @angular/cli
    ) else (
        echo Por favor, instale manualmente: npm install -g @angular/cli
        pause
        exit /b 1
    )
)

echo [OK] Angular CLI encontrado:
ng version --minimal

echo.
echo ========================================
echo  Instalando Dependencias do Frontend
echo ========================================
echo.

cd src\InsightEngine.Web

if not exist "node_modules" (
    echo Instalando dependencias npm...
    call npm install
    if %ERRORLEVEL% NEQ 0 (
        echo [ERRO] Falha ao instalar dependencias npm
        pause
        exit /b 1
    )
    echo [OK] Dependencias instaladas com sucesso!
) else (
    echo [INFO] node_modules ja existe. Pulando instalacao.
    echo Para reinstalar, delete a pasta node_modules e execute novamente.
)

cd ..\..

echo.
echo ========================================
echo  Setup Concluido!
echo ========================================
echo.
echo Proximos passos:
echo.
echo 1. Abra um terminal e execute:
echo    cd src\InsightEngine.API
echo    dotnet run
echo.
echo 2. Abra OUTRO terminal e execute:
echo    cd src\InsightEngine.Web
echo    npm start
echo.
echo 3. Acesse no navegador:
echo    http://localhost:4200
echo.
echo Ou use os scripts prontos:
echo    - start-backend.bat
echo    - start-frontend.bat
echo.
pause
