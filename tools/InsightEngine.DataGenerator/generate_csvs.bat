@echo off
echo ==================================================
echo InsightEngine Data Generator
echo ==================================================
echo.
echo Gerando arquivos CSV com dados de teste...
echo.
echo Destino: ..\..\samples\
echo.
py generate_all.py
echo.
echo ==================================================
echo Geração concluída!
echo Verifique os arquivos em samples/
echo ==================================================
pause