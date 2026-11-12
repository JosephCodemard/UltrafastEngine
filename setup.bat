@echo off
rem Build UltrafastEngine from source for this machine. Options: setup.bat --help ; guide: docs\install.md
rem   setup.bat --cli --python --plugins fitting hitran --check
setlocal
set "PY="
where py >nul 2>nul && set "PY=py -3"
if not defined PY where python >nul 2>nul && set "PY=python"
if not defined PY (
  echo setup.bat needs Python 3.10+ ^(https://www.python.org^) and the .NET 10 SDK
  exit /b 1
)
%PY% "%~dp0build\setup.py" %*
