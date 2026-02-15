@echo off
echo.
echo ========================================
echo  InsightEngine - Demo Completa
echo ========================================
echo.

REM Verificar se as dependencias foram instaladas
if not exist "src\InsightEngine.Web\node_modules" (
    echo [ERRO] Dependencias do frontend nao instaladas!
    echo.
    echo Execute primeiro: setup.bat
    echo.
    pause
    exit /b 1
)

echo Este script ira:
echo 1. Iniciar o Backend API (porta 5001)
echo 2. Iniciar o Frontend Angular (porta 4200)
echo 3. Abrir o navegador automaticamente
echo.
echo IMPORTANTE: Vai abrir 2 janelas de terminal
echo.
pause

echo.
echo [1/2] Iniciando Backend API...
start "InsightEngine Backend" cmd /k "cd src\InsightEngine.API && dotnet run"

echo Aguardando 5 segundos para o backend inicializar...
timeout /t 5 /nobreak > nul

echo.
echo [2/2] Iniciando Frontend Angular...
start "InsightEngine Frontend" cmd /k "cd src\InsightEngine.Web && npm start"

echo.
echo ========================================
echo  Servidores Iniciados!
echo ========================================
echo.
echo Backend:  https://localhost:5001
echo Frontend: http://localhost:4200
echo.
echo O navegador abrira automaticamente em alguns segundos.
echo.
echo Para parar os servidores:
echo - Feche as janelas de terminal abertas
echo - Ou pressione Ctrl+C em cada uma
echo.
pause
