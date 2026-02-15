@echo off
echo.
echo ========================================
echo  InsightEngine - Iniciando Frontend
echo ========================================
echo.

cd src\InsightEngine.Web

if not exist "node_modules" (
    echo [ERRO] Dependencias nao instaladas!
    echo Execute setup.bat primeiro.
    pause
    exit /b 1
)

echo Iniciando Angular em http://localhost:4200
echo.
echo Aguarde a mensagem: "compiled successfully"
echo.
echo O navegador abrira automaticamente
echo Pressione Ctrl+C para parar o servidor
echo.

call npm start

pause
