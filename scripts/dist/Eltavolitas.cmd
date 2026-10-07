@echo off
rem Planner Widget eltavolitasa (programfajlok, parancsikonok, automatikus inditas).
rem A beallitasok megmaradnak; ha azokat is torolni szeretned: eltavolito.ps1 -RemoveData
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0eltavolito.ps1"
pause
