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

"%CSC%" /target:winexe ^
        /out:"%~dp0Stash.exe" ^
        /win32icon:"%~dp0src\app.ico" ^
        /win32manifest:"%~dp0src\app.manifest" ^
        /optimize+ ^
        /codepage:65001 ^
        /r:System.dll ^
        /r:System.Core.dll ^
        /r:System.Drawing.dll ^
        /r:System.IO.Compression.dll ^
        /r:System.IO.Compression.FileSystem.dll ^
        /r:System.Web.Extensions.dll ^
        /r:System.Windows.Forms.dll ^
        /recurse:"%~dp0src\*.cs"

if %ERRORLEVEL% NEQ 0 (
    echo [ERROR] Compilation failed!
    exit /b %ERRORLEVEL%
)

rem Optional code signing: set STASH_SIGN_PFX (and STASH_SIGN_PASSWORD) to sign the EXE.
rem Once a release is signed, the updater only accepts updates signed by the same publisher.
if defined STASH_SIGN_PFX (
    signtool sign /fd SHA256 /tr http://timestamp.digicert.com /td SHA256 /f "%STASH_SIGN_PFX%" /p "%STASH_SIGN_PASSWORD%" "%~dp0Stash.exe"
    if errorlevel 1 (
        echo [ERROR] Code signing failed!
        exit /b 1
    )
)

set "STASH_HASH="
for /f "skip=1 tokens=*" %%H in ('certutil -hashfile "%~dp0Stash.exe" SHA256') do if not defined STASH_HASH set "STASH_HASH=%%H"
set "STASH_HASH=%STASH_HASH: =%"
if not defined STASH_HASH (
    echo [ERROR] Could not generate Stash.exe.sha256
    exit /b 1
)
>"%~dp0Stash.exe.sha256" echo %STASH_HASH%  Stash.exe

if exist "%~dp0dist" (
    copy /Y "%~dp0Stash.exe" "%~dp0dist\Stash.exe" >nul
    copy /Y "%~dp0Stash.exe.sha256" "%~dp0dist\Stash.exe.sha256" >nul
)

echo [SUCCESS] Built standalone executable: %~dp0Stash.exe
exit /b 0
