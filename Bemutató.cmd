@echo off
rem Planner Widget - bemutato mod (kitalalt adatok, semmi nem modosul)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1" -Demo
if errorlevel 1 pause
