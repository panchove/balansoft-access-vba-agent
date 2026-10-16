---
name: bls-registration-scripts
description: Registro COM HKCU, regasm x86, scripts PowerShell 5.1 y empaquetado
---

# SKILL 07 — Registro HKCU + empaquetado

> **Referencia ISO/IEC**: 12207 (Release & Deployment) + 25010 (Maintainability)
> **Área**: registration | hkcu | scripts | packaging
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `Registration/BlsAddinRegistrar.cs`, `Registration/BlsRegistryHelper.cs`,
> `Properties/AssemblyInfo.cs`, los scripts `RegisterAddin.ps1`,
> `UnregisterAddin.ps1`, `Pack.ps1`, o cualquier cosa relacionada con el
> registro COM, LoadBehavior o distribución.

---

## Contexto

El add-in debe registrarse **sin privilegios de administrador** y cargar
automáticamente al abrir Access. Esto obliga a:

- Registro COM en **HKCU** (no HKLM).
- `regasm /codebase` (no `gacutil`, no instalador MSI).
- `LoadBehavior = 3` para carga al inicio + conexión.
- Arquitectura **x86** exclusivamente (Access x86).

El registro es **per-user**. Dos usuarios de la misma máquina tienen
registros independientes.

---

## Registro COM

### Herramienta
`regasm.exe` **x86**:

```
%WINDIR%\Microsoft.NET\Framework\v4.0.30319\regasm.exe
```

**No** usar `%WINDIR%\Microsoft.NET\Framework64\...` (rompería Access x86).

### Comando
```powershell
& $regasmX86 /codebase /tlb:"$dllDir\Balansoft.AIAddin.Access.tlb" $dllPath
```

- `/codebase`: registra la ruta del ensamblado en el registro (necesario
  porque la DLL no está en GAC).
- `/tlb`: genera la Type Library (opcional pero útil para diagnóstico).
- Si `$dllPath` no existe → abortar con mensaje claro.

### Clave de registro del add-in
Regasm escribe la mayor parte del registro COM (CLSID, InprocServer32,
ProgID, etc.). Además, **nosotros** escribimos la clave específica de
Access Add-in:

```
HKCU\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access
```

Valores:

| Nombre            | Tipo        | Valor                              |
|-------------------|-------------|------------------------------------|
| `FriendlyName`    | `REG_SZ`    | `Balansoft Access VBA Agent (NIM)` |
| `Description`     | `REG_SZ`    | `Terminal NIM embebida en el VBE`  |
| `LoadBehavior`    | `REG_DWORD` | `3` (cargar al inicio, conectar)   |
| `CommandLineSafe` | `REG_DWORD` | `0`                                |

### LoadBehavior — valores
| Valor | Significado                                     |
|-------|-------------------------------------------------|
| 0     | Desconectado                                    |
| 1     | Conectado (cargado)                             |
| 2     | Cargar al inicio, **no** conectar               |
| 3     | **Cargar al inicio y conectar** ← nuestro caso  |
| 8     | Cargar bajo demanda                             |
| 9     | Cargar al inicio y conectar (con UI de usuario) |
| 16    | Cargar al inicio y conectar (primera vez)       |

Access degrada `LoadBehavior` a `2` si el add-in **falla al cargar**
(excepción en `OnConnection`). Esto se debe verificar tras un arranque
real: si vale `2` cuando debería valer `3`, hay un bug de arranque.

### Desregistro
```powershell
& $regasmX86 /unregister $dllPath
Remove-Item "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access" -Recurse -Force -ErrorAction SilentlyContinue
```

El `Remove-Item` con `-ErrorAction SilentlyContinue` lo hace idempotente.

---

## `BlsAddinRegistrar` (runtime)

`BlsAddinRegistrar` es la **verificación** en runtime de que el registro
sigue correcto, no la escritura:

```csharp
internal static class BlsAddinRegistrar
{
    public static void EnsureAddinKey()
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access");

        key.SetValue("FriendlyName", "Balansoft Access VBA Agent (NIM)");
        key.SetValue("Description",  "Terminal NIM embebida en el VBE");
        key.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
        key.SetValue("CommandLineSafe", 0, RegistryValueKind.DWord);
    }

    public static int GetLoadBehavior()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access");
        return (int)(key?.GetValue("LoadBehavior") ?? 0);
    }
}
```

`EnsureAddinKey` se llama **desde el script de registro**, no en cada
arranque. En runtime solo se usa `GetLoadBehavior` para diagnóstico.

---

## Scripts PowerShell

Todos los scripts apuntan a **PowerShell 5.1** (baseline Windows 10).
No usar sintaxis PS7 (`??`, `-Parallel`, etc.).

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
    throw "Este script debe ejecutarse en PowerShell x86 (32-bit)."
}

# 2. Localizar regasm x86
$regasm = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\regasm.exe"
if (-not (Test-Path $regasm)) { throw "regasm x86 no encontrado: $regasm" }

# 3. Localizar DLL
if ([string]::IsNullOrEmpty($DllPath)) {
    $DllPath = Join-Path $PSScriptRoot "..\src\Balansoft.AIAddin.Access\bin\$Configuration\Balansoft.AIAddin.Access.dll"
}
$DllPath = (Resolve-Path $DllPath).Path
if (-not (Test-Path $DllPath)) { throw "DLL no encontrada: $DllPath" }

# 4. Regasm
& $regasm /codebase $DllPath
if ($LASTEXITCODE -ne 0) { throw "regasm falló (exit $LASTEXITCODE)" }

# 5. Clave Access Add-in
$key = "HKCU:\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name "FriendlyName"    -Value "Balansoft Access VBA Agent (NIM)"
Set-ItemProperty -Path $key -Name "Description"     -Value "Terminal NIM embebida en el VBE"
Set-ItemProperty -Path $key -Name "LoadBehavior"    -Value 3 -Type DWord
Set-ItemProperty -Path $key -Name "CommandLineSafe" -Value 0 -Type DWord

Write-Host "Add-in registrado correctamente." -ForegroundColor Green
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
    $DllPath = Join-Path $PSScriptRoot "..\src\Balansoft.AIAddin.Access\bin\$Configuration\Balansoft.AIAddin.Access.dll"
}

if (Test-Path $DllPath) {
    & $regasm /unregister $DllPath | Out-Null
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
    [string]$OutputDir     = "$PSScriptRoot\..\dist"
)

$ErrorActionPreference = "Stop"

# 1. Build Release x86
$sln = Join-Path $PSScriptRoot "..\Balansoft.AIAddin.sln"
& msbuild $sln /p:Configuration=$Configuration /p:Platform=x86 /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Build falló" }

# 2. Preparar carpeta
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir | Out-Null

# 3. Copiar artefactos
$binDir = Join-Path $PSScriptRoot "..\src\Balansoft.AIAddin.Access\bin\$Configuration"
Copy-Item (Join-Path $binDir "Balansoft.AIAddin.Access.dll") $OutputDir
Copy-Item (Join-Path $PSScriptRoot "RegisterAddin.ps1")     $OutputDir
Copy-Item (Join-Path $PSScriptRoot "UnregisterAddin.ps1")   $OutputDir
Copy-Item (Join-Path $PSScriptRoot "..\README.md")           $OutputDir -ErrorAction SilentlyContinue

# 4. version.txt con versión + commit
$version = (Get-Content (Join-Path $PSScriptRoot "..\Directory.Build.props") | Select-String "Version").ToString()
$commit  = (& git rev-parse --short HEAD) 2>$null
"$version`n$commit" | Out-File (Join-Path $OutputDir "version.txt") -Encoding utf8

# 5. Zip
$zip = Join-Path $PSScriptRoot "..\Balansoft.AIAddin.Access-$Configuration-x86.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$OutputDir\*" -DestinationPath $zip

Write-Host "Paquete: $zip" -ForegroundColor Green
```

---

## `Directory.Build.props`

Centraliza versión, target framework, plataforma y settings comunes:

```xml
<Project>
  <PropertyGroup>
    <Version>0.1.0</Version>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>

    <TargetFramework>net48</TargetFramework>
    <LangVersion>10</LangVersion>
    <Nullable>disable</Nullable>
    <PlatformTarget>x86</PlatformTarget>
    <Prefer32Bit>true</Prefer32Bit>

    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest</AnalysisLevel>
  </PropertyGroup>
</Project>
```

---

## `Properties/AssemblyInfo.cs`

**Solo** lo imprescindible (el resto está en `Directory.Build.props`):

```csharp
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: ComVisible(true)]
[assembly: Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]

[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests")]
[assembly: InternalsVisibleTo("Balansoft.AIAddin.Tests.Integration")]
```

**No** poner `AssemblyVersion`/`FileVersion` aquí (van en props).

---

## Reglas duras

1. **NUNCA** tocar HKLM. Todo el registro vive en HKCU.
2. **NUNCA** requerir admin. Si algo falla por permisos, es un bug.
3. **NUNCA** usar el regasm x64 (rompe Access x86).
4. **NUNCA** registrar en `HKCU\Software\Classes` manualmente; regasm lo
   hace por nosotros.
5. **NUNCA** usar `gacutil` (no hay GAC).
6. Los scripts deben ser **idempotentes**: ejecutarlos N veces deja el
   mismo estado.
7. Los scripts deben funcionar en **PowerShell 5.1**.
8. `Pack.ps1` siempre compila **Release x86** antes de empaquetar.
9. `version.txt` incluye **versión + commit** (para trazabilidad).

---

## Verificación post-registro

Tras `RegisterAddin.ps1`:

1. Abrir Access.
2. Comprobar `HKCU\...\Addins\Balansoft.AIAddin.Access\LoadBehavior` == 3.
3. Comprobar que el menú Tools del VBE tiene "NIM Terminal".
4. Comprobar log en `%APPDATA%\Balansoft\AIAddin\logs\` con línea
   `Versión X, commit Y, OS ..., Access ... x86`.

Si `LoadBehavior` se degrada a **2** tras el arranque → el add-in lanzó
excepción en `OnConnection`. Buscar en el log.

---

## Prohibiciones absolutas

| Anti-patrón                             | Por qué                    |
|-----------------------------------------|----------------------------|
| Registro en HKLM                        | Requiere admin, viola PRD  |
| `gacutil /i`                            | No usamos GAC              |
| regasm x64                              | Rompe Access x86           |
| Escribir `HKCU\Software\Classes` a mano | Duplica lo que hace regasm |
| Scripts con sintaxis PowerShell 7       | Baseline PS 5.1            |
| `LoadBehavior = 2` por defecto          | No conecta al inicio       |
| Ignorar `$LASTEXITCODE` de regasm       | Falla silenciosa           |
| `Pack.ps1` sin build previo             | Empaqueta binarios viejos  |
| `Pack.ps1` en Debug                     | Release siempre            |

---

## Checklist antes de commitear cambios en registro/scripts

- [ ] ¿Regasm apunta a `Framework\v4.0.30319\regasm.exe` (x86)?
- [ ] ¿El script verifica `[Environment]::Is64BitProcess == false`?
- [ ] ¿Se escribe `LoadBehavior = 3` (DWord)?
- [ ] ¿La clave vive en `HKCU\...\Access\Addins\...`?
- [ ] ¿El desregistro es idempotente (no falla si no existe)?
- [ ] ¿`Pack.ps1` compila Release x86 antes de copiar?
- [ ] ¿`version.txt` incluye versión + commit?
- [ ] ¿`Directory.Build.props` centraliza versión/TFM/plataforma?
- [ ] ¿`AssemblyInfo.cs` solo tiene `ComVisible`, `Guid`,
      `InternalsVisibleTo`?
- [ ] ¿Los scripts corren en PowerShell 5.1?
- [ ] ¿Nunca se toca HKLM ni se requiere admin?

---

## Referencias

- `AGENTS.md` regla 6 (`PlatformTarget=x86`), 7 (warnings as errors).
- PRD §3.1, §7 FR-06, §9 (capa `Registration/`).
- `.opencode/skills/bls-logging-errors/SKILL.md` (línea de versión+commit en el log).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (verificación de `LoadBehavior` como
  test de integración manual).

## Referencias Cruzadas

Las siguientes skills están directamente relacionadas y deben consultarse según el área de trabajo:

- [.opencode/skills/bls-project-context/SKILL.md](.opencode/skills/bls-project-context/SKILL.md) — Skill 00: Contexto y visión del proyecto
- [.opencode/skills/bls-com-late-binding/SKILL.md](.opencode/skills/bls-com-late-binding/SKILL.md) — Skill 01: COM late-binding sin PIAs
- [.opencode/skills/bls-vbe-automation/SKILL.md](.opencode/skills/bls-vbe-automation/SKILL.md) — Skill 02: Automatización VBE
- [.opencode/skills/bls-nim-client-streaming/SKILL.md](.opencode/skills/bls-nim-client-streaming/SKILL.md) — Skill 03: Cliente NIM streaming
- [.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md](.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md) — Skill 04: Bridge JSON-RPC sobre Named Pipe
- [.opencode/skills/bls-secure-store-dpapi/SKILL.md](.opencode/skills/bls-secure-store-dpapi/SKILL.md) — Skill 05: DPAPI y almacenamiento seguro
- [.opencode/skills/bls-winforms-threading/SKILL.md](.opencode/skills/bls-winforms-threading/SKILL.md) — Skill 06: WinForms y threading
- [.opencode/skills/bls-registration-scripts/SKILL.md](.opencode/skills/bls-registration-scripts/SKILL.md) — Skill 07: Registro HKCU + scripts
- [.opencode/skills/bls-logging-errors/SKILL.md](.opencode/skills/bls-logging-errors/SKILL.md) — Skill 08: Logging y propagación de errores
- [.opencode/skills/bls-testing-strategy/SKILL.md](.opencode/skills/bls-testing-strategy/SKILL.md) — Skill 09: Estrategia de tests
- [.opencode/skills/bls-workflow-nemotron/SKILL.md](.opencode/skills/bls-workflow-nemotron/SKILL.md) — Skill 10: Workflow Nemotron 3 Ultra
- [.opencode/agents/bls-iso.md](.opencode/agents/bls-iso.md) — Agente de cumplimiento y gobernanza ISO/IEC (42001, 27001, 25010, 29148, 12207)
