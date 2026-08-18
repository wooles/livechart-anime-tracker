$env:DOTNET_ROOT = "$HOME\.dotnet"
$env:PATH = "$HOME\.dotnet;" + $env:PATH

Write-Host "====================================================================" -ForegroundColor Cyan
Write-Host "  LiveChart Anime Tracker (.NET 8 + Tenrai.Net)" -ForegroundColor Green
Write-Host "====================================================================" -ForegroundColor Cyan

Start-Process "http://localhost:5000"
dotnet run --server
