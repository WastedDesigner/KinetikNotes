@echo off
cd /d "%~dp0"
rem Builds KinetikNotes.exe with the .NET Framework compiler that ships with Windows. No SDK needed.
rem "build.bat package" also makes KinetikNotes.zip (the exe, README and licence) for a release.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikNotes.new.exe /win32icon:icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.VisualBasic.dll ^
  KinetikNotes.cs SuiteSupport.cs || exit /b 1
rem Keep previous binaries. Never delete recovery material during a build.
if not exist "..\Old Versions\" mkdir "..\Old Versions" || exit /b 1
if exist KinetikNotes.exe (
  for /f %%t in ('powershell -NoProfile -Command "[Guid]::NewGuid().ToString('N')"') do move /y KinetikNotes.exe "..\Old Versions\KinetikNotes-%%t.exe" >nul
  if exist KinetikNotes.exe exit /b 1
)
move /y KinetikNotes.new.exe KinetikNotes.exe >nul || exit /b 1
echo Built KinetikNotes.exe
if /i not "%~1"=="package" exit /b 0
powershell -NoProfile -ExecutionPolicy Bypass -File package.ps1 -App KinetikNotes
if errorlevel 1 exit /b 1
exit /b 0
