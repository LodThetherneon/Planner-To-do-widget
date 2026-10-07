<#
.SYNOPSIS
    Eltávolítja a telepített Planner Widgetet (programfájlok, parancsikonok, automatikus indítás).
.PARAMETER RemoveData
    A beállításokat, a bejelentkezési gyorsítótárat és a naplókat is törli
    (%LOCALAPPDATA%\PlannerWidget). Enélkül egy újratelepítés mindent megtart.
#>
param([switch]$RemoveData)

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PlannerWidget'

Get-Process PlannerWidget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

foreach ($lnk in @(
        (Join-Path ([Environment]::GetFolderPath('Programs')) 'Planner Widget.lnk'),
        (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Planner Widget.lnk'))) {
    if (Test-Path $lnk) { Remove-Item $lnk -Force; Write-Host "Törölve: $lnk" }
}

$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $run -Name 'PlannerWidget' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $run -Name 'PlannerWidget'
    Write-Host 'Automatikus indítás kikapcsolva.'
}

if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force; Write-Host "Törölve: $installDir" }

if ($RemoveData) {
    $data = Join-Path $env:LOCALAPPDATA 'PlannerWidget'
    if (Test-Path $data) { Remove-Item $data -Recurse -Force; Write-Host "Adatok törölve: $data" }
}

Write-Host 'Eltávolítás kész.' -ForegroundColor Green
