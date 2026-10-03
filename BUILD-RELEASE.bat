@echo off
setlocal
cd /d "%~dp0"
set "PYTHONUTF8=1"
set "PYTHONIOENCODING=utf-8"

echo ============================================================
echo AXIOOS DOWNLOAD MANAGER RELEASE BUILD
echo ============================================================

where py >nul 2>nul
if errorlevel 1 goto use_python
py -3 --version >nul 2>nul
if errorlevel 1 goto use_python
py -3 tools\build_release.py
goto done

:use_python
where python >nul 2>nul
if errorlevel 1 (
  echo ERROR: Python is required to build the release.
  exit /b 3
)
python tools\build_release.py

:done
exit /b %errorlevel%
