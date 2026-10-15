$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $installation) { throw 'Cannot find native toolchain' }
$setup = Join-Path $installation 'Common7/Tools/VsDevCmd.bat'
Set-Location $root
New-Item -ItemType Directory -Force 'audit-results/native-privacy' | Out-Null
$command = "call `"$setup`" -no_logo -arch=x64 -host_arch=x64 >nul && cl /nologo /EHsc /W4 /WX tests/native_privacy_tests.cpp /Fe:audit-results/native-privacy/test.exe /Fo:audit-results/native-privacy/test.obj && audit-results\native-privacy\test.exe"
& cmd /c $command
if ($LASTEXITCODE -ne 0) { throw 'Native UI filter regression failed' }
Write-Host 'PASS production native UI filter on Windows; no game hooks executed.'
