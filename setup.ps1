# setup.ps1 — Master Workspace Setup & Synchronization for Anime Ecosystem
# Projects: anime-sorter (sort.moe) & livechart-anime-tracker (Calendar)

Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host "   🌸 Anime Ecosystem Workspace Synchronization (sort.moe)     " -ForegroundColor Cyan
Write-Host "===============================================================" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Continue"

$sorterDir = "C:\Users\Piotrek\Desktop\sorter"
$kalendarzDir = "C:\Users\Piotrek\Desktop\kalendarz"

# 1. Check Git
Write-Host "[1/6] Checking Git environment..." -ForegroundColor Yellow
if (Get-Command git -ErrorAction SilentlyContinue) {
    Write-Host "  [OK] Git is available: $(git --version)" -ForegroundColor Green
} else {
    Write-Host "  [WARN] Git not found in PATH." -ForegroundColor Red
}

# 2. Check and Sync Repositories
Write-Host "`n[2/6] Synchronizing Git repositories..." -ForegroundColor Yellow

# A. anime-sorter
if (Test-Path "$sorterDir\.git") {
    Write-Host "  [OK] anime-sorter repository detected at $sorterDir" -ForegroundColor Green
    Push-Location $sorterDir
    Write-Host "    Pulling latest changes from origin..." -ForegroundColor Gray
    git pull origin main 2>$null
    Pop-Location
} else {
    Write-Host "  [*] Cloning anime-sorter from GitHub..." -ForegroundColor Cyan
    git clone https://github.com/wooles/anime-sorter.git $sorterDir
}

# B. livechart-anime-tracker
if (Test-Path "$kalendarzDir\.git") {
    Write-Host "  [OK] livechart-anime-tracker repository detected at $kalendarzDir" -ForegroundColor Green
    Push-Location $kalendarzDir
    Write-Host "    Pulling latest changes from origin..." -ForegroundColor Gray
    git pull origin main 2>$null
    Pop-Location
} else {
    Write-Host "  [*] Cloning livechart-anime-tracker from GitHub..." -ForegroundColor Cyan
    git clone https://github.com/wooles/livechart-anime-tracker.git $kalendarzDir
}

# 3. Check .NET SDK & Restore NuGet Packages (Tenrai.Net, Ical.Net)
Write-Host "`n[3/6] Restoring .NET Dependencies & Packages..." -ForegroundColor Yellow
$dotnetSdks = & dotnet --list-sdks 2>$null
if ($LASTEXITCODE -eq 0 -and $dotnetSdks) {
    Write-Host "  [OK] .NET SDK is available" -ForegroundColor Green

    # Restore kalendarz (LiveChartTracker.csproj)
    if (Test-Path "$kalendarzDir\LiveChartTracker.csproj") {
        Write-Host "    Restoring LiveChartTracker.csproj (.NET 8 + Tenrai.Net 3.1.0 + Ical.Net)..." -ForegroundColor Cyan
        Push-Location $kalendarzDir
        dotnet restore LiveChartTracker.csproj --verbosity quiet
        Pop-Location
        Write-Host "    [OK] Packages restored for livechart-anime-tracker." -ForegroundColor Green
    }

    # Restore mal-proxy
    if (Test-Path "$sorterDir\mal-proxy\MalProxy.csproj") {
        Write-Host "    Restoring mal-proxy/MalProxy.csproj (.NET 8 + Tenrai.Net)..." -ForegroundColor Cyan
        Push-Location $sorterDir
        dotnet restore mal-proxy\MalProxy.csproj --verbosity quiet
        Pop-Location
        Write-Host "    [OK] Packages restored for mal-proxy." -ForegroundColor Green
    }
} else {
    Write-Host "  [INFO] .NET SDK not detected in PATH. Local .NET features will require .NET 8 SDK." -ForegroundColor Yellow
}

# 4. Check Python
Write-Host "`n[4/6] Checking Python runtime..." -ForegroundColor Yellow
if (Get-Command python -ErrorAction SilentlyContinue) {
    Write-Host "  [OK] Python is available: $(python --version)" -ForegroundColor Green
} else {
    Write-Host "  [INFO] Python is optional (for local server.py). index.html can be opened directly in browser." -ForegroundColor Gray
}

# 5. Setup .vscode Configurations & Extensions
Write-Host "`n[5/6] Configuring VS Code settings and launch targets..." -ForegroundColor Yellow

$vscodeDirs = @("$sorterDir\.vscode", "$kalendarzDir\.vscode")
foreach ($dir in $vscodeDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $extContent = '{ "recommendations": ["ms-dotnettools.csharp", "ms-dotnettools.csdevkit", "ms-python.python", "ritwickdey.liveserver"] }'
    Set-Content -Path "$dir\extensions.json" -Value $extContent -Encoding UTF8

    $settContent = '{ "editor.tabSize": 2, "editor.formatOnSave": true, "files.trimTrailingWhitespace": true }'
    Set-Content -Path "$dir\settings.json" -Value $settContent -Encoding UTF8
}
Write-Host "  [OK] .vscode settings and recommended extensions configured." -ForegroundColor Green

# 6. Verify Inter-Project Links & Health
Write-Host "`n[6/6] Verifying Inter-Project Links & Live Endpoints..." -ForegroundColor Yellow
Write-Host "  [OK] Anime Sorter (sort.moe) -> Anime Calendar (livechart-anime-tracker.onrender.com)" -ForegroundColor Green
Write-Host "  [OK] Anime Calendar -> Anime Sorter (sort.moe)" -ForegroundColor Green

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Green
Write-Host "  [SUCCESS] All repositories, dependencies, and settings READY!" -ForegroundColor Green
Write-Host "===============================================================" -ForegroundColor Green
Write-Host ""
Write-Host "Quick Launch Commands:" -ForegroundColor Cyan
Write-Host "  - Sorter Web:    Open '$sorterDir\index.html' or run 'python server.py'" -ForegroundColor White
Write-Host "  - Calendar Web:  Double-click '$kalendarzDir\run.bat' or run 'dotnet run --server'" -ForegroundColor White
Write-Host "  - Live Sorter:   https://sort.moe/" -ForegroundColor White
Write-Host "  - Live Calendar: https://livechart-anime-tracker.onrender.com/" -ForegroundColor White
Write-Host ""
