@echo off
setlocal

REM ===== 修改为你的游戏安装目录 =====
REM 默认 Steam 路径示例，请按实际修改
set "GAME_DIR=D:\Download\STEAM\steamapps\common\Probably Stolen"
set "MANAGED=%GAME_DIR%\Probably Stolen_Data\Managed"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo [ERROR] csc.exe not found: %CSC%
  pause
  exit /b 1
)
if not exist "%MANAGED%\Assembly-CSharp.dll" (
  echo [ERROR] Game assemblies not found under: %MANAGED%
  echo Please edit GAME_DIR in build.bat
  pause
  exit /b 1
)

echo Building AppraisalExpansion.dll ...
"%CSC%" -nologo -target:library -langversion:5 -codepage:65001 ^
  -out:mod\AppraisalExpansion.dll ^
  -r:"%MANAGED%\netstandard.dll" ^
  -r:"%MANAGED%\System.Runtime.dll" ^
  -r:"%MANAGED%\Assembly-CSharp.dll" ^
  -r:"%MANAGED%\Assembly-CSharp-firstpass.dll" ^
  -r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
  -r:"%MANAGED%\0Harmony.dll" ^
  -r:"%MANAGED%\UnityEngine.UI.dll" ^
  -r:"%MANAGED%\Unity.TextMeshPro.dll" ^
  src\AppraisalExpansion.cs

if errorlevel 1 (
  echo.
  echo [FAILED] Build failed.
  pause
  exit /b 1
)

echo.
echo [OK] Build succeeded: mod\AppraisalExpansion.dll
pause
