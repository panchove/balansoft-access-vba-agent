#Requires -Version 5.1
<#
.SYNOPSIS
    Atajos de desarrollo para Balansoft Access VBA Agent (NIM Direct).

.DESCRIPTION
    Envuelve los comandos más frecuentes (build, test, pack, reg,
    unreg, clean, restore, all) en un solo script con subcomandos.
    No reimplementa nada: delega en dotnet build, dotnet vstest y los
    scripts existentes (RegisterAddin.ps1, UnregisterAddin.ps1, Pack.ps1).

    No es un orquestador. Es azúcar para no escribir 3-4 comandos
    cada vez que compilas.

.PARAMETER Command
    Subcomando a ejecutar:
      build    - Compila Debug x86 por defecto.
      release  - Compila Release x86.
      test     - Ejecuta los tests sin integración (Category!=Integration).
      testall  - Ejecuta TODOS los tests, incluida la integración NIM.
      pack     - Empaqueta la release (delega en Pack.ps1).
      reg      - Registra el add-in (delega en RegisterAddin.ps1).
      unreg    - Desregistra el add-in (delega en UnregisterAddin.ps1).
      restore  - Restaura paquetes NuGet.
      clean    - Limpia bin/obj/dist y el ZIP.
      all      - build + test + pack (Release).
      status   - Muestra el estado del repo (build, tests, pack, registro).

.PARAMETER Configuration
    Debug o Release. Default: Debug para build, Release para pack/all.

.PARAMETER Filter
    Expresión de filtro de VSTest, p. ej. "FullyQualifiedName~Bridge".
    Opcional.

.PARAMETER NoBuild
    Para test/testall: no recompila antes de testear.

.EXAMPLE
    PS> .\dev.ps1 build
    Compila Debug x86.

.EXAMPLE
    PS> .\dev.ps1 release
    Compila Release x86.

.EXAMPLE
    PS> .\dev.ps1 test
    Ejecuta los tests sin integración NIM.

.EXAMPLE
    PS> .\dev.ps1 test -Filter "FullyQualifiedName~Bridge"
    Ejecuta solo los tests del bridge.

.EXAMPLE
    PS> .\dev.ps1 all
    build Release + test + pack.

.NOTES
    Autor:    Big Pickle (arquitecto)
    Versión:  0.2.0
    Requiere: PowerShell 5.1, .NET SDK (dotnet build / dotnet vstest).
    No requiere admin (excepto 'reg'/'unreg', que van por usuario).
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet(
        "build", "release", "test", "testall", "pack", "reg", "unreg",
        "restore", "clean", "all", "status", "help"
    )]
    [string]$Command,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "",

    [string]$Filter = "",

    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

# ─────────────────────────────────────────────────────────────
# Rutas
# ─────────────────────────────────────────────────────────────

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$zipPattern = "Balansoft.AIAddin.Access-*-x86.zip"

# Se compilan los .csproj, no la solución: `msbuild Balansoft.AIAddin.slnx
# /p:Platform=x86` no propaga Platform a los proyectos SDK-style y escribe en
# bin\<Config>\ en vez de bin\x86\<Config>\. Compilando los csproj la ruta es
# siempre bin\x86\<Config>\net48\, que es la que usan Pack.ps1 y
# RegisterAddin.ps1.
$srcProj  = Join-Path $root "src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj"
$testProj = Join-Path $root "tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj"

# ─────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────

function Get-Dotnet {
    $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $cmd) {
        Write-Host "ERROR: dotnet no encontrado en PATH." -ForegroundColor Red
        Write-Host "Instala el .NET SDK: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        exit 1
    }
    return $cmd.Source
}

function Assert-Projects {
    foreach ($p in @($srcProj, $testProj)) {
        if (-not (Test-Path $p)) {
            Write-Host "ERROR: proyecto no encontrado: $p" -ForegroundColor Red
            exit 1
        }
    }
}

function Invoke-Build([string]$config) {
    Assert-Projects
    # `dotnet build` y no `msbuild`: el MSBuild del .NET SDK es el único que
    # resuelve Microsoft.NET.Sdk en un equipo con VS/BuildTools sin el workload
    # de .NET. La arquitectura la fija Directory.Build.props.
    $dotnet = Get-Dotnet
    Write-Host ""
    Write-Host ">>> Build $config x86" -ForegroundColor Cyan
    # Compila el proyecto de tests: arrastra la referencia al de src.
    & $dotnet build $testProj -t:Restore,Build -c $config -p:Platform=x86 -v:minimal --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build falló (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "Build OK" -ForegroundColor Green
}

function Invoke-Test([string]$config, [string]$filter, [switch]$noBuild, [switch]$includeIntegration) {
    Assert-Projects
    $dotnet = Get-Dotnet

    if (-not $noBuild) {
        Invoke-Build $config
    }

    $dll = Join-Path $root "tests\Balansoft.AIAddin.Tests\bin\x86\$config\net48\Balansoft.AIAddin.Tests.dll"
    if (-not (Test-Path $dll)) {
        Write-Host "ERROR: DLL de tests no encontrada: $dll" -ForegroundColor Red
        exit 1
    }

    Write-Host ""
    Write-Host ">>> Tests ($config)" -ForegroundColor Cyan

    # `dotnet vstest` y no `dotnet test`: el SDK mete al medio su propio host de
    # test para net48 y eso no es lo verificado. El switch es /Settings:.
    if ($includeIntegration) {
        # Sin /Settings: ni filtro entran también los tests de NIM.
        $args = @("vstest", $dll, "/Platform:x86")
    }
    else {
        $args = @("vstest", $dll, "/Settings:$(Join-Path $root '.runsettings')", "/Platform:x86")
    }

    if (-not [string]::IsNullOrEmpty($filter)) {
        $args += "/TestCaseFilter:$filter"
    }

    & $dotnet @args
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests fallaron (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
    Write-Host "Tests OK" -ForegroundColor Green
}

function Invoke-Restore {
    Assert-Projects
    $dotnet = Get-Dotnet
    Write-Host ""
    Write-Host ">>> Restore" -ForegroundColor Cyan
    & $dotnet restore $testProj
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Restore OK" -ForegroundColor Green
}

function Invoke-Clean {
    Write-Host ""
    Write-Host ">>> Clean" -ForegroundColor Cyan

    $targets = @(
        (Join-Path $root "src\Balansoft.AIAddin.Access\bin"),
        (Join-Path $root "src\Balansoft.AIAddin.Access\obj"),
        (Join-Path $root "tests\Balansoft.AIAddin.Tests\bin"),
        (Join-Path $root "tests\Balansoft.AIAddin.Tests\obj"),
        (Join-Path $root "dist")
    )
    foreach ($t in $targets) {
        if (Test-Path $t) {
            Remove-Item $t -Recurse -Force
            Write-Host "  - $t" -ForegroundColor DarkGray
        }
    }

    Get-ChildItem -Path $root -Filter $zipPattern -File | ForEach-Object {
        Remove-Item $_.FullName -Force
        Write-Host "  - $($_.Name)" -ForegroundColor DarkGray
    }

    Write-Host "Clean OK" -ForegroundColor Green
}

function Invoke-Script([string]$scriptName, [string[]]$scriptArgs = @()) {
    $path = Join-Path $PSScriptRoot $scriptName
    if (-not (Test-Path $path)) {
        Write-Host "ERROR: script no encontrado: $path" -ForegroundColor Red
        exit 1
    }
    $psExe = if ($scriptName -match 'RegisterAddin\.ps1') {
        Join-Path $env:WINDIR "SysWOW64\WindowsPowerShell\v1.0\powershell.exe"
    } else {
        "powershell"
    }
    if (-not (Test-Path $psExe)) { $psExe = "powershell" }

    & $psExe -NoProfile -ExecutionPolicy Bypass -File $path @scriptArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Show-Status {
    Write-Host ""
    Write-Host "=== Estado del repo ===" -ForegroundColor Cyan
    Write-Host ""

    # Git
    $git = Get-Command git -ErrorAction SilentlyContinue
    if ($git) {
        Push-Location $root
        try {
            $branch = & git rev-parse --abbrev-ref HEAD 2>$null
            $commit = & git rev-parse --short HEAD 2>$null
            $dirty = & git status --porcelain 2>$null
            Write-Host "Git:" -ForegroundColor White
            Write-Host "  rama:   $branch"
            Write-Host "  commit: $commit"
            if ($dirty) {
                Write-Host "  estado: cambios sin commitear" -ForegroundColor Yellow
            }
            else {
                Write-Host "  estado: limpio" -ForegroundColor Green
            }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "Git: no disponible" -ForegroundColor DarkGray
    }

    # Build
    Write-Host ""
    Write-Host "Build:" -ForegroundColor White
    $dllDebug   = Join-Path $root "src\Balansoft.AIAddin.Access\bin\x86\Debug\net48\Balansoft.AIAddin.Access.dll"
    $dllRelease = Join-Path $root "src\Balansoft.AIAddin.Access\bin\x86\Release\net48\Balansoft.AIAddin.Access.dll"
    if (Test-Path $dllDebug) {
        $t = (Get-Item $dllDebug).LastWriteTime
        Write-Host "  Debug:   $t" -ForegroundColor Green
    }
    else {
        Write-Host "  Debug:   no compilado" -ForegroundColor DarkGray
    }
    if (Test-Path $dllRelease) {
        $t = (Get-Item $dllRelease).LastWriteTime
        Write-Host "  Release: $t" -ForegroundColor Green
    }
    else {
        Write-Host "  Release: no compilado" -ForegroundColor DarkGray
    }

    # ZIP
    Write-Host ""
    Write-Host "Packaging:" -ForegroundColor White
    $zips = Get-ChildItem -Path $root -Filter $zipPattern -File
    if ($zips) {
        foreach ($z in $zips) {
            Write-Host "  $($z.Name) ($([math]::Round($z.Length/1KB,1)) KB, $($z.LastWriteTime))" -ForegroundColor Green
        }
    }
    else {
        Write-Host "  sin ZIP generado" -ForegroundColor DarkGray
    }

    # Registro
    Write-Host ""
    Write-Host "Registro (HKCU):" -ForegroundColor White
    $key = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
    if (Test-Path $key) {
        $lb = (Get-ItemProperty -Path $key -Name "LoadBehavior" -ErrorAction SilentlyContinue).LoadBehavior
        Write-Host "  registrado, LoadBehavior=$lb" -ForegroundColor Green
    }
    else {
        Write-Host "  no registrado" -ForegroundColor DarkGray
    }

    # Datos de usuario
    Write-Host ""
    Write-Host "Datos de usuario (%APPDATA%\\Balansoft\\AIAddin):" -ForegroundColor White
    $appData = Join-Path $env:APPDATA "Balansoft\AIAddin"
    if (Test-Path $appData) {
        $apiKey = Join-Path $appData "apikey.bin"
        if (Test-Path $apiKey) { Write-Host "  apikey.bin: existe" -ForegroundColor Green }
        else { Write-Host "  apikey.bin: no existe" -ForegroundColor DarkGray }

        $histDir = Join-Path $appData "history"
        if (Test-Path $histDir) {
            $n = (Get-ChildItem $histDir -Filter "*.json" -File).Count
            Write-Host "  historial:  $n archivo(s)" -ForegroundColor Green
        }
        else {
            Write-Host "  historial:  vacío" -ForegroundColor DarkGray
        }

        $logDir = Join-Path $appData "logs"
        if (Test-Path $logDir) {
            $n = (Get-ChildItem $logDir -Filter "*.log" -File).Count
            Write-Host "  logs:       $n archivo(s)" -ForegroundColor Green
        }
        else {
            Write-Host "  logs:       vacío" -ForegroundColor DarkGray
        }
    }
    else {
        Write-Host "  (sin datos aún)" -ForegroundColor DarkGray
    }

    Write-Host ""
}

function Show-Help {
    Write-Host ""
    Write-Host "dev.ps1 - Atajos de desarrollo" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Uso:" -ForegroundColor White
    Write-Host "  .\dev.ps1 <comando> [opciones]"
    Write-Host ""
    Write-Host "Comandos:" -ForegroundColor White
    Write-Host "  build        Compila Debug x86"
    Write-Host "  release      Compila Release x86"
    Write-Host "  test         Ejecuta tests sin integración NIM"
    Write-Host "  testall      Ejecuta TODOS los tests (incluye integración)"
    Write-Host "  pack         Empaqueta la release (llama a Pack.ps1)"
    Write-Host "  reg          Registra el add-in (llama a RegisterAddin.ps1)"
    Write-Host "  unreg        Desregistra el add-in (llama a UnregisterAddin.ps1)"
    Write-Host "  restore      Restaura paquetes NuGet"
    Write-Host "  clean        Limpia bin/obj/dist y el ZIP"
    Write-Host "  all          build Release + test + pack"
    Write-Host "  status       Muestra el estado del repo"
    Write-Host "  help         Muestra esta ayuda"
    Write-Host ""
    Write-Host "Opciones:" -ForegroundColor White
    Write-Host "  -Configuration <Debug|Release>   Fuerza la configuración"
    Write-Host "  -Filter <expr>                   Filtro para los tests"
    Write-Host "  -NoBuild                         test/testall sin recompilar"
    Write-Host ""
    Write-Host "Ejemplos:" -ForegroundColor White
    Write-Host "  .\dev.ps1 build"
    Write-Host "  .\dev.ps1 release"
    Write-Host "  .\dev.ps1 test -Filter `"FullyQualifiedName~Bridge`""
    Write-Host "  .\dev.ps1 all"
    Write-Host "  .\dev.ps1 reg"
    Write-Host ""
}

# ─────────────────────────────────────────────────────────────
# Dispatch
# ─────────────────────────────────────────────────────────────

switch ($Command) {

    "help" {
        Show-Help
    }

    "build" {
        $cfg = if ($Configuration) { $Configuration } else { "Debug" }
        Invoke-Build $cfg
    }

    "release" {
        $cfg = if ($Configuration) { $Configuration } else { "Release" }
        Invoke-Build $cfg
    }

    "restore" {
        Invoke-Restore
    }

    "test" {
        $cfg = if ($Configuration) { $Configuration } else { "Debug" }
        Invoke-Test -config $cfg -filter $Filter -noBuild:$NoBuild
    }

    "testall" {
        $cfg = if ($Configuration) { $Configuration } else { "Debug" }
        if (-not $env:NIM_API_KEY) {
            Write-Host "AVISO: NIM_API_KEY no definido. Los tests de integración fallarán." -ForegroundColor Yellow
        }
        Invoke-Test -config $cfg -filter $Filter -noBuild:$NoBuild -includeIntegration
    }

    "clean" {
        Invoke-Clean
    }

    "reg" {
        $cfg = if ($Configuration) { $Configuration } else { "Release" }
        Invoke-Script "RegisterAddin.ps1" @("-Configuration", $cfg)
    }

    "unreg" {
        $cfg = if ($Configuration) { $Configuration } else { "Release" }
        Invoke-Script "UnregisterAddin.ps1" @("-Configuration", $cfg)
    }

    "pack" {
        $cfg = if ($Configuration) { $Configuration } else { "Release" }
        Invoke-Script "Pack.ps1" @("-Configuration", $cfg)
    }

    "all" {
        $cfg = if ($Configuration) { $Configuration } else { "Release" }
        Invoke-Build $cfg
        Invoke-Test -config $cfg -filter "" -noBuild
        Invoke-Script "Pack.ps1" @("-Configuration", $cfg)
    }

    "status" {
        Show-Status
    }
}

exit 0