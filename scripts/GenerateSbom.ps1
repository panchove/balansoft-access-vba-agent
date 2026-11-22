# Copyright c 2026 Balansoft
# SPDX-License-Identifier: LGPL-3.0-or-later
# Genera un SBOM determinista a partir de los assets restaurados por NuGet.
# Sin dependencias externas: solo parsea obj/project.assets.json.

[CmdletBinding()]
param(
    [string] $Project = 'src/Balansoft.AIAddin.Access/Balansoft.AIAddin.Access.csproj',
    [string] $Output = 'artifacts/sbom.json'
)

$ErrorActionPreference = 'Stop'
$root = (Get-Location).Path
$assets = Join-Path $root ((Split-Path $Project -Parent) + '/obj/project.assets.json')
if (-not (Test-Path $assets)) {
    throw "No existe $assets. Ejecuta dotnet restore antes de generar el SBOM."
}

$doc = Get-Content $assets -Raw | ConvertFrom-Json
$packages = @()
foreach ($prop in $doc.libraries.PSObject.Properties) {
    $library = $prop.Value
    if ($library.type -ne 'package') { continue }
    $parts = $prop.Name -split '/', 2
    $packages += [pscustomobject]@{
        name    = $parts[0]
        version = if ($parts.Count -gt 1) { $parts[1] } else { '' }
        type    = 'nuget'
    }
}
$packages = $packages | Sort-Object name, version

$sbom = [ordered]@{
    schemaVersion    = '1.0'
    generator        = 'Balansoft.GenerateSbom'
    generatedAt      = (Get-Date).ToUniversalTime().ToString('o')
    component        = 'Balansoft.AIAddin.Access'
    targetFramework  = 'net48'
    components       = @($packages)
}

$outPath = Join-Path $root $Output
$outDir = Split-Path $outPath -Parent
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
$sbom | ConvertTo-Json -Depth 8 | Set-Content $outPath -Encoding UTF8
Write-Host "SBOM generado: $Output ($($packages.Count) dependencias)"
