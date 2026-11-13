@echo off
rem Builds IRR-InventoryEditor.exe using the C# compiler that ships with Windows (.NET Framework 4.x).
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
set S=%~dp0src
"%CSC%" /nologo /optimize /codepage:65001 /target:winexe /out:"%~dp0IRR-InventoryEditor.exe" ^
  /win32icon:"%~dp0assets\app.ico" /win32manifest:"%S%\app.manifest" ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll ^
  "%S%\AssemblyInfo.cs" "%S%\Lang.cs" "%S%\Gvas.cs" "%S%\Model.cs" "%S%\Tools.cs" "%S%\Undo.cs" ^
  "%S%\Theme.cs" "%S%\Base.cs" "%S%\Gunsmith.cs" "%S%\Dialogs.cs" "%S%\AddItemForm.cs" "%S%\GridPanel.cs" "%S%\MainForm.cs"
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo Built IRR-InventoryEditor.exe
