@echo off
setlocal
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%~dp0..\bin\tests" mkdir "%~dp0..\bin\tests"
"%CSC%" /nologo /target:exe /out:"%~dp0..\bin\tests\DownloadManagerTests.exe" /main:DownloadManagerTests /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /recurse:"%~dp0..\src\*.cs" "%~dp0DownloadManagerTests.cs"
if errorlevel 1 exit /b 1
"%~dp0..\bin\tests\DownloadManagerTests.exe"
exit /b %ERRORLEVEL%
