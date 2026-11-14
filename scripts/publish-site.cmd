@echo off
chcp 65001 >nul
title MonoLauncher Site Publisher
rem WinForms needs a single-threaded apartment; the default console host is MTA.
powershell -STA -NoProfile -ExecutionPolicy Bypass -File "%~dp0publish-site.ps1" %*
if errorlevel 1 (
    echo.
    echo Script exited with an error.
    pause
)
