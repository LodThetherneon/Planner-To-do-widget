@echo off
rem Planner Widget - vegleges telepites (%LOCALAPPDATA%\Programs\PlannerWidget) + Start menu es asztali parancsikon
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\install.ps1" -Desktop
pause
