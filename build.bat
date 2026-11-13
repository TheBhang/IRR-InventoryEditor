@echo off
rem Builds IRR-InventoryEditor.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /optimize /codepage:65001 /target:winexe /out:"%~dp0IRR-InventoryEditor.exe" ^
  /win32icon:"%~dp0assets\app.ico" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  "%~dp0src\AssemblyInfo.cs" "%~dp0src\Lang.cs" "%~dp0src\Gvas.cs" "%~dp0src\Model.cs" "%~dp0src\Tools.cs" ^
  "%~dp0src\Theme.cs" "%~dp0src\Base.cs" "%~dp0src\MainForm.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built IRR-InventoryEditor.exe
