@echo off
setlocal
set "DOTNET_ROOT=%~dp0runtime"
set "DOTNET_MULTILEVEL_LOOKUP=0"

if not exist "%DOTNET_ROOT%\dotnet.exe" (
    echo Runtime local nao encontrado.
    echo Extraia o ZIP inteiro antes de executar este arquivo.
    pause
    exit /b 1
)

start "" "%DOTNET_ROOT%\dotnet.exe" "%~dp0TdeMauiBanco.dll"
