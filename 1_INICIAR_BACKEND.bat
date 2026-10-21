@echo off
chcp 65001 >nul
title SciKids LMS - Servidor Backend ASP.NET Core - Puerto 5000
color 0B

echo ============================================================================
echo   SCIKIDS LMS COLOMBIA - INICIANDO BACKEND ASP.NET CORE WEB API
echo   Marco Legal: Ley 1581 de 2012, Decreto 1377 de 2013 y Ley 1098 de 2006
echo ============================================================================
echo.

cd /d "%~dp0backend\LMS.API"
if errorlevel 1 goto :ERROR_DIR

echo [1/2] Restaurando paquetes NuGet con dotnet restore...
dotnet restore
if errorlevel 1 goto :ERROR_RESTORE

echo.
echo [2/2] Compilando e iniciando servidor en http://localhost:5000 ...
echo       Swagger UI disponible en: http://localhost:5000/swagger
echo.
dotnet run --urls "http://localhost:5000"
if errorlevel 1 goto :ERROR_RUN
goto :END

:ERROR_DIR
color 0C
echo [ERROR] No se encontro el directorio backend\LMS.API.
pause
exit /b 1

:ERROR_RESTORE
color 0C
echo [ERROR] Fallo la restauracion de paquetes NuGet.
pause
exit /b 1

:ERROR_RUN
color 0C
echo.
echo [ERROR] El servidor Backend se detuvo con errores.
pause
exit /b 1

:END
pause
