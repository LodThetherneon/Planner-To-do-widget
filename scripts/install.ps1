<#
.SYNOPSIS
    Kiadási (Release) változat készítése és telepítése a felhasználó saját mappájába.
.DESCRIPTION
    - Lefordítja Release módban (ReadyToRun – gyorsabb indulás).
    - Telepíti ide: %LOCALAPPDATA%\Programs\PlannerWidget  (rendszergazdai jog nem kell)
    - Start menü parancsikont készít (opcionálisan asztalit is).
    - Ha a widget fut, előbb bezárja, a beállítások és a bejelentkezés megmaradnak.
.PARAMETER Desktop
    Asztali parancsikon is készüljön.
.PARAMETER SelfContained
    A .NET futtatókörnyezetet is becsomagolja (más gépre másoláshoz; nagyobb méret).
.PARAMETER Autostart
    Induljon a Windows-zal (ez a Beállításokban is kapcsolható).
.EXAMPLE
    .\scripts\install.ps1 -Desktop
#>
param(
    [switch]$Desktop,
    [switch]$SelfContained,
    [switch]$Autostart
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\PlannerWidget.App\PlannerWidget.App.csproj'
$publishDir = Join-Path $env:LOCALAPPDATA 'PlannerWidget.Build\publish'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\PlannerWidget'

Write-Host '1/4  Kiadási változat készítése...' -ForegroundColor Cyan
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
$sc = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained $sc -o $publishDir -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'A publikálás nem sikerült.' }

Write-Host '2/4  Futó példány bezárása...' -ForegroundColor Cyan
Get-Process PlannerWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$installDir*" } | ForEach-Object {
    $_.CloseMainWindow() | Out-Null
    if (-not $_.WaitForExit(3000)) { $_ | Stop-Process -Force }
}

Write-Host "3/4  Telepítés: $installDir" -ForegroundColor Cyan
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $publishDir '*') $installDir -Recurse -Force
$exe = Join-Path $installDir 'PlannerWidget.exe'

Write-Host '4/4  Parancsikonok...' -ForegroundColor Cyan
$shell = New-Object -ComObject WScript.Shell
function New-Shortcut([string]$path) {
    $lnk = $shell.CreateShortcut($path)
    $lnk.TargetPath = $exe
    $lnk.WorkingDirectory = $installDir
    $lnk.IconLocation = "$exe,0"
    $lnk.Description = 'Planner Widget – Microsoft Planner feladatok az asztalon'
    $lnk.Save()
}
$programs = [Environment]::GetFolderPath('Programs')
New-Shortcut (Join-Path $programs 'Planner Widget.lnk')
if ($Desktop) { New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Planner Widget.lnk') }
if ($Autostart) {
    Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PlannerWidget' -Value "`"$exe`" --autostart"
}

$size = [math]::Round(((Get-ChildItem $installDir -Recurse | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host ''
Write-Host "Kész! ($size MB)  Indítás: Start menü -> Planner Widget" -ForegroundColor Green
Start-Process $exe
