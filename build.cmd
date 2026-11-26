@echo off
setlocal DisableDelayedExpansion
cd /d "%~dp0"
if not defined DOTNET_EXE set "DOTNET_EXE=dotnet"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false"
set "DOTNET_GENERATE_ASPNET_CERTIFICATE=false"
set "DOTNET_CLI_HOME=%CD%\.build\cli-home"
set "NUGET_PACKAGES=%CD%\.build\packages"
if not exist ".build" mkdir ".build"
"%DOTNET_EXE%" --version > ".build\sdk-version.txt"
if errorlevel 1 exit /b 1
set /p SDK_VERSION=<".build\sdk-version.txt"
if not "%SDK_VERSION%"=="10.0.404" (
echo This source requires .NET SDK 10.0.404.
exit /b 1
)
if /i "%~1"=="build" goto build
if /i "%~1"=="test" goto test
if /i "%~1"=="run" goto run
if /i "%~1"=="demo" goto demo
echo Usage: build.cmd build ^| test ^| run ^| demo
exit /b 2
:build
"%DOTNET_EXE%" restore Scanner.Wpf\Scanner.Wpf.csproj --configfile NuGet.Config --artifacts-path .build\artifacts --nologo
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" build Scanner.Wpf\Scanner.Wpf.csproj --no-restore --artifacts-path .build\artifacts -c Release --nologo
exit /b %errorlevel%
:test
"%DOTNET_EXE%" restore Presentation.Checks\Presentation.Checks.csproj --configfile NuGet.Config --artifacts-path .build\checks --nologo
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" build Presentation.Checks\Presentation.Checks.csproj --no-restore --artifacts-path .build\checks -c Release --nologo
if errorlevel 1 exit /b 1
"%DOTNET_EXE%" ".build\checks\bin\Presentation.Checks\release\Presentation.Checks.dll"
exit /b %errorlevel%
:run
if not exist ".build\artifacts\bin\Scanner.Wpf\release\Scanner.Wpf.dll" goto missing
"%DOTNET_EXE%" ".build\artifacts\bin\Scanner.Wpf\release\Scanner.Wpf.dll" --software-rendering
exit /b %errorlevel%
:demo
if not exist ".build\artifacts\bin\Scanner.Wpf\release\Scanner.Wpf.dll" goto missing
"%DOTNET_EXE%" ".build\artifacts\bin\Scanner.Wpf\release\Scanner.Wpf.dll" --demo --software-rendering
exit /b %errorlevel%
:missing
echo Run build.cmd build first.
exit /b 1