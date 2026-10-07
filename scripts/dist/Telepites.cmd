@echo off
rem Planner Widget telepitese (vagy frissitese) a csomagbol: %LOCALAPPDATA%\Programs\PlannerWidget
rem Rendszergazdai jog nem kell. A beallitasok es a bejelentkezes frissiteskor megmaradnak.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0telepito.ps1"
pause
