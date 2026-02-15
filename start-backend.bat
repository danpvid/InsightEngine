@echo off
echo.
echo ========================================
echo  InsightEngine - Iniciando Backend API
echo ========================================
echo.

cd src\InsightEngine.API

echo Iniciando API em https://localhost:5001
echo.
echo Aguarde a mensagem: "Now listening on: https://localhost:5001"
echo.
echo Pressione Ctrl+C para parar o servidor
echo.

dotnet run

pause
