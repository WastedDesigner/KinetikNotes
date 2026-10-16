@echo off
cd /d "%~dp0"
rem Builds KinetikNotes.exe with the .NET Framework compiler that ships with Windows. No SDK needed.
rem "build.bat package" also makes KinetikNotes.zip (the exe, README and licence) for a release.
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /utf8output /codepage:65001 /platform:x64 /target:winexe /out:KinetikNotes.new.exe /win32icon:icon.ico ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:Microsoft.VisualBasic.dll ^
  KinetikNotes.cs || exit /b 1
rem Built under a new name first, so this works while the app is running. The exe it replaces goes to ..\Old Versions
rem (when that folder is there), stamped with the date and time, to go back to if a build misbehaves.
if exist KinetikNotes.exe if exist "..\Old Versions\" (
  for /f %%t in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd-HHmmss"') do move /y KinetikNotes.exe "..\Old Versions\KinetikNotes-%%t.exe" >nul
)
if exist KinetikNotes.exe move /y KinetikNotes.exe KinetikNotes.old.exe >nul
move /y KinetikNotes.new.exe KinetikNotes.exe >nul || exit /b 1
if exist KinetikNotes.old.exe del KinetikNotes.old.exe 2>nul
echo Built KinetikNotes.exe
if /i not "%~1"=="package" exit /b 0
if exist dist rmdir /s /q dist
mkdir dist\KinetikNotes
copy /y KinetikNotes.exe dist\KinetikNotes\ >nul
copy /y README.md dist\KinetikNotes\ >nul
copy /y LICENSE dist\KinetikNotes\ >nul
if exist KinetikNotes.zip del KinetikNotes.zip
powershell -NoProfile -Command "Compress-Archive -Path dist\KinetikNotes\* -DestinationPath KinetikNotes.zip"
rmdir /s /q dist
echo Made KinetikNotes.zip
