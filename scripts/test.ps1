<#
.SYNOPSIS
    Lefuttatja az automatikus teszteket.
.PARAMETER Pdf
    Opcionális: egy valódi jelenléti ív PDF. Ha megadod, a PDF-kitöltést is teszteli
    egy ideiglenes MÁSOLATON (az eredeti fájl nem változik).
.EXAMPLE
    .\scripts\test.ps1
    .\scripts\test.ps1 -Pdf "\\fs2.eik.sze.hu\...\Jelenléti_március.pdf"
#>
param([string]$Pdf)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$root = Split-Path $PSScriptRoot -Parent

if ($Pdf) {
    if (-not (Test-Path -LiteralPath $Pdf)) { throw "A PDF nem található: $Pdf" }
    $env:PLANNERWIDGET_TEST_PDF = (Resolve-Path -LiteralPath $Pdf).Path
    Write-Host "PDF-integrációs teszt is fut (másolaton): $Pdf" -ForegroundColor Cyan
}
else {
    Remove-Item Env:\PLANNERWIDGET_TEST_PDF -ErrorAction SilentlyContinue
}

dotnet test (Join-Path $root 'tests\PlannerWidget.Core.Tests\PlannerWidget.Core.Tests.csproj') -nologo
exit $LASTEXITCODE
