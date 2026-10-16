---
version: 1.0.0
date: 2025-01-06
status: approved
iso_standard: [29148, 42010, 12207]
aligns_to: [PRD.md, AGENTS.md, specs/009-registration.md]
tags: [design, architecture, packaging, registration, powershell, regasm]
---

# ADR-010 — Registration + Packaging (regasm HKCU + scripts)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-06

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

## Depende de
- `docs/design/20250107-logging-version.md` (BlsVersion usado en logs
  y en `version.txt`; ver nota en §10.2).

---

## 1. Contexto

El add-in debe:

1. **Registrarse sin admin** en HKCU (`regasm /codebase`).
2. **Cargar automáticamente** al arrancar Access (`LoadBehavior = 3`).
3. **Distribuirse** como ZIP con scripts de instalación.
4. **Verificar en runtime** que el registro sigue correcto (diagnóstico,
   no escritura).

Todo el registro vive bajo `HKCU\Software\Microsoft\Office\Access\Addins\`
y bajo las claves COM que `regasm` escribe. Nada en HKLM. Nunca admin.

Cubre:
- PRD §3.1 (registro HKCU, carga en Access x86).
- PRD §7 FR-06 (registro solo HKCU, LoadBehavior=3, scripts).
- PRD §11 criterio 1 (carga sin errores en Access 2010/2016/365 x86).

Y materializa:
- AGENTS.md regla 6 (`PlatformTarget=x86`).
- AGENTS.md regla 7 (`TreatWarningsAsErrors`).

---

## 2. Alcance

### Incluido
- `RegisterAddin.ps1` (idempotente, PS 5.1, verifica x86).
- `UnregisterAddin.ps1` (idempotente, tolerante a "no existe").
- `Pack.ps1` (build Release x86 + copia + zip).
- `BlsAddinRegistrar` (verificación en runtime).
- `BlsRegistryHelper` (lectura de HKCU).
- `Directory.Build.props` (versión, TFM, plataforma, warnings).
- `Properties/AssemblyInfo.cs` (solo ComVisible, Guid, InternalsVisibleTo).
- Verificación post-registro manual documentada.

### Excluido (fuera de MVP)
- Instalador MSI.
- Publicación en ClickOnce.
- Firma de la DLL.
- Registro en HKLM.
- Registro para todos los usuarios de la máquina.
- Actualización automática.
- Desinstalador gráfico.
- Soporte para Access x64.
- Registro en `HKCR` manual (regasm lo hace).
- GAC (`gacutil`).
- Registro de la Type Library en HKCU (regasm lo hace en HKCR bajo perfil).

---

## 3. Interfaces nuevas

### 3.1 `BlsAddinRegistrar` (Registration/)
Registro y verificación en runtime. Solo **lee** HKCU en producción; la
escritura la hacen los scripts.

```csharp
namespace Balansoft.AIAddin.Registration;

internal static class BlsAddinRegistrar
{
    private const string AddinKeyPath =
        @"Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access";

    // Verificación en runtime (solo lectura)
    public static bool IsRegistered();
    public static int GetLoadBehavior();

    // Escritura (usada por los scripts)
    public static void EnsureAddinKey();
    public static void RemoveAddinKey();
}
```

**Notas:**
- `IsRegistered` → comprueba que la clave existe y `LoadBehavior == 3`.
- `GetLoadBehavior` → devuelve el valor actual (para diagnóstico).
- `EnsureAddinKey` / `RemoveAddinKey` las invocan los scripts vía
  PowerShell; **no** se llaman en runtime normal.

### 3.2 `BlsRegistryHelper` (Registration/)
Wrapper mínimo de `Microsoft.Win32.Registry`. Inyectable para tests.

```csharp
internal interface IRegistryAccessor
{
    string? GetString(string subKey, string valueName);
    int? GetInt(string subKey, string valueName);
    bool KeyExists(string subKey);
}

internal sealed class BlsRegistryHelper : IRegistryAccessor
{
    private readonly RegistryKey _root;   // HKCU en prod; fake en tests

    public BlsRegistryHelper(RegistryKey root);
    // implementa IRegistryAccessor
}
```

**Notas:**
- `BlsAddinRegistrar` recibe `IRegistryAccessor` por constructor para
  testear sin tocar el registro real.
- En producción se inyecta `BlsRegistryHelper(Registry.CurrentUser)`.

### 3.3 `RegistrationInfo` (Registration/)
DTO de diagnóstico para el log de arranque.

```csharp
internal sealed record RegistrationInfo(
    bool IsRegistered,
    int LoadBehavior,
    string? AddinKeyPath);
```

Usado por `BlsAddinRegistrar.GetRegistrationInfo()` en el arranque para
loguear el estado (skill bls-logging-errors).

---

## 4. Invariantes afectados

| Fuente                         | Invariante                             | Cómo se cumple                             |
|--------------------------------|----------------------------------------|--------------------------------------------|
| AGENTS.md regla 6              | `PlatformTarget=x86`                   | `Directory.Build.props`                    |
| AGENTS.md regla 7              | Warnings as errors                     | `Directory.Build.props`                    |
| PRD FR-06                      | Solo HKCU                              | `BlsAddinRegistrar` escribe en `HKCU`      |
| PRD FR-06                      | LoadBehavior = 3                       | `EnsureAddinKey` y script                  |
| PRD FR-06                      | Scripts (des)registro                  | `RegisterAddin.ps1`, `UnregisterAddin.ps1` |
| PRD FR-06                      | Sin admin                              | No hay escritura en HKLM                   |
| skill bls-registration-scripts | regasm x86                             | `Framework\v4.0.30319\regasm.exe`          |
| skill bls-registration-scripts | Idempotencia                           | Scripts con `-Force` y tolerancia          |
| skill bls-registration-scripts | PS 5.1                                 | Sintaxis compatible                        |
| skill bls-registration-scripts | Verificar `LoadBehavior` tras arranque | Log de arranque con `GetRegistrationInfo`  |
| PRD §11.1                      | Carga sin errores                      | Verificado por checklist manual            |

---

## 5. Flujo del registro

### Instalación
```
1. Usuario descarga ZIP.
2. Descomprime en carpeta local.
3. Ejecuta PowerShell x86.
4. cd a la carpeta descomprimida.
5. .\RegisterAddin.ps1
6. Script:
   a. Verifica PowerShell x86 ([Environment]::Is64BitProcess == false)
   b. Localiza regasm x86
   c. Localiza la DLL (argumento o ruta por defecto)
   d. Ejecuta: regasm /codebase "<dll>"
   e. Verifica exit code 0
   f. Escribe HKCU\...\Access\Addins\Balansoft.AIAddin.Access
      - FriendlyName = "Balansoft Access VBA Agent (NIM)"
      - Description  = "Terminal NIM embebida en el VBE"
      - LoadBehavior = 3 (DWord)
      - CommandLineSafe = 0 (DWord)
   g. Muestra mensaje verde "Add-in registrado correctamente."
7. Abrir Access → el add-in carga.
```

### Desinstalación
```
1. Ejecutar .\UnregisterAddin.ps1 (PS x86).
2. Script:
   a. Verifica PS x86
   b. regasm /unregister "<dll>"
   c. Remove-Item HKCU\...\Addins\Balansoft.AIAddin.Access -Recurse -Force
      -ErrorAction SilentlyContinue
   d. Muestra mensaje amarillo "Add-in desregistrado."
3. Opcional: borrar %APPDATA%\Balansoft\AIAddin\ (a mano).
```

### Verificación en runtime (arranque del add-in)
```
1. En OnConnection, tras cargar el add-in:
   a. info = BlsAddinRegistrar.GetRegistrationInfo()
   b. Logger.Info("Registration", $"IsRegistered={info.IsRegistered}, " +
                  $"LoadBehavior={info.LoadBehavior}")
   c. if (!info.IsRegistered)
        Logger.Warn("Registration",
            "Add-in no registrado correctamente. Ejecuta RegisterAddin.ps1.")
2. Si Access degrada LoadBehavior a 2:
   - Significa que el add-in lanzó excepción en OnConnection.
   - El log tendrá la traza. Corregir y volver a registrar.
```

---

## 6. Contenido de la clave HKCU

```
HKCU\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access
├── (Default)        REG_SZ     "Balansoft.AIAddin.Access"  (creado por regasm)
├── FriendlyName     REG_SZ     "Balansoft Access VBA Agent (NIM)"
├── Description      REG_SZ     "Terminal NIM embebida en el VBE"
├── LoadBehavior     REG_DWORD  3
└── CommandLineSafe  REG_DWORD  0
```

Regasm además escribe (en HKCU\Software\Classes\CLSID y
`\WOW6432Node\CLSID` según arquitectura):

```
HKCU\Software\Classes\CLSID\{d31bf73e-dbe1-4739-84bc-2c974fcd3171}
├── (Default)                     REG_SZ   "Balansoft.AIAddin.Access"
├── InprocServer32
│   ├── (Default)                 REG_SZ   "mscoree.dll"
│   ├── ThreadingModel            REG_SZ   "Both"
│   ├── Class                       REG_SZ "Balansoft.AIAddin.Access.BalansoftConnect"
│   ├── Assembly                    REG_SZ "Balansoft.AIAddin.Access, Version=0.1.0.0, ..."
│   ├── RuntimeVersion              REG_SZ "v4.0.30319"
│   └── CodeBase                    REG_SZ "file:///C:/.../Balansoft.AIAddin.Access.dll"

HKCU\Software\Classes\Balansoft.AIAddin.Access
└── (Default)                      REG_SZ   "Balansoft.AIAddin.Access"
```

**No tocamos estas claves a mano.** Regasm las gestiona con `/codebase`.

---

## 7. Contenido de los scripts

### `scripts/RegisterAddin.ps1`

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DllPath       = ""
)

$ErrorActionPreference = "Stop"

# 1. Verificar x86
if ([Environment]::Is64BitProcess) {
    throw "Este script debe ejecutarse en PowerShell x86 (32-bit). " +
          "Abre 'Windows PowerShell (x86)' desde el menú Inicio."
}

# 2. Localizar regasm x86
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\regasm.exe"
if (-not (Test-Path $regasm)) {
    throw "regasm x86 no encontrado: $regasm"
}

# 3. Localizar DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $DllPath = Join-Path $PSScriptRoot `
        "..\src\Balansoft.AIAddin.Access\bin\$Configuration\Balansoft.AIAddin.Access.dll"
}
if (-not (Test-Path $DllPath)) {
    throw "DLL no encontrada: $DllPath. Compila primero (msbuild /p:Configuration=$Configuration /p:Platform=x86)."
}
$DllPath = (Resolve-Path $DllPath).Path

# 4. Desregistrar previamente (idempotente)
& $regasm /unregister $DllPath 2>$null | Out-Null

# 5. Registrar
& $regasm /codebase $DllPath
if ($LASTEXITCODE -ne 0) {
    throw "regasm falló (exit $LASTEXITCODE). Revisa los mensajes anteriores."
}

# 6. Clave Access Add-in
$key = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name "FriendlyName"    -Value "Balansoft Access VBA Agent (NIM)"
Set-ItemProperty -Path $key -Name "Description"     -Value "Terminal NIM embebida en el VBE"
Set-ItemProperty -Path $key -Name "LoadBehavior"    -Value 3 -Type DWord
Set-ItemProperty -Path $key -Name "CommandLineSafe" -Value 0 -Type DWord

Write-Host "Add-in registrado correctamente." -ForegroundColor Green
Write-Host "Abre Access y verifica Tools -> NIM Terminal en el VBE."
```

### `scripts/UnregisterAddin.ps1`

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DllPath       = ""
)

$ErrorActionPreference = "Continue"

if ([Environment]::Is64BitProcess) {
    throw "Este script debe ejecutarse en PowerShell x86 (32-bit)."
}

$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\regasm.exe"

if ([string]::IsNullOrEmpty($DllPath)) {
    $DllPath = Join-Path $PSScriptRoot `
        "..\src\Balansoft.AIAddin.Access\bin\$Configuration\Balansoft.AIAddin.Access.dll"
}

if (Test-Path $DllPath) {
    & $regasm /unregister $DllPath 2>$null | Out-Null
}

$key = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
if (Test-Path $key) {
    Remove-Item -Path $key -Recurse -Force
}

Write-Host "Add-in desregistrado." -ForegroundColor Yellow
```

### `scripts/Pack.ps1`

```powershell
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$OutputDir     = ""
)

$ErrorActionPreference = "Stop"

$root    = Resolve-Path (Join-Path $PSScriptRoot "..")
$distDir = if ($OutputDir) { $OutputDir } else { Join-Path $root "dist" }
$sln     = Join-Path $root "Balansoft.AIAddin.sln"

# 1. Build Release x86
& msbuild $sln /p:Configuration=$Configuration /p:Platform=x86 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build falló" }

# 2. Preparar carpeta
if (Test-Path $distDir) { Remove-Item $distDir -Recurse -Force }
New-Item -ItemType Directory -Path $distDir | Out-Null

# 3. Copiar artefactos
$binDir = Join-Path $root "src\Balansoft.AIAddin.Access\bin\$Configuration"
Copy-Item (Join-Path $binDir "Balansoft.AIAddin.Access.dll") $distDir
Copy-Item (Join-Path $PSScriptRoot "RegisterAddin.ps1")     $distDir
Copy-Item (Join-Path $PSScriptRoot "UnregisterAddin.ps1")   $distDir
if (Test-Path (Join-Path $root "README.md")) {
    Copy-Item (Join-Path $root "README.md") $distDir
}

# 4. version.txt (versión + commit)
$versionFile = Join-Path $root "version.txt"
$version = if (Test-Path $versionFile) {
    (Get-Content $versionFile -First 1).Trim()
} else { "0.1.0" }
$commit  = (& git rev-parse --short HEAD 2>$null)
if (-not $commit) { $commit = "unknown" }
"$version`n$commit" | Out-File (Join-Path $distDir "version.txt") -Encoding utf8

# 5. Zip
$zipName = "Balansoft.AIAddin.Access-$version-x86.zip"
$zipPath = Join-Path $root $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path "$distDir\*" -DestinationPath $zipPath

Write-Host "Paquete: $zipPath" -ForegroundColor Green
```

---

## 8. `Directory.Build.props`

```xml
<Project>
  <PropertyGroup>
    <!-- Versión -->
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

  <!-- Commit hash en AssemblyMetadata para BlsVersion -->
  <Target Name="AddCommitHash" BeforeTargets="GetAssemblyVersion" Condition="'$(GITHUB_SHA)' != ''">
    <ItemGroup>
      <AssemblyMetadata Include="Commit" Value="$(GITHUB_SHA)" />
    </ItemGroup>
  </Target>
</Project>
```

**Nota sobre el commit:** el `<Target>` de arriba es un esqueleto. En
local, `Pack.ps1` genera `version.txt` con `git rev-parse`. Si se quiere
inyectar el commit en el assembly, ver §10.3.

---

## 9. `Properties/AssemblyInfo.cs`

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: ComVisible(true)]
[assembly: Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]

[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests")]
[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests.Integration")]
```

**Nada más.** Sin `AssemblyVersion`, `FileVersion`, `Title`, `Company`
(el `Directory.Build.props` los cubre).

---

## 10. Decisiones internas (no requieren ADR)

### 10.1 Regasm x86 hardcodeado
`%WINDIR%\Microsoft.NET\Framework\v4.0.30319\regasm.exe` es estable en
todas las instalaciones de .NET Framework 4.8 en Windows 10/11. No se
intenta descubrir dinámicamente (evita complejidad y errores).

### 10.2 `BlsVersion` y `version.txt`
`BlsVersion` (doc #7) lee la versión del assembly. Los scripts usan
`version.txt` en la raíz del repo (generado por un script previo o
mantenido a mano) más `git rev-parse`. La fuente de verdad es
`Directory.Build.props`; `version.txt` es un caché para el packaging.

### 10.3 Commit hash: prioridad del `version.txt`
- **Local:** `Pack.ps1` lee `git rev-parse` y lo pone en `version.txt`.
- **CI:** se puede inyectar vía `<AssemblyMetadata>` en
  `Directory.Build.props` (ver §8). Para MVP, no es obligatorio.

`BlsVersion.Commit` intentará en orden:
1. `AssemblyMetadata("Commit")` si existe.
2. `version.txt` en el directorio del assembly (segunda línea).
3. `"unknown"`.

### 10.4 Desregistro previo en `RegisterAddin.ps1`
Antes de registrar, se desregistra (silencioso). Esto limpia entradas
antiguas si la DLL cambió de ruta. Es idempotente y evita estados raros
de regasm al re-registrar.

### 10.5 `CommandLineSafe = 0`
Significa que el add-in no se carga en modo "command line safe"
(Access `/safe`). Es lo correcto: no queremos cargar en modo seguro.

### 10.6 Sin registro de Type Library en HKCU
Regasm `/tlb` no se usa en el script. La Type Library solo es útil si
otro COM cliente quiere early binding; no es nuestro caso. Ahorra un
paso y evita errores de registro.

### 10.7 `BlsAddinRegistrar` en runtime solo lee
La escritura la hacen los scripts. En runtime, `EnsureAddinKey` **no**
se llama. Esto evita que un add-in roto por registro incorrecto intente
"arreglarse" y enmascare problemas de instalación.

### 10.8 Verificación de `LoadBehavior` en el log
En `OnConnection`, tras cargar el add-in, se llama
`GetRegistrationInfo()` y se loguea. Si el valor es `2` (en lugar de
`3`), significa que Access lo degradó por un fallo previo. El log lo
deja claro.

### 10.9 `Pack.ps1` sobrescribe el `dist`
Cada ejecución borra y recrea `dist/`. No se versiona `dist/`; el ZIP
tampoco. `.gitignore` incluye ambos.

### 10.10 `version.txt` en el ZIP, no en el repo
El ZIP incluye `version.txt` con versión + commit. El repo no lo
necesita (la versión está en `Directory.Build.props`).

### 10.11 Sin firma digital
Fuera de MVP. Un ZIP con DLL sin firmar puede disparar SmartScreen en
algunos equipos; se documenta en el README.

### 10.12 Sin actualizador
Fuera de MVP. La actualización es manual: desregistrar, reemplazar DLL,
registrar de nuevo.

---

## 11. Criterios de aceptación

### CA-01 — Registro correcto en HKCU
- [ ] Tras `RegisterAddin.ps1`, existe la clave
      `HKCU\...\Access\Addins\Balansoft.AIAddin.Access`.
- [ ] `FriendlyName`, `Description`, `LoadBehavior`, `CommandLineSafe`
      tienen los valores esperados.
- [ ] Regasm escribió `HKCU\Software\Classes\CLSID\{...}`.
- **Test manual:** checklist.
- **Test unitario:** `Registrar_EnsureAddinKey_EscribeClavesCorrectas`
  (con `IRegistryAccessor` fake).

### CA-02 — `LoadBehavior = 3`
- [ ] Tras el registro, `LoadBehavior` es `3` (DWord).
- **Tests:** `Registrar_GetLoadBehavior_Devuelve3TrasEnsure`,
  `Registrar_IsRegistered_TrueCuandoLoadBehavior3`.

### CA-03 — Idempotencia
- [ ] Ejecutar `RegisterAddin.ps1` N veces deja el mismo estado.
- [ ] Ejecutar `UnregisterAddin.ps1` N veces no falla.
- **Test manual:** checklist.

### CA-04 — Verificación de plataforma
- [ ] El script falla con mensaje claro si se ejecuta en PowerShell x64.
- **Test manual:** checklist.

### CA-05 — `UnregisterAddin.ps1` limpia
- [ ] Tras `UnregisterAddin.ps1`, la clave `HKCU\...\Addins\...` no
      existe.
- [ ] `regasm /unregister` fue invocado (exit code 0 o "no existe").
- **Test manual:** checklist.

### CA-06 — `Pack.ps1` produce ZIP
- [ ] `Pack.ps1` compila Release x86.
- [ ] El ZIP contiene `Balansoft.AIAddin.Access.dll`,
      `RegisterAddin.ps1`, `UnregisterAddin.ps1`, `version.txt`,
      `README.md` (si existe).
- [ ] `version.txt` incluye versión + commit.
- **Test manual:** checklist.

### CA-07 — Verificación en runtime
- [ ] `GetRegistrationInfo()` devuelve `IsRegistered=true` cuando el
      add-in está correctamente registrado.
- [ ] El log de arranque incluye la línea de registro.
- **Tests:** `Registrar_GetRegistrationInfo_EstadoCorrecto`,
  `Registrar_GetRegistrationInfo_NoRegistrado`.

### CA-08 — Sin HKLM
- [ ] Ningún script escribe en HKLM.
- [ ] `BlsAddinRegistrar` solo lee/escribe bajo `HKCU`.
- **Test unitario:** `Registrar_NuncaTocaHKLM` (con un fake que falle si
  se usa otra raíz).

### CA-09 — `Directory.Build.props` correcto
- [ ] `PlatformTarget == x86`.
- [ ] `TargetFramework == net48`.
- [ ] `LangVersion == 10`.
- [ ] `Nullable == disable`.
- [ ] `TreatWarningsAsErrors == true`.
- **Test manual:** inspección del `.props` + build.

### CA-10 — `AssemblyInfo.cs` mínimo
- [ ] Solo contiene `ComVisible`, `Guid`, `InternalsVisibleTo`.
- [ ] No contiene `AssemblyVersion`/`FileVersion`/`Title`.
- **Test manual:** inspección.

---

## 12. Estructura de archivos

```
repo/
├── Directory.Build.props                           (nuevo)
├── .editorconfig                                   (nuevo)
├── .gitignore                                      (nuevo)
├── Balansoft.AIAddin.sln                           (nuevo)
├── version.txt                                     (opcional, raíz)
├── scripts/
│   ├── RegisterAddin.ps1                           (nuevo)
│   ├── UnregisterAddin.ps1                         (nuevo)
│   └── Pack.ps1                                    (nuevo)
├── src/
│   └── Balansoft.AIAddin.Access/
│       ├── Balansoft.AIAddin.Access.csproj         (nuevo)
│       ├── Properties/
│       │   └── AssemblyInfo.cs                     (nuevo)
│       └── Registration/
│           ├── BlsAddinRegistrar.cs                (nuevo)
│           ├── BlsRegistryHelper.cs                (nuevo)
│           ├── IRegistryAccessor.cs                (nuevo, internal)
│           └── RegistrationInfo.cs                 (nuevo, internal)
└── tests/
    └── Balansoft.AIAddin.Tests/
        ├── Balansoft.AIAddin.Tests.csproj          (nuevo)
        └── Registration/
            ├── BlsAddinRegistrarTests.cs           (nuevo)
            └── Fakes/
                └── FakeRegistryAccessor.cs         (nuevo)
```

### `.gitignore` (mínimo)

```
bin/
obj/
dist/
*.zip
.vs/
*.user
*.suo
*.log
TestResults/
```

### `.editorconfig` (extracto)

```
root = true

[*]
charset = utf-8
end_of_line = crlf
insert_final_newline = true
indent_style = space
indent_size = 4
trim_trailing_whitespace = true

[*.{cs,csx}]
dotnet_sort_system_directives_first = true
csharp_new_line_before_open_brace = all
csharp_prefer_braces = true:warning
csharp_style_namespace_declarations = file_scoped:suggestion

[*.ps1]
indent_size = 4
```

---

## 13. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (reglas 6, 7).
2. `.opencode/skills/bls-project-context/SKILL.md`.
3. `.opencode/skills/bls-registration-scripts/SKILL.md` (este dominio).
4. `.opencode/skills/bls-logging-errors/SKILL.md` (log de arranque).
5. `.opencode/skills/bls-testing-strategy/SKILL.md` (fakes de registro).

Opcional:
- `.opencode/skills/bls-workflow-nemotron/SKILL.md` (para entender por qué los
  scripts no van en el assembly).

---

## 14. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Registrar"

# Registro local (PowerShell x86)
powershell -File scripts/RegisterAddin.ps1
powershell -File scripts/UnregisterAddin.ps1

# Packaging
powershell -File scripts/Pack.ps1
```

Criterio de éxito: build verde + tests verdes + registro/desregistro
idempotentes + ZIP generado.

---

## 15. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §11 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §13 están listados.
- [ ] `Directory.Build.props` aprobado (versión, TFM, plataforma).

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar tests de CA-01..CA-10 (los automatizables).
- [ ] No modificar interfaces del §3.
- [ ] Referenciar este doc en el commit:
      `feat: registro HKCU + scripts (refs design/20250106-registration-scripts.md)`.
- [ ] Incluir el checklist manual firmado para CA-03, CA-04, CA-05, CA-06,
      CA-09, CA-10.

---

## 16. Referencias

- `AGENTS.md` reglas 6, 7.
- `PRD.md` §3.1, §7 FR-06, §11.
- `.opencode/skills/bls-registration-scripts/SKILL.md`.
- `.opencode/skills/bls-logging-errors/SKILL.md`.
- `.opencode/skills/bls-testing-strategy/SKILL.md`.
- `.opencode/skills/bls-workflow-nemotron/SKILL.md`.
- `docs/design/20250107-logging-version.md` (BlsVersion usado en logs y
  version.txt).
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa el registro (paralelo a #1, #2, #4, #7)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. docs/design/20250106-registration-scripts.md (el diseño)
4. Los skills citados en §13 del diseño

Implementa la feature 'Registration + Packaging' exactamente como está
especificado.
Incluye:
- Directory.Build.props
- AssemblyInfo.cs mínimo
- BlsAddinRegistrar + BlsRegistryHelper + IRegistryAccessor
- RegisterAddin.ps1, UnregisterAddin.ps1, Pack.ps1
- .gitignore, .editorconfig básicos

No cambies interfaces del §3. No añadas features del §2 excluidas.
Entrega diff completo + tests + comando de verificación del §14."
```

### Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/bls-workflow-nemotron/SKILL.md (checklist de rechazo)
3. docs/design/20250106-registration-scripts.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill bls-workflow-nemotron.
Presta especial atención a:
- Nunca tocar HKLM (regla 6).
- PlatformTarget=x86 (regla 6).
- TreatWarningsAsErrors=true (regla 7).
- LoadBehavior=3 (FR-06).
- Scripts en PS 5.1.
- AssemblyInfo mínimo (sin AssemblyVersion/FileVersion).
- Idempotencia de los scripts.

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño

| # | Doc                                | Estado            | Depende de |
|---|------------------------------------|-------------------|------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado       | —          |
| 2 | `20250102-nim-client.md`           | ✅ Entregado       | —          |
| 3 | `20250103-nim-terminal.md`         | ✅ Entregado       | #1, #2, #4 |
| 4 | `20250104-secure-store.md`         | ✅ Entregado       | ADR-001    |
| 5 | `20250105-chat-history.md`         | ⚠️ Fusionado en #4 | —          |
| 6 | `20250106-registration-scripts.md` | ✅ Entregado       | #7         |
| 7 | `20250107-logging-version.md`      | Pendiente         | —          |
| 8 | `20250108-addin-lifecycle.md`      | Pendiente         | Todos      |

**Ruta crítica hacia el MVP:**
```
#1 bridge ─┐
           ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client ─┘       ↑
                   │
#4 store ──────────┤ (alimenta #2 y #3)
#6 reg ────────────┤ (alimenta #8)
#7 log ────────────┘ (transversal)
```

**Paralelización inmediata (4 frentes):**
- Frente A: #1 bridge
- Frente B: #2 client
- Frente C: #4 store + history
- Frente D: #6 reg (hecho) + #7 log (pendiente)

Solo queda **#7 (logging-version)** como dependencia de #6 y como
transversal para todos, y **#8 (lifecycle)** que integra todo.
