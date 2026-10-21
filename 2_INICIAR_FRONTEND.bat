@echo off
chcp 65001 >nul
title SciKids LMS - Servidor Frontend Angular 18 (Puerto 4200)
color 0A

echo ============================================================================
echo   SCIKIDS LMS COLOMBIA - INICIANDO FRONTEND ANGULAR 18 SPA
echo   Accesibilidad: NTC 5854 / WCAG 2.1 Nivel AA (Educacion Primaria 6-11 anos)
echo ============================================================================
echo.

cd /d "%~dp0frontend"
if %ERRORLEVEL% NEQ 0 (
    color 0C
    echo [ERROR] No se encontro el directorio frontend.
    pause
    exit /b 1
)

if not exist "node_modules\" (
    echo [1/2] Instalando dependencias de Node.js por primera vez (npm install)...
    call npm install
    if %ERRORLEVEL% NEQ 0 (
        color 0C
        echo [ERROR] Fallo la instalacion de paquetes npm. Verifique que Node.js 18+ este instalado.
        pause
        exit /b 1
    )
) else (
    echo [1/2] Dependencias node_modules detectadas correctamente.
)

echo.
echo [2/2] Iniciando servidor de desarrollo Angular en http://localhost:4200 ...
echo.
call npm start

if %ERRORLEVEL% NEQ 0 (
    color 0C
    echo.
    echo [ERROR] El servidor Angular se detuvo con errores.
    pause
    exit /b 1
)

pause
