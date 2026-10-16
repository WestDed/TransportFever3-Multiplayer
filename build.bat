@echo off
rem Builds the whole MPFever release from source: native DLL, launcher, then the zip in release\.
rem Requires Visual Studio 2022 or later with the "Desktop development with C++" and ".NET desktop" workloads
rem (.NET Framework 4.8 targeting pack).
setlocal
cd /d "%~dp0"
echo MPFever build %date% %time%
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (echo vswhere.exe not found: install Visual Studio & exit /b 1)
for /f "usebackq delims=" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do set "MSBUILD=%%i"
if not defined MSBUILD (echo MSBuild not found & exit /b 1)

echo === 1/3 native module
call native\build.bat || exit /b 1

echo === 2/3 launcher
"%MSBUILD%" launcher\MPFever.csproj -restore -p:Configuration=Release -v:minimal -nologo || exit /b 1

echo === 3/3 package
set "VERSION=0.3.7-westded.2-experimental"
set "DIST=release\dist\MPFever"
if exist release\dist rmdir /s /q release\dist
mkdir "%DIST%" || exit /b 1
copy /y launcher\bin\Release\MPFever.exe "%DIST%\" >nul || exit /b 1
copy /y launcher\bin\Release\MPFever.exe.config "%DIST%\" >nul || exit /b 1
copy /y native\out\winhttp.dll "%DIST%\" >nul || exit /b 1
copy /y LICENSE "%DIST%\" >nul || exit /b 1
for %%f in (README INSTALL ROADMAP CHANGELOG) do copy /y docs\%%f.txt "%DIST%\" >nul || exit /b 1
copy /y docs\BUSINESS_MECHANICS.md "%DIST%\BUSINESS_MECHANICS.md" >nul || exit /b 1
copy /y docs\OFFLINE_UPSTREAM_FIXES.md "%DIST%\OFFLINE_UPSTREAM_FIXES.md" >nul || exit /b 1
copy /y docs\COMPANY_PROGRESSION.md "%DIST%\COMPANY_PROGRESSION.md" >nul || exit /b 1
xcopy /e /i /q /y mod\mpfever_1 "%DIST%\mod\mpfever_1" >nul || exit /b 1
if exist "release\MPFever-%VERSION%.zip" del "release\MPFever-%VERSION%.zip"
rem explicit forward-slash zip entries, also on the older framework used by PowerShell 5
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\package.ps1 || exit /b 1
echo BUILD OK: release\MPFever-%VERSION%.zip
