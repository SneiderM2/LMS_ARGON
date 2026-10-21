@echo off
chcp 65001 >nul
title SciKids LMS - Servidor Frontend Angular 18 - Puerto 4200
color 0A

echo ============================================================================
echo   SCIKIDS LMS COLOMBIA - INICIANDO FRONTEND ANGULAR 18 SPA
echo   Accesibilidad: NTC 5854 / WCAG 2.1 Nivel AA - Educacion Primaria
echo ============================================================================
echo.

cd /d "%~dp0frontend"
if errorlevel 1 goto :ERROR_DIR

if exist "node_modules\@angular-devkit\build-angular\package.json" goto :SKIP_INSTALL

echo [1/2] Instalando dependencias de Node.js por primera vez con npm install...
call npm install --legacy-peer-deps
if errorlevel 1 goto :ERROR_NPM
goto :RUN_SERVER

:SKIP_INSTALL
echo [1/2] Dependencias node_modules completas y verificadas.

:RUN_SERVER
echo.
echo [2/2] Iniciando servidor Angular en http://localhost:4200 ...
echo.
call npx ng serve --proxy-config proxy.conf.json --open
if errorlevel 1 goto :ERROR_SERVE
goto :END

:ERROR_DIR
color 0C
echo [ERROR] No se encontro la carpeta frontend.
pause
exit /b 1

:ERROR_NPM
color 0C
echo [ERROR] Fallo la instalacion de paquetes npm.
pause
exit /b 1

:ERROR_SERVE
color 0C
echo.
echo [ERROR] El servidor Angular se detuvo con errores.
pause
exit /b 1

:END
pause
