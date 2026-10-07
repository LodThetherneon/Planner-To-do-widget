<#
.SYNOPSIS
    Kiosztható ZIP-csomag készítése kollégáknak (önálló program + egyszerű telepítő, fordítás nélkül).
.DESCRIPTION
    - Release módban, ÖNÁLLÓAN (a .NET és a Windows App SDK is benne van) publikál win-x64-re.
    - Összeállítja a csomagot: app\ (a program), Telepites.cmd, Eltavolitas.cmd, a telepítési útmutató és a képei.
    - Kimenet: %LOCALAPPDATA%\PlannerWidget.Build\dist\PlannerWidget-<verzió>-win-x64.zip (nem a OneDrive-ra).
    A címzetteknek nem kell .NET, Visual Studio vagy rendszergazdai jog. Lásd: docs\TELEPITES_KOLLEGAKNAK.md
.PARAMETER OutDir
    Más kimeneti mappa a ZIP-nek.
.EXAMPLE
    .\scripts\package.ps1
#>
param([string]$OutDir = (Join-Path $env:LOCALAPPDATA 'PlannerWidget.Build\dist'))

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src\PlannerWidget.App\PlannerWidget.App.csproj'
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { $version = '0.0.0' }
$name = "PlannerWidget-$version-win-x64"
$staging = Join-Path $env:LOCALAPPDATA "PlannerWidget.Build\package\$name"

Write-Host "1/3  Önálló kiadási változat ($version)..." -ForegroundColor Cyan
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
dotnet publish $project -c Release -r win-x64 -p:Platform=x64 --self-contained true -o (Join-Path $staging 'app') -nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'A publikálás nem sikerült.' }
# Hibakereső szimbólumok nem kellenek a kollégáknak.
Get-ChildItem (Join-Path $staging 'app') -Recurse -Filter *.pdb | Remove-Item -Force

Write-Host '2/3  Telepítő és útmutató...' -ForegroundColor Cyan
$dist = Join-Path $PSScriptRoot 'dist'
Copy-Item (Join-Path $dist 'telepito.ps1'), (Join-Path $dist 'Telepites.cmd'), (Join-Path $dist 'Eltavolitas.cmd') $staging
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1') (Join-Path $staging 'eltavolito.ps1')
$guide = Join-Path $root 'docs\TELEPITES_KOLLEGAKNAK.md'
Copy-Item $guide (Join-Path $staging 'TELEPITES_KOLLEGAKNAK.md')
# Az útmutatóban hivatkozott képek (kepek/...) a ZIP-ben is a helyükön legyenek.
New-Item -ItemType Directory -Force (Join-Path $staging 'kepek') | Out-Null
foreach ($image in [regex]::Matches((Get-Content $guide -Raw -Encoding UTF8), 'kepek/([\w\-]+\.png)') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique) {
    Copy-Item (Join-Path $root "docs\kepek\$image") (Join-Path $staging "kepek\$image")
}

Write-Host '3/3  ZIP...' -ForegroundColor Cyan
New-Item -ItemType Directory -Force $OutDir | Out-Null
$zip = Join-Path $OutDir "$name.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
# Belső mappa nélkül: az Intéző „Összes kibontása” úgyis a ZIP nevével hoz létre mappát, a dupla szint pedig
# a 260 karakteres útvonalkorlátot közelítené (néhány belső fájlnév eleve hosszú).
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try { $maxEntry = ($archive.Entries | ForEach-Object { $_.FullName.Length } | Measure-Object -Maximum).Maximum } finally { $archive.Dispose() }

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host ''
Write-Host "Kész: $zip ($size MB; leghosszabb belső útvonal: $maxEntry karakter)" -ForegroundColor Green
Write-Host 'Ezt a fájlt oszd meg (pl. Teams / OneDrive), mellé a docs\TELEPITES_KOLLEGAKNAK.md útmutatót.' -ForegroundColor Green
