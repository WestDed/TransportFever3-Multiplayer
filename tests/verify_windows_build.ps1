$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
function Assert-Amd64PE([string] $relative) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $root $relative))
    if ($bytes.Length -lt 128 -or $bytes[0] -ne 0x4d -or $bytes[1] -ne 0x5a) { throw "Not a PE file: $relative" }
    $offset = [BitConverter]::ToInt32($bytes, 0x3c)
    if ($offset -lt 0 -or $offset + 6 -ge $bytes.Length -or [BitConverter]::ToUInt32($bytes, $offset) -ne 0x4550 -or [BitConverter]::ToUInt16($bytes, $offset + 4) -ne 0x8664) {
        throw "Not an AMD64 PE file: $relative"
    }
    Write-Host "PASS built AMD64 PE: $relative"
}
Assert-Amd64PE 'launcher/bin/Release/MPFever.exe'
Assert-Amd64PE 'native/out/winhttp.dll'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $installation) { throw 'Cannot find native toolchain for export checks' }
$version = (Get-Content (Join-Path $installation 'VC/Auxiliary/Build/Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$dumpbin = Join-Path $installation "VC/Tools/MSVC/$version/bin/Hostx64/x64/dumpbin.exe"
$exports = (& $dumpbin /nologo /exports (Join-Path $root 'native/out/winhttp.dll') | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'dumpbin /exports failed' }
$exportMap = @{}
foreach ($match in [regex]::Matches($exports, '(?m)^\s+(\d+)\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(WinHttp\w+)\b')) {
    $exportMap[$match.Groups[2].Value] = [int]$match.Groups[1].Value
}
foreach ($line in Get-Content (Join-Path $root 'native/winhttp.def')) {
    if ($line -match '^\s*(WinHttp\w+)=\w+\s+@(\d+)') {
        $name = $Matches[1]; $ordinal = [int]$Matches[2]
        if (-not $exportMap.ContainsKey($name) -or $exportMap[$name] -ne $ordinal) { throw "Missing or wrong ordinal for native export: $name" }
        Write-Host "PASS native export: $name @$ordinal"
    }
}
if ($exportMap.Count -ne 14) { throw "Unexpected native export count: $($exportMap.Count)" }
$dependencies = (& $dumpbin /nologo /dependents (Join-Path $root 'native/out/winhttp.dll') | Out-String)
if ($LASTEXITCODE -ne 0 -or $dependencies -notmatch '(?i)\bKERNEL32\.dll\b' -or $dependencies -match '(?i)(vcruntime|msvcp|ucrtbase|msvcrt)[^\s]*\.dll') { throw 'Unexpected freestanding DLL dependencies' }
$exports | Set-Content (Join-Path $root 'release/native-exports.txt') -Encoding UTF8
$packageVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'launcher/MainForm.cs')), 'public const string Version = "([a-z0-9.-]+)"').Groups[1].Value
$buildVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'build.bat')), 'set "VERSION=([a-z0-9.-]+)"').Groups[1].Value
if (!$packageVersion -or $packageVersion -ne $buildVersion) { throw 'Launcher/package versions differ' }
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root "release/MPFever-$packageVersion.zip"))
try {
    foreach ($entry in @('MPFever/MPFever.exe','MPFever/MPFever.exe.config','MPFever/winhttp.dll','MPFever/LICENSE','MPFever/BUSINESS_MECHANICS.md','MPFever/OFFLINE_UPSTREAM_FIXES.md','MPFever/COMPANY_PROGRESSION.md','MPFever/INCIDENT_2026_10_18_MAP_LOAD.md','MPFever/INCIDENT_2026_10_18_SNAPSHOT_QUEUE.md','MPFever/LOBBY_AND_SNAPSHOT_CACHE.md','MPFever/FASTER_CONNECTIONS.md','MPFever/mod/mpfever_1/content/mpfever_common.lua')) {
        $found = $zip.GetEntry($entry)
        if ($null -eq $found -or $found.Length -eq 0) { throw "Missing or empty packaged entry: $entry" }
        Write-Host "PASS package entry: $entry"
    }
    $stream = $zip.GetEntry('MPFever/LICENSE').Open()
    $reader = [IO.StreamReader]::new($stream)
    try { if ($reader.ReadToEnd() -ne [IO.File]::ReadAllText((Join-Path $root 'LICENSE'))) { throw 'Packaged license differs from original' } }
    finally { $reader.Dispose() }
    # A file copied into the ZIP is invisible to the game's resource catalog if
    # _content.json omits it. Validate the actual release, not just source files.
    $prefix = 'MPFever/mod/mpfever_1/'
    $catalogEntry = $zip.GetEntry($prefix + '_content.json')
    if ($null -eq $catalogEntry) { throw 'Missing packaged game resource catalog' }
    $reader = [IO.StreamReader]::new($catalogEntry.Open())
    try { $catalogText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    if ($catalogText -ne [IO.File]::ReadAllText((Join-Path $root 'mod/mpfever_1/_content.json'))) { throw 'Packaged resource catalog differs from source' }
    $catalog = $catalogText | ConvertFrom-Json
    $registered = @{}
    foreach ($name in $catalog.files) {
        if ($name -isnot [string] -or [string]::IsNullOrEmpty($name) -or $name.StartsWith('/') -or $name.Contains('\') -or $name.Split('/') -contains '..' -or $registered.ContainsKey($name)) { throw "Invalid or duplicate resource path: $name" }
        $registered[$name] = $true
        $entry = $zip.GetEntry($prefix + 'content/' + $name)
        if ($null -eq $entry -or $entry.Length -eq 0) { throw "Missing or empty packaged game resource: $name" }
        $hash = [Security.Cryptography.SHA256]::Create()
        $stream = $entry.Open()
        try { $actualHash = [BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-', '').ToLowerInvariant() }
        finally { $stream.Dispose(); $hash.Dispose() }
        $expectedHash = (Get-FileHash (Join-Path $root ('mod/mpfever_1/content/' + $name)) -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) { throw "Packaged resource differs from source: $name" }
        Write-Host "PASS registered packaged game resource: $name"
    }
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName.StartsWith($prefix + 'content/', [StringComparison]::Ordinal) -and -not $entry.FullName.EndsWith('/')) {
            $name = $entry.FullName.Substring(($prefix + 'content/').Length)
            if (-not $registered.ContainsKey($name)) { throw "Packaged content is not registered in game catalog: $name" }
        }
    }
}
finally { $zip.Dispose() }
$versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $root 'launcher/bin/Release/MPFever.exe'))
$packageParts = [regex]::Match($packageVersion, '^(\d+\.\d+\.\d+)-westded\.(\d+)-experimental$')
if (-not $packageParts.Success) { throw "Cannot derive file version from package: $packageVersion" }
$expectedFileVersion = $packageParts.Groups[1].Value + '.' + $packageParts.Groups[2].Value
if ($versionInfo.FileVersion -ne $expectedFileVersion) { throw "Wrong launcher file version: $($versionInfo.FileVersion), expected $expectedFileVersion" }
$manifest = [ordered]@{
    packageVersion = $packageVersion
    sourceCommit = $env:GITHUB_SHA
    protocolVersion = [int]([regex]::Match([IO.File]::ReadAllText((Join-Path $root 'launcher/NetworkIO.cs')), 'const int Version = (\d+)').Groups[1].Value)
    experimental = $true
    gameTested = $false
    binaries = @('launcher/bin/Release/MPFever.exe','native/out/winhttp.dll') | ForEach-Object {
        [ordered]@{ file = $_; sha256 = (Get-FileHash (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'release/build-manifest.json') -Encoding UTF8
Write-Host 'Windows binaries, native exports/dependencies and package verified; no game or engine hooks were executed.'
