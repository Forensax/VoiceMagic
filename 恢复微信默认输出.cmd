@echo off
cd /d "%~dp0"
if not exist "dist\VoiceMagic.exe" exit /b 1
start /wait "" "dist\VoiceMagic.exe" --restore
