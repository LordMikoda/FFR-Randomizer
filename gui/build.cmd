@echo off
rem Builds build\FFRRandomizer.exe with the C# compiler that ships with .NET Framework 4.8.
rem The icon comes from art\icon.ico, made by tools\make_art.ps1.
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set ICON=%~dp0..\art\icon.ico
if not exist "%~dp0build" mkdir "%~dp0build"
"%CSC%" /nologo /target:winexe /optimize+ /out:"%~dp0build\FFRRandomizer.exe" /win32icon:"%ICON%" /resource:"%ICON%",FFRRandomizerGui.icon.ico /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0FFRRandomizerGui.cs"
