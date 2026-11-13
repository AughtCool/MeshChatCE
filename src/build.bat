@echo off
rem ===========================================================
rem  build.bat - builds MeshChatCE.exe for Windows Mobile
rem  Needs: .NET Framework 3.5 (Windows feature) and
rem         NetCFSetupv35.msi (.NET Compact Framework 3.5)
rem  Just double-click this file. The result is MeshChatCE.exe
rem  and a log in build_log.txt.
rem ===========================================================
setlocal
cd /d "%~dp0"

rem ---- find the C# compiler that comes with Windows ----
set "CSC=%WINDIR%\Microsoft.NET\Framework\v3.5\csc.exe"
if exist "%CSC%" goto havecsc
set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if exist "%CSC%" goto havecsc
echo.
echo ERROR: could not find csc.exe.
echo Turn on ".NET Framework 3.5" in Control Panel - Programs -
echo "Turn Windows features on or off", then try again.
goto done
:havecsc

rem ---- find the Compact Framework reference files ----
set "CF=%ProgramFiles(x86)%\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE"
if exist "%CF%\mscorlib.dll" goto havecf
set "CF=%ProgramFiles%\Microsoft.NET\SDK\CompactFramework\v3.5\WindowsCE"
if exist "%CF%\mscorlib.dll" goto havecf
echo.
echo ERROR: could not find the Compact Framework files (mscorlib.dll).
echo Is NetCFSetupv35.msi installed? Searching for them now...
dir /s /b "%ProgramFiles(x86)%\Microsoft.NET\mscorlib.dll" 2>nul
dir /s /b "%ProgramFiles%\Microsoft.NET\mscorlib.dll" 2>nul
echo Please include the lines above if you open an issue on GitHub.
goto done
:havecf

echo Compiler:   %CSC%
echo References: %CF%
echo.
echo Building MeshChatCE.exe ...
if exist MeshChatCE.exe del MeshChatCE.exe

"%CSC%" /nologo /noconfig /nostdlib /target:winexe /optimize+ ^
  /out:MeshChatCE.exe ^
  /r:"%CF%\mscorlib.dll" ^
  /r:"%CF%\System.dll" ^
  /r:"%CF%\System.Drawing.dll" ^
  /r:"%CF%\System.Windows.Forms.dll" ^
  /r:"%CF%\Microsoft.WindowsCE.Forms.dll" ^
  /win32icon:MeshChatCE.ico ^
  /resource:MeshChatCE.ico,MeshChatCE.ico ^
  Mesh.cs MainForm.cs NodesForm.cs Extras.cs MapForm.cs Screens.cs ChatView.cs > build_log.txt 2>&1

type build_log.txt
echo.
if exist MeshChatCE.exe (
  echo ===================================================
  echo  SUCCESS: MeshChatCE.exe is ready.
  echo  Copy it to the SD card, next to meshchat_history.txt
  echo ===================================================
) else (
  echo ===================================================
  echo  BUILD FAILED - see the messages above
  echo  ^(they are also saved in build_log.txt^)
  echo ===================================================
)

:done
echo.
pause
