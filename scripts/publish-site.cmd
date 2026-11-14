@echo off
chcp 65001 >nul
title MonoLauncher Site Publisher
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-site.ps1" %*
if errorlevel 1 (
    echo.
    echo Script exited with an error.
    pause
)
