@echo off
chcp 65001 >nul
setlocal

echo ============================================
echo   MonoLauncher - upload to GitHub
echo ============================================
echo.
echo 1) Make sure the repository already exists on GitHub.
echo 2) Enter your GitHub username below.
echo 3) A login window will pop up - sign in once.
echo.

set /p GHUSER=GitHub username: 
if "%GHUSER%"=="" (
    echo No username entered, aborting.
    pause
    exit /b 1
)

set /p REPONAME=Repository name [MonoLauncher]: 
if "%REPONAME%"=="" set REPONAME=MonoLauncher

cd /d "%~dp0.."
echo.
echo Working directory: %CD%
echo Remote: https://github.com/%GHUSER%/%REPONAME%.git
echo.

git remote remove origin >nul 2>&1
git remote add origin https://github.com/%GHUSER%/%REPONAME%.git
git branch -M main

echo Pushing...
echo.
git push -u origin main

if errorlevel 1 (
    echo.
    echo --------------------------------------------------
    echo Push failed. Common causes:
    echo  - Wrong username or repository name
    echo  - The repository already has a README/license
    echo    (run: git pull --rebase origin main, then push again)
    echo  - Authentication: use a Personal Access Token as
    echo    the password when the login window asks for one
    echo --------------------------------------------------
) else (
    echo.
    echo Done: https://github.com/%GHUSER%/%REPONAME%
)

echo.
pause
