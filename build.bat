@echo off
setlocal

echo ========================================================
echo Building Stash Windows EXE...
echo ========================================================

set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [ERROR] .NET Framework C# compiler not found at %CSC%
    exit /b 1
)

if not exist "%~dp0bin" mkdir "%~dp0bin"

"%CSC%" /target:winexe ^
        /out:"%~dp0Stash.exe" ^
        /win32icon:"%~dp0src\app.ico" ^
        /win32manifest:"%~dp0src\app.manifest" ^
        /optimize+ ^
        /r:System.dll ^
        /r:System.Core.dll ^
        /r:System.Drawing.dll ^
        /r:System.Windows.Forms.dll ^
        "%~dp0src\Program.cs"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Compilation failed!
    exit /b %ERRORLEVEL%
)

if exist "%~dp0dist" (
    copy /Y "%~dp0Stash.exe" "%~dp0dist\Stash.exe" >nul
)

echo [SUCCESS] Built standalone executable: %~dp0Stash.exe
exit /b 0
