<#
.SYNOPSIS
    Starts the Test Media Server, the API and (when present) the web dashboard, each in its own PowerShell window.

.DESCRIPTION
    Prerequisite: the database connection string is configured (see README, Setup).
    The API applies EF Core migrations on start-up (Database:ApplyMigrationsOnStartup=true).

    Then open http://localhost:5173

    If script execution is disabled on this machine, run it as:
        powershell -ExecutionPolicy Bypass -File .\scripts\start-all.ps1
#>
# The dashboard is a separate repository. It is started too when it is cloned next to this one.
$api = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$web = 'streaming-web', 'streaming-app' |
    ForEach-Object { Join-Path (Split-Path -Parent $api) $_ } |
    Where-Object { Test-Path (Join-Path $_ 'package.json') } |
    Select-Object -First 1

if (-not $web) {
    Write-Warning 'Dashboard folder not found next to this repository. Starting the backend only.'
}
elseif (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Installing web dependencies (first run)...' -ForegroundColor Cyan
    Push-Location $web; npm.cmd ci; Pop-Location
}

$commands = @(
    @{ title = 'Test Media Server :5200'; cmd = "dotnet run --project `"$api\StreamingAutomation.MediaServer`" --launch-profile http" },
    @{ title = 'API :5126';               cmd = "dotnet run --project `"$api\StreamingAutomation.API`" --launch-profile http" }
)
if ($web) {
    # npm.cmd (not npm): the npm.ps1 shim is blocked when PowerShell script execution is disabled.
    $commands += @{ title = 'Web :5173'; cmd = "npm.cmd --prefix `"$web`" run dev" }
}

foreach ($c in $commands) {
    Start-Process powershell -ArgumentList '-NoExit', '-Command', "`$Host.UI.RawUI.WindowTitle = '$($c.title)'; $($c.cmd)"
    Start-Sleep -Seconds 2
}

Write-Host ''
Write-Host 'Starting... give it ~20 seconds, then open:' -ForegroundColor Green
if ($web) { Write-Host '  Dashboard      http://localhost:5173' }
Write-Host '  API docs       http://localhost:5126/scalar'
Write-Host '  Health         http://localhost:5126/health   http://localhost:5200/health'
