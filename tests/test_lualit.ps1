$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$core = Get-Content -LiteralPath (Join-Path $root 'launcher/Core.cs') -Raw -Encoding UTF8
$start = $core.IndexOf('    static class LuaLit')
$end = $core.IndexOf('    /// <summary>File link with one game instance:', $start)
if ($start -lt 0 -or $end -le $start) { throw 'LuaLit source extraction anchor changed' }
$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
'@ + "`n" + $core.Substring($start, $end - $start) + "`n" + (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LuaLitTests.cs') -Raw -Encoding UTF8)
Add-Type -TypeDefinition $source -Language CSharp
[LuaLitTests]::Run()
