# Compatible with Windows PowerShell 5.1 and .NET Framework. Explicit entry names
# avoid the framework's legacy backslash ZIP entries on Windows.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path $PSScriptRoot
$source = (Resolve-Path (Join-Path $root 'release/dist/MPFever')).Path
$version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'build.bat')), 'set "VERSION=([a-z0-9.-]+)"').Groups[1].Value
if (!$version) { throw 'Cannot read package version from build.bat' }
$output = Join-Path $root "release/MPFever-$version.zip"
$archive = [IO.Compression.ZipFile]::Open($output, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $source -File -Recurse) {
        $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, "MPFever/$relative", [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $archive.Dispose() }
