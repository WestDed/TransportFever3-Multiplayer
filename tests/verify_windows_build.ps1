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
$zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $root 'release/MPFever-0.3.6-experimental.zip'))
try {
    foreach ($entry in @('MPFever/MPFever.exe','MPFever/MPFever.exe.config','MPFever/winhttp.dll','MPFever/LICENSE','MPFever/mod/mpfever_1/content/mpfever_common.lua')) {
        $found = $zip.GetEntry($entry)
        if ($null -eq $found -or $found.Length -eq 0) { throw "Missing or empty packaged entry: $entry" }
        Write-Host "PASS package entry: $entry"
    }
    $stream = $zip.GetEntry('MPFever/LICENSE').Open()
    $reader = [IO.StreamReader]::new($stream)
    try { if ($reader.ReadToEnd() -ne [IO.File]::ReadAllText((Join-Path $root 'LICENSE'))) { throw 'Packaged license differs from original' } }
    finally { $reader.Dispose() }
}
finally { $zip.Dispose() }
$manifest = [ordered]@{
    sourceCommit = $env:GITHUB_SHA
    protocolVersion = [int]([regex]::Match([IO.File]::ReadAllText((Join-Path $root 'launcher/NetworkIO.cs')), 'const int Version = (\d+)').Groups[1].Value)
    experimental = $true
    gameTested = $false
    binaries = @('launcher/bin/Release/MPFever.exe','native/out/winhttp.dll') | ForEach-Object {
        [ordered]@{ file = $_; sha256 = (Get-FileHash (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $root 'release/build-manifest.json') -Encoding UTF8
Write-Host 'Windows build and packaging verified; no game or engine hooks were executed.'
