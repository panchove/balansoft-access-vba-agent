#Requires -Version 5.1
<#
.SYNOPSIS
    Scaffolding del repositorio Balansoft Access VBA Agent (NIM Direct).

.DESCRIPTION
    Crea la estructura de carpetas y archivos base del proyecto:
      - .gitignore, .editorconfig, .runsettings
      - Directory.Build.props
      - Balansoft.AIAddin.sln (vía dotnet sln)
      - src/Balansoft.AIAddin.Access/ (csproj + AssemblyInfo.cs)
      - tests/Balansoft.AIAddin.Tests/ (csproj)
      - docs/manual-tests.md
      - README.md, CHANGELOG.md

    NO crea archivos .cs de implementación. Esos los produce el
    implementador (Nemotron 3 Ultra) al ejecutar los docs de diseño
    en docs/design/.

    El script es IDEMPOTENTE: se puede ejecutar N veces. Sin -Force,
    no sobrescribe archivos existentes.

.PARAMETER Root
    Raíz del repositorio. Por defecto, el directorio padre de este
    script (scripts\..).

.PARAMETER Force
    Si se especifica, sobrescribe archivos existentes con la versión
    del scaffold. Sin este flag, los archivos existentes se respetan.

.EXAMPLE
    PS> .\Scaffold.ps1
    Crea la estructura en el directorio padre de scripts\.

.EXAMPLE
    PS> .\Scaffold.ps1 -Root C:\dev\balansoft -Force
    Crea/sobrescribe la estructura en C:\dev\balansoft.

.NOTES
    Autor:     Big Pickle (arquitecto)
    Versión:   0.1.0
    Requiere:  PowerShell 5.1, .NET SDK (para 'dotnet sln'), .NET
               Framework 4.8 (para compilar).
    No requiere admin. No toca HKLM. No toca el registro.
#>
[CmdletBinding()]
param(
    [string]$Root = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
$script:CreatedCount = 0
$script:SkippedCount = 0
$script:DirCount = 0

# ─────────────────────────────────────────────────────────────
# Helpers
# ─────────────────────────────────────────────────────────────

function Write-Step([string]$text) {
    Write-Host ""
    Write-Host $text -ForegroundColor Yellow
}

function Ensure-Dir([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
        Write-Host "  + $relative/" -ForegroundColor Green
        $script:DirCount++
    }
    else {
        Write-Host "  = $relative/" -ForegroundColor DarkGray
    }
}

function Write-FileIfMissing([string]$relative, [string]$content) {
    $path = Join-Path $Root $relative
    if ((Test-Path $path) -and -not $Force) {
        Write-Host "  = $relative" -ForegroundColor DarkGray
        $script:SkippedCount++
        return
    }
    $dir = Split-Path $path -Parent
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    # UTF-8 sin BOM, terminadores CRLF
    $normalized = $content -replace "`r`n", "`n" -replace "`n", "`r`n"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($path, $normalized, $utf8NoBom)
    Write-Host "  + $relative" -ForegroundColor Green
    $script:CreatedCount++
}

function Ensure-GitKeep([string]$relativeDir) {
    $keep = Join-Path $Root (Join-Path $relativeDir ".gitkeep")
    if (-not (Test-Path $keep)) {
        New-Item -ItemType File -Path $keep -Force | Out-Null
        Write-Host "  + $relativeDir/.gitkeep" -ForegroundColor DarkGray
    }
}

# ─────────────────────────────────────────────────────────────
# Resolver raíz
# ─────────────────────────────────────────────────────────────

if ([string]::IsNullOrEmpty($Root)) {
    $Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
else {
    if (-not (Test-Path $Root)) {
        throw "La ruta raíz no existe: $Root"
    }
    $Root = (Resolve-Path $Root).Path
}

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Scaffolding — Balansoft Access VBA Agent (NIM Direct)   ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host "Raíz: $Root" -ForegroundColor Cyan
if ($Force) {
    Write-Host "Modo: -Force (sobrescribir existentes)" -ForegroundColor Magenta
}
else {
    Write-Host "Modo: respetar existentes (sin -Force)" -ForegroundColor Cyan
}

# ─────────────────────────────────────────────────────────────
# 1. Carpetas
# ─────────────────────────────────────────────────────────────

Write-Step "[1/6] Creando carpetas"

$dirs = @(
    "docs",
    "docs/design",
    "scripts",
    ".opencode",
    ".opencode/skills",
    "src",
    "src/Balansoft.AIAddin.Access",
    "src/Balansoft.AIAddin.Access/Properties",
    "src/Balansoft.AIAddin.Access/Core",
    "src/Balansoft.AIAddin.Access/Core/Protocol",
    "src/Balansoft.AIAddin.Access/UI",
    "src/Balansoft.AIAddin.Access/UI/Nim",
    "src/Balansoft.AIAddin.Access/VbeIntegration",
    "src/Balansoft.AIAddin.Access/AccessIntegration",
    "src/Balansoft.AIAddin.Access/Registration",
    "src/Balansoft.AIAddin.Access/Helpers",
    "src/Balansoft.AIAddin.Access/Helpers/Exceptions",
    "tests",
    "tests/Balansoft.AIAddin.Tests",
    "tests/Balansoft.AIAddin.Tests/Core",
    "tests/Balansoft.AIAddin.Tests/Helpers",
    "tests/Balansoft.AIAddin.Tests/UI",
    "tests/Balansoft.AIAddin.Tests/Registration",
    "tests/Balansoft.AIAddin.Tests/Fakes"
)
foreach ($d in $dirs) { Ensure-Dir $d }

# .gitkeep en carpetas que estarán vacías hasta que el implementador
# escriba el código.
$gitkeepDirs = @(
    "src/Balansoft.AIAddin.Access/Core",
    "src/Balansoft.AIAddin.Access/Core/Protocol",
    "src/Balansoft.AIAddin.Access/UI",
    "src/Balansoft.AIAddin.Access/UI/Nim",
    "src/Balansoft.AIAddin.Access/VbeIntegration",
    "src/Balansoft.AIAddin.Access/AccessIntegration",
    "src/Balansoft.AIAddin.Access/Registration",
    "src/Balansoft.AIAddin.Access/Helpers",
    "src/Balansoft.AIAddin.Access/Helpers/Exceptions",
    "tests/Balansoft.AIAddin.Tests/Core",
    "tests/Balansoft.AIAddin.Tests/Helpers",
    "tests/Balansoft.AIAddin.Tests/UI",
    "tests/Balansoft.AIAddin.Tests/Registration",
    "tests/Balansoft.AIAddin.Tests/Fakes"
)
foreach ($d in $gitkeepDirs) { Ensure-GitKeep $d }

# ─────────────────────────────────────────────────────────────
# 2. Archivos de configuración raíz
# ─────────────────────────────────────────────────────────────

Write-Step "[2/6] Archivos de configuración raíz"

Write-FileIfMissing ".gitignore" @'
# Build outputs
bin/
obj/
dist/
*.zip
*.nupkg
*.snupkg

# Visual Studio
.vs/
*.user
*.suo
*.userprefs

# ReSharper / Rider
.idea/
*.DotSettings.user

# Test results
TestResults/
*.trx
*.coverage
coverage*.xml
coverage*.json

# Logs locales
*.log

# version.txt de la raíz (se regenera con Pack.ps1)
version.txt

# Ficheros del sistema
Thumbs.db
Desktop.ini
$RECYCLE.BIN/

# Archivos temporales de test
.apikey-test.bin
*.json.tmp
'@

Write-FileIfMissing ".editorconfig" @'
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true
max_line_length = 120

[*.{md,markdown}]
trim_trailing_whitespace = false
max_line_length = off

[*.{cs,csx}]
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
csharp_new_line_before_else = true
csharp_new_line_before_catch = true
csharp_new_line_before_finally = true
csharp_prefer_braces = true:warning
csharp_style_namespace_declarations = file_scoped:suggestion
csharp_style_var_for_built_in_types = true:suggestion
csharp_style_var_when_type_is_apparent = true:suggestion
csharp_style_expression_bodied_methods = when_on_single_line:suggestion
csharp_style_expression_bodied_properties = when_on_single_line:suggestion
csharp_style_prefer_switch_expression = true:suggestion
dotnet_style_qualification_for_field = false:suggestion
dotnet_style_qualification_for_property = false:suggestion
dotnet_style_qualification_for_method = false:suggestion
dotnet_style_qualification_for_event = false:suggestion
dotnet_style_predefined_type_for_locals_parameters_members = true:suggestion
dotnet_style_predefined_type_for_member_access = true:suggestion
dotnet_style_readonly_field = true:warning

[*.{ps1,psm1,psd1}]
indent_size = 4
end_of_line = crlf

[*.{xml,csproj,props,targets,sln}]
indent_size = 2
end_of_line = crlf

[*.{json,jsonc}]
indent_size = 2

[*.yml]
indent_size = 2
end_of_line = lf
'@

Write-FileIfMissing ".runsettings" @'
<?xml version="1.0" encoding="utf-8"?>
<RunSettings>
  <RunConfiguration>
    <TargetFrameworkVersion>net48</TargetFrameworkVersion>
    <TestCaseFilter>Category!=Integration</TestCaseFilter>
    <MaxCpuCount>0</MaxCpuCount>
  </RunConfiguration>
  <MSTest>
    <Parallelize>
      <Workers>4</Workers>
      <Scope>MethodLevel</Scope>
    </Parallelize>
    <CaptureTraceOutput>false</CaptureTraceOutput>
  </MSTest>
  <DataCollectionRunSettings>
    <DataCollectors>
      <DataCollector friendlyName="Code Coverage" uri="datacollector://Microsoft/CodeCoverage/2.0">
        <Configuration>
          <CodeCoverage>
            <ModulePaths>
              <Include>
                <ModulePath>.*Balansoft\.AIAddin\.Access\.dll$</ModulePath>
              </Include>
            </ModulePaths>
            <ExcludeByAttribute>
              <Attribute>ExcludeFromCodeCoverage</Attribute>
              <Attribute>GeneratedCodeAttribute</Attribute>
            </ExcludeByAttribute>
          </CodeCoverage>
        </Configuration>
      </DataCollector>
    </DataCollectors>
  </DataCollectionRunSettings>
</RunSettings>
'@

Write-FileIfMissing "Directory.Build.props" @'
<Project>
  <PropertyGroup>
    <!-- Versión (fuente de verdad) -->
    <Version>0.1.0</Version>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
    <InformationalVersion>0.1.0</InformationalVersion>

    <!-- Plataforma y lenguaje -->
    <TargetFramework>net48</TargetFramework>
    <LangVersion>10</LangVersion>
    <Nullable>disable</Nullable>
    <PlatformTarget>x86</PlatformTarget>
    <Prefer32Bit>true</Prefer32Bit>

    <!-- Calidad -->
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- Metadatos -->
    <Company>Balansoft</Company>
    <Product>Balansoft Access VBA Agent (NIM)</Product>
    <Copyright>Copyright © Balansoft 2025</Copyright>
  </PropertyGroup>

  <!-- Commit como AssemblyMetadata, si la variable de entorno existe. -->
  <Target Name="AddCommitMetadata"
          BeforeTargets="GetAssemblyVersion"
          Condition="'$(GIT_COMMIT)' != ''">
    <ItemGroup>
      <AssemblyMetadata Include="Commit" Value="$(GIT_COMMIT)" />
    </ItemGroup>
  </Target>
</Project>
'@

# ─────────────────────────────────────────────────────────────
# 3. Proyectos
# ─────────────────────────────────────────────────────────────

Write-Step "[3/6] Proyectos"

Write-FileIfMissing "src/Balansoft.AIAddin.Access/Balansoft.AIAddin.Access.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Library</OutputType>
    <RootNamespace>Balansoft.AIAddin.Access</RootNamespace>
    <AssemblyName>Balansoft.AIAddin.Access</AssemblyName>
    <UseWindowsForms>true</UseWindowsForms>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <ComVisible>true</ComVisible>
    <EnableComHosting>false</EnableComHosting>
    <RegisterForComInterop>false</RegisterForComInterop>
    <!-- El registro COM lo hace regasm vía scripts/RegisterAddin.ps1,
         no el build. -->
  </PropertyGroup>

  <ItemGroup>
    <!-- Sin PIAs de Office. Todo late-bound vía dynamic aislado en Helpers. -->
    <Reference Include="System.Security" />   <!-- ProtectedData (DPAPI) -->
  </ItemGroup>

</Project>
'@

Write-FileIfMissing "src/Balansoft.AIAddin.Access/Properties/AssemblyInfo.cs" @'
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: ComVisible(true)]
[assembly: Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]

[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests")]
[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests.Integration")]
'@

Write-FileIfMissing "tests/Balansoft.AIAddin.Tests/Balansoft.AIAddin.Tests.csproj" @'
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Balansoft.AIAddin.Tests</RootNamespace>
    <AssemblyName>Balansoft.AIAddin.Tests</AssemblyName>
    <!-- x86 para poder cargar la DLL x86 si un test la referencia. -->
    <PlatformTarget>x86</PlatformTarget>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="MSTest.TestAdapter"      Version="3.2.2" />
    <PackageReference Include="MSTest.TestFramework"    Version="3.2.2" />
    <PackageReference Include="coverlet.collector"      Version="6.0.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj" />
  </ItemGroup>

</Project>
'@

# ─────────────────────────────────────────────────────────────
# 4. Solución (vía dotnet sln)
# ─────────────────────────────────────────────────────────────

Write-Step "[4/6] Solución"

$slnPath = Join-Path $Root "Balansoft.AIAddin.sln"
if ((-not (Test-Path $slnPath)) -or $Force) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Host "  ! dotnet no encontrado. Se omite la solución." -ForegroundColor Red
        Write-Host "    Instala el .NET SDK y ejecuta de nuevo el script." -ForegroundColor Red
        Write-Host "    Alternativa manual: abre el .sln en Visual Studio." -ForegroundColor Red
    }
    else {
        Push-Location $Root
        try {
            # Crear solución vacía (si no existe)
            if (-not (Test-Path $slnPath)) {
                & dotnet new sln -n Balansoft.AIAddin --force | Out-Null
                Write-Host "  + Balansoft.AIAddin.sln" -ForegroundColor Green
                $script:CreatedCount++
            }
            else {
                Write-Host "  = Balansoft.AIAddin.sln" -ForegroundColor DarkGray
                $script:SkippedCount++
            }

            # Añadir proyectos (dotnet sln add es idempotente; ignora duplicados)
            & dotnet sln Balansoft.AIAddin.sln add `
                "src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj" 2>&1 | Out-Null
            & dotnet sln Balansoft.AIAddin.sln add `
                "tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj" 2>&1 | Out-Null
        }
        finally {
            Pop-Location
        }
    }
}
else {
    Write-Host "  = Balansoft.AIAddin.sln" -ForegroundColor DarkGray
    $script:SkippedCount++
}

# ─────────────────────────────────────────────────────────────
# 5. Documentos raíz
# ─────────────────────────────────────────────────────────────

Write-Step "[5/6] Documentos"

Write-FileIfMissing "README.md" @'
# Balansoft Access VBA Agent (NIM Direct)

Complemento COM para Microsoft Access 2010+ (solo **x86**) que integra
**Nvidia NIM** directamente en el editor de VBA (VBE).

- **ProgID:** `Balansoft.AIAddin.Access`
- **CLSID:** `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`
- **Estado:** En desarrollo (0.x)
- **Versión:** 0.1.0

## Requisitos

- Windows 10 / 11
- .NET Framework 4.8
- Access 2010, 2016 o 365 (x86)
- PowerShell 5.1 (no requiere admin)
- API key de Nvidia NIM (https://build.nvidia.com/)

## Instalación

1. Descarga el ZIP de la release.
2. Descomprime en una carpeta local.
3. Abre **Windows PowerShell (x86)**.
4. Ejecuta:

   ```powershell
   cd <carpeta_descomprimida>
   .\RegisterAddin.ps1
   ```

5. Abre Access. En el VBE verás **Tools → NIM Terminal**.
6. Introduce tu API key la primera vez (se guarda cifrada con DPAPI).

## Desinstalación

```powershell
.\UnregisterAddin.ps1
```

## Uso

1. Abre el VBE (Alt+F11) en Access.
2. Menú **Tools → NIM Terminal**.
3. Escribe tu prompt en la caja inferior.
4. La terminal tiene contexto del módulo activo y de la selección.

## Arquitectura

Ver `AGENTS.md` y `.opencode/skills/` para la documentación interna.

```
Access (host COM) ──→ BalansoftConnect (IDTExtensibility2)
                          ├─→ BlsLogger / BlsVersion
                          ├─→ BlsSecureStore (DPAPI)
                          ├─→ BlsChatHistory (JSON)
                          ├─→ BlsVbeService (VBE automation)
                          ├─→ BlsAccessObjectService (read-only)
                          ├─→ BlsVbeBridgeServer (Named Pipe + JSON-RPC)
                          └─→ BlsNimClient (HTTP/2 SSE)
```

## Desarrollo

Ver `docs/design/` para los documentos de diseño y ADRs.

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Debug /p:Platform=x86 /v:minimal

# Tests (sin integración NIM)
dotnet test --settings .runsettings

# Tests de integración NIM (requiere NIM_API_KEY)
$env:NIM_API_KEY = "..."
dotnet test --filter "Category=Integration"

# Registro local
powershell -File scripts\RegisterAddin.ps1

# Packaging
powershell -File scripts\Pack.ps1
```

## Seguridad

- API key solo cifrada con DPAPI (`CurrentUser`).
- Nunca en claro (ni logs, ni JSON, ni registro).
- Named Pipe `CurrentUserOnly`.
- Registro en HKCU. Nunca requiere admin.

## Licencia

Propietario — Balansoft. Todos los derechos reservados.
'@

Write-FileIfMissing "CHANGELOG.md" @'
# Changelog

Todas las modificaciones notables del proyecto se documentan aquí.

El formato sigue [Keep a Changelog](https://keepachangelog.com/es/1.1.0/)
y el proyecto adhiere a [Semantic Versioning](https://semver.org/lang/es/).

## [Unreleased]

### Añadido
- Estructura inicial del repositorio (scaffolding).
- Documentos de diseño y ADRs en `docs/design/`.
- Skills de opencode en `.opencode/skills/`.

### Pendiente (MVP)
- Bridge JSON-RPC.
- Cliente NIM SSE.
- Terminal WinForms.
- Secure Store + Chat History.
- Registro + Packaging.
- Logger + Version.
- Add-in Lifecycle.

## [0.1.0] — 2025-01-01

### Añadido
- Primera versión de desarrollo. Sin binarios publicados.
'@

Write-FileIfMissing "docs/manual-tests.md" @'
# Checklist de pruebas manuales

Ejecutar antes de cada release. Firmar al final.

**Versión:** ______
**Commit:** ______
**Fecha:** ______
**Probado por:** ______

---

## Pre-requisitos

- [ ] Windows 10 o 11 actualizado.
- [ ] .NET Framework 4.8 instalado.
- [ ] Access 2010, 2016 o 365 (x86).
- [ ] Base `.accdb` de prueba con módulo, formulario, tabla y query.
- [ ] API key de Nvidia NIM válida.

## 1. Registro

- [ ] PowerShell (x86) → `scripts\RegisterAddin.ps1`.
- [ ] Termina con "Add-in registrado correctamente".
- [ ] Ejecutar de nuevo → idempotente.
- [ ] `HKCU\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access`
      tiene `LoadBehavior = 3`.
- [ ] PowerShell x64 → falla con mensaje claro.

## 2. Carga en Access

- [ ] Abrir Access con la base.
- [ ] VBE (Alt+F11) → Tools → NIM Terminal existe.
- [ ] Cerrar Access.
- [ ] Log en `%APPDATA%\Balansoft\AIAddin\logs\` con versión + commit.
- [ ] `LoadBehavior` sigue en 3 (no en 2).

## 3. Terminal básica

- [ ] Tools → NIM Terminal abre con owner = VBE.
- [ ] Estado inicial "Listo". Detener deshabilitado.
- [ ] Segunda apertura → foco, no duplica.

## 4. API key

- [ ] Primer prompt pide API key.
- [ ] Cancelar → no envía.
- [ ] Guardar → envía.
- [ ] Reabrir Access → no vuelve a pedir.
- [ ] `apikey.bin` no contiene la key en claro.

## 5. Streaming

- [ ] Prompt simple → respuesta en vivo.
- [ ] Estado cambia a "Pensando…" y luego "Listo".

## 6. Contexto VBE

- [ ] Selección de código → el prompt la incluye.

## 7. Tool call

- [ ] "Crea un módulo UtilsTest con Suma(a,b)" → módulo creado.

## 8. Cancelación

- [ ] Detener a mitad del stream → "Cancelado por el usuario".
- [ ] Escape equivalente.
- [ ] Detener sin turno → no-op.

## 9. Historial

- [ ] 3 prompts, cerrar terminal, reabrir → historial restaurado.

## 10. Errores reales

- [ ] API key inválida → error de auth (no "hecho").
- [ ] Borrar módulo inexistente → error con mensaje original.

## 11. Sin VBE (runtime)

- [ ] `.accde` → add-in carga, log `VBE no disponible`.

## 12. Desinstalación

- [ ] `scripts\UnregisterAddin.ps1` → clave eliminada.
- [ ] Access → add-in no carga.
- [ ] Idempotente.

## 13. Multi-versión

Repetir 2, 3, 5, 7 en Access 2010, 2016 y 365 (x86).

## 14. Máquina limpia

- [ ] Windows 10/11 + .NET 4.8 + Office x86.
- [ ] `RegisterAddin.ps1` sin admin.
- [ ] Repetir 2, 3, 5, 7.

---

## Firma

| Rol  | Nombre | Fecha | Firma |
|------|--------|-------|-------|
| QA   |        |       |       |
| Dev  |        |       |       |
'@

# ─────────────────────────────────────────────────────────────
# 6. Restaurar NuGet
# ─────────────────────────────────────────────────────────────

Write-Step "[6/6] Restaurando paquetes NuGet"

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet -and (Test-Path $slnPath)) {
    Push-Location $Root
    try {
        & dotnet restore Balansoft.AIAddin.sln 2>&1 | Out-Null
        Write-Host "  + Restore completado" -ForegroundColor Green
    }
    catch {
        Write-Host "  ! Restore falló: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    finally {
        Pop-Location
    }
}
else {
    Write-Host "  ! dotnet no disponible, se omite restore" -ForegroundColor Yellow
}

# ─────────────────────────────────────────────────────────────
# Resumen
# ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "╔══════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
Write-Host "║  Scaffolding completado                                  ║" -ForegroundColor Cyan
Write-Host "╚══════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
Write-Host ""
Write-Host "Carpetas creadas:     $script:DirCount"
Write-Host "Archivos creados:     $script:CreatedCount"
Write-Host "Archivos respetados:  $script:SkippedCount"
Write-Host ""
Write-Host "Siguientes pasos:" -ForegroundColor Cyan
Write-Host ""
Write-Host "  1. Copia los MDs a sus rutas:" -ForegroundColor White
Write-Host "       AGENTS.md                       -> ./AGENTS.md"
Write-Host "       docs/design/ADR-*.md            -> ./docs/design/"
Write-Host "       docs/design/2025*.md            -> ./docs/design/"
Write-Host "       .opencode/skills/NN-*.md        -> ./.opencode/skills/"
Write-Host ""
Write-Host "  2. Verifica el build:" -ForegroundColor White
Write-Host "       msbuild Balansoft.AIAddin.sln /p:Configuration=Debug /p:Platform=x86 /v:minimal"
Write-Host ""
Write-Host "  3. Verifica los tests (0 tests, verde):" -ForegroundColor White
Write-Host "       dotnet test --settings .runsettings"
Write-Host ""
Write-Host "  4. Inicia git:" -ForegroundColor White
Write-Host "       git init"
Write-Host "       git add ."
Write-Host "       git commit -m 'chore: scaffolding inicial'"
Write-Host ""
Write-Host "  5. Implementa los frentes en paralelo (ver docs/design/):" -ForegroundColor White
Write-Host "       Frente A: 20250101-vbe-bridge.md"
Write-Host "       Frente B: 20250102-nim-client.md"
Write-Host "       Frente C: 20250104-secure-store.md"
Write-Host "       Frente D: 20250106-registration-scripts.md + 20250107-logging-version.md"
Write-Host "       Luego:    20250103-nim-terminal.md + 20250108-addin-lifecycle.md"
Write-Host ""
