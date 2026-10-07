<#
.SYNOPSIS
    A Planner Widget telepítése a kiosztott ZIP-csomagból (fordítás és .NET nélkül).
.DESCRIPTION
    A csomag „app” mappáját ide másolja: %LOCALAPPDATA%\Programs\PlannerWidget (rendszergazdai jog nem kell),
    feloldja a letöltött fájlok blokkolását, Start menü és asztali parancsikont készít, majd elindítja.
    Ha a widget már fut, előbb bezárja; a beállítások és a bejelentkezés megmaradnak (frissítéskor is).
.PARAMETER InstallDir
    Más célmappa (teszteléshez).
.PARAMETER NoShortcuts
    Ne készüljön parancsikon (teszteléshez).
.PARAMETER NoStart
    Telepítés után ne induljon el (teszteléshez).
.PARAMETER Autostart
    Induljon a Windows-zal (a widget Beállításaiban is kapcsolható).
#>
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\PlannerWidget'),
    [switch]$NoShortcuts,
    [switch]$NoStart,
    [switch]$Autostart
)

$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'app'
$exeName = 'PlannerWidget.exe'

try {
    if (-not (Test-Path (Join-Path $source $exeName))) {
        throw "Nem található: $source\$exeName. Előbb csomagold ki a teljes ZIP-et egy mappába, és onnan indítsd a Telepites.cmd-t."
    }

    Write-Host '1/4  Futó Planner Widget bezárása...' -ForegroundColor Cyan
    Get-Process PlannerWidget -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$InstallDir*" } | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(3000)) { $_ | Stop-Process -Force }
    }

    Write-Host "2/4  Másolás ide: $InstallDir" -ForegroundColor Cyan
    if (Test-Path $InstallDir) { Remove-Item $InstallDir -Recurse -Force }
    New-Item -ItemType Directory -Force $InstallDir | Out-Null
    Copy-Item (Join-Path $source '*') $InstallDir -Recurse -Force

    # Az internetről / Teamsből letöltött fájlokon „blokkolt” jelölés van – enélkül a Windows minden indításkor rákérdezne.
    Get-ChildItem $InstallDir -Recurse -File | Unblock-File
    $exe = Join-Path $InstallDir $exeName

    Write-Host '3/4  Parancsikonok...' -ForegroundColor Cyan
    if (-not $NoShortcuts) {
        $shell = New-Object -ComObject WScript.Shell
        foreach ($folder in @([Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop'))) {
            $lnk = $shell.CreateShortcut((Join-Path $folder 'Planner Widget.lnk'))
            $lnk.TargetPath = $exe
            $lnk.WorkingDirectory = $InstallDir
            $lnk.IconLocation = "$exe,0"
            $lnk.Description = 'Planner Widget – Microsoft Planner feladatok az asztalon'
            $lnk.Save()
        }
    }

    if ($Autostart) {
        Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'PlannerWidget' -Value "`"$exe`" --autostart"
    }

    Write-Host '4/4  Indítás...' -ForegroundColor Cyan
    if (-not $NoStart) { Start-Process $exe }

    Write-Host ''
    Write-Host 'Kész! A Planner Widget a képernyő jobb felső sarkában jelenik meg.' -ForegroundColor Green
    Write-Host 'Később a Start menüből vagy az asztali „Planner Widget” ikonnal indíthatod.' -ForegroundColor Green
}
catch {
    Write-Host ''
    Write-Host "Hiba a telepítés közben: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Készíts erről képernyőképet, és küldd el annak, akitől a programot kaptad.' -ForegroundColor Red
    exit 1
}
