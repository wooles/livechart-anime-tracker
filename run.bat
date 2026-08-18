@echo off
setlocal
echo ====================================================================
echo   LiveChart Anime Tracker (.NET 8 + Tenrai.Net)
echo ====================================================================
echo.

set "DOTNET_ROOT=%USERPROFILE%\.dotnet"
set "PATH=%USERPROFILE%\.dotnet;%PATH%"

where dotnet >nul 2>nul
if %errorlevel% neq 0 (
    echo [Blad] Nie znaleziono .NET SDK w systemie.
    pause
    exit /b 1
)

echo Uruchamianie aplikacji na http://localhost:5000 ...
start "" http://localhost:5000
dotnet run --server
pause
