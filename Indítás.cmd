@echo off
rem Planner Widget - forditas es inditas (valodi adatokkal, bejelentkezessel)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run.ps1"
if errorlevel 1 pause
