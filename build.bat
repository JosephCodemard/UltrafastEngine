@echo off
rem Compile and package UltrafastEngine release folders. Options: build.bat --help ; guide: docs\releasing.md
rem   build.bat --cli --python --cpp --csharp --plugins fitting zemax --platform win64 x86 --zos-api-dir "C:\Program Files\Ansys Zemax OpticStudio 2025 R1"
setlocal
set "PY="
where py >nul 2>nul && set "PY=py -3"
if not defined PY where python >nul 2>nul && set "PY=python"
if not defined PY (
  echo build.bat needs Python 3.9+ ^(https://www.python.org^) and the .NET 10 SDK
  exit /b 1
)
%PY% "%~dp0build\package.py" %*
exit /b %ERRORLEVEL%
