@echo off
rem Daedalus 构建入口（build.ps1 的 cmd 包装）。
rem 用法：build.bat [Debug^|Release] [-Clean]
setlocal
set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"
if /i not "%CONFIG%"=="Debug" if /i not "%CONFIG%"=="Release" (
    echo 用法: build.bat [Debug^|Release] [-Clean]
    exit /b 1
)
set "EXTRA="
if /i "%~2"=="-Clean" set "EXTRA=-Clean"
where pwsh >nul 2>nul
if %errorlevel%==0 (
    pwsh -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration %CONFIG% %EXTRA%
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration %CONFIG% %EXTRA%
)
exit /b %errorlevel%
