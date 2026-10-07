<#
.SYNOPSIS
    Lefordítja és elindítja a Planner Widgetet (fejlesztői, Debug változat).
.EXAMPLE
    .\scripts\run.ps1           # normál indítás (valódi Planner-adatok, bejelentkezés)
    .\scripts\run.ps1 -Demo     # bemutató mód: kitalált adatok, semmi nem módosul
    .\scripts\run.ps1 -Demo -StaleHeartbeat   # bemutató, leállt szinkron-életjellel (riasztás 6:20–21:00 között)
#>
param(
    [switch]$Demo,
    [switch]$StaleHeartbeat,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\PlannerWidget.App\PlannerWidget.App.csproj'

Write-Host 'Fordítás...' -ForegroundColor Cyan
dotnet build $project -c $Configuration -p:Platform=x64 -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'A fordítás nem sikerült.' }

$exe = Join-Path $env:LOCALAPPDATA "PlannerWidget.Build\bin\PlannerWidget.App\$($Configuration.ToLower())_win-x64\PlannerWidget.exe"
if (-not (Test-Path $exe)) { throw "Nem található: $exe" }

$arguments = @()
if ($Demo) { $arguments += '--demo' }
if ($Demo -and $StaleHeartbeat) { $arguments += '--demo-stale-heartbeat' }

Write-Host "Indítás: $exe $arguments" -ForegroundColor Green
if ($arguments.Count -gt 0) { Start-Process $exe -ArgumentList $arguments } else { Start-Process $exe }
