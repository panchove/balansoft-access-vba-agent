$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$files = Get-ChildItem -Path $root -Recurse -Filter *.cs -File | Where-Object { $_.FullName -notmatch '\\bin\\|\\obj\\' }
foreach ($f in $files) {
    $content = Get-Content $f.FullName -Raw
    if ($content -notmatch 'SPDX-License-Identifier') {
        $header = "// Copyright © 2026 Balansoft`n// SPDX-License-Identifier: LGPL-3.0-or-later`n`n"
        Set-Content $f.FullName ($header + $content) -Encoding UTF8
        Write-Host "Added header to $($f.FullName)"
    } else {
        Write-Host "Header already present in $($f.FullName)"
    }
}