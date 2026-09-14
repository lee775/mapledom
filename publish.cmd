@echo off
rem MapleDom single-exe build (self-contained, no .NET install needed). Output: dist\MapleDom.exe
setlocal
cd /d "%~dp0"
dotnet publish src\MapleDom.App\MapleDom.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -o dist
if errorlevel 1 (echo. & echo BUILD FAILED & if /i not "%1"=="--no-pause" pause & exit /b 1)
echo.
echo Done: %~dp0dist\MapleDom.exe
if /i not "%1"=="--no-pause" pause
