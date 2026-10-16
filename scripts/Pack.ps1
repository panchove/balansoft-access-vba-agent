#Requires -Version 5.1
<#
.SYNOPSIS
    Empaqueta el add-in en un ZIP distribuible (Release x86).

.DESCRIPTION
    1. Compila la solución en Release x86.
    2. Prepara dist/ con: DLL, RegisterAddin.ps1, UnregisterAddin.ps1,
       README.md y version.txt.
    3. Genera version.txt con versión + commit (git rev-parse).
    4. Comprime todo en Balansoft.AIAddin.Access-{version}-x86.zip.

    dist/ y *.zip están en .gitignore.

.PARAMETER Configuration
    Configuración del build. Default: Release.

.PARAMETER OutputDir
    Directorio temporal de empaquetado. Default: <raíz>\dist.

.EXAMPLE
    PS> .\Pack.ps1
    Empaqueta la release.

.EXAMPLE
    PS> .\Pack.ps1 -OutputDir C:\releases\balansoft
    Empaqueta en un directorio alternativo.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.2.0
    Requiere: .NET SDK en PATH, git en PATH, PowerShell 5.1.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("x86", "x64")]
    [string]$Architecture = "x86",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

# Se compila el proyecto de tests porque arrastra la referencia al de src y
# cubre ambos en una sola pasada. La DLL acaba en bin\x86\<Config>\net48\:
# `-p:PlatformTarget=<Arch>` es lo que añade el segmento de arquitectura a la
# se compila el .csproj en lugar de la solución. `dotnet build` y no `msbuild`:
# el MSBuild del SDK es el único que resuelve Microsoft.NET.Sdk.
$testProj = Join-Path $root "tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj"
$srcProj  = Join-Path $root "src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj"

foreach ($p in @($testProj, $srcProj)) {
    if (-not (Test-Path $p)) {
        Write-Host "ERROR: proyecto no encontrado: $p" -ForegroundColor Red
        exit 1
    }
}

if ([string]::IsNullOrEmpty($OutputDir)) {
    $OutputDir = Join-Path $root "dist"
}

Write-Host ""
Write-Host "=== Pack ===" -ForegroundColor Cyan
Write-Host "Raíz:        $root"
Write-Host "Config:      $Configuration"
Write-Host "Architecture: $Architecture"
Write-Host "Output:      $OutputDir"
Write-Host ""

# 1. Build Release x86
Write-Host "[1/5] Compilando $Configuration $Architecture..." -ForegroundColor DarkGray
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
    Write-Host "ERROR: dotnet no encontrado en PATH." -ForegroundColor Red
    Write-Host "Instala el .NET SDK: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
    exit 2
}

& dotnet build $testProj -t:Restore,Build -c $Configuration -p:Platform=$Architecture -p:PlatformTarget=$Architecture -v:minimal --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: build falló (exit $LASTEXITCODE)." -ForegroundColor Red
    exit 3
}
Write-Host "  + Build OK" -ForegroundColor Green

# 2. Preparar dist/
Write-Host ""
Write-Host "[2/5] Preparando dist/..." -ForegroundColor DarkGray
if (Test-Path $OutputDir) {
    Remove-Item $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

$binDir = Join-Path $root "src\Balansoft.AIAddin.Access\bin\$Architecture\$Configuration\net48"
$dll = Join-Path $binDir "Balansoft.AIAddin.Access.dll"
if (-not (Test-Path $dll)) {
    Write-Host "ERROR: DLL no encontrada tras el build: $dll" -ForegroundColor Red
    exit 4
}
Copy-Item $dll -Destination $OutputDir
Write-Host "  + Balansoft.AIAddin.Access.dll" -ForegroundColor Green

Copy-Item (Join-Path $PSScriptRoot "RegisterAddin.ps1")   -Destination $OutputDir
Write-Host "  + RegisterAddin.ps1" -ForegroundColor Green

Copy-Item (Join-Path $PSScriptRoot "UnregisterAddin.ps1") -Destination $OutputDir
Write-Host "  + UnregisterAddin.ps1" -ForegroundColor Green

$readme = Join-Path $root "README.md"
if (Test-Path $readme) {
    Copy-Item $readme -Destination $OutputDir
    Write-Host "  + README.md" -ForegroundColor Green
}

# 3. version.txt
Write-Host ""
Write-Host "[3/5] Generando version.txt..." -ForegroundColor DarkGray
# NBGV inyecta la versión en el assembly; la leemos de la DLL construida.
$dll = Join-Path $root "src\Balansoft.AIAddin.Access\bin\$Architecture\$Configuration\net48\Balansoft.AIAddin.Access.dll"
$version = "0.0.0"
if (Test-Path $dll) {
    $asm = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
    $version = $asm.Version.ToString(3)  # major.minor.patch
}

$commit = "unknown"
$git = Get-Command git -ErrorAction SilentlyContinue
if ($git) {
    Push-Location $root
    try {
        $commitResult = & git rev-parse --short HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and $commitResult) {
            $commit = $commitResult.ToString().Trim()
        }
    }
    catch { }
    finally { Pop-Location }
}

"$version`n$commit" | Out-File (Join-Path $OutputDir "version.txt") -Encoding utf8 -NoNewline:$false
Write-Host "  + version.txt ($version / $commit)" -ForegroundColor Green

# 4. ZIP
Write-Host ""
Write-Host "[4/5] Comprimiendo..." -ForegroundColor DarkGray
$zipName = "Balansoft.AIAddin.Access-$version-$Architecture.zip"
$zipPath = Join-Path $root $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -Path "$OutputDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "  + $zipName" -ForegroundColor Green

# 5. Resumen
Write-Host ""
Write-Host "[5/5] Verificando contenido del ZIP..." -ForegroundColor DarkGray
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entry in $zip.Entries) {
        Write-Host "      $($entry.Name) ($($entry.Length) bytes)" -ForegroundColor DarkGray
    }
}
finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "Paquete listo: $zipPath" -ForegroundColor Green
Write-Host ""
exit 0