# Scripts — Balansoft Access VBA Agent (NIM Direct)

Scripts de operación del repositorio. Todos son **PowerShell 5.1**, no
requieren administrador, y no tocan HKLM ni el registro fuera de HKCU.

---

## Índice

| Script                                       | Cuándo se ejecuta          | Requiere x86 | Requiere admin | Toca registro |
|----------------------------------------------|----------------------------|:------------:|:--------------:|:-------------:|
| [`Scaffold.ps1`](#scaffoldps1)               | Una vez, al montar el repo |      No      |       No       |      No       |
| [`CopyDocs.ps1`](#copydocsps1)               | Una vez, tras Scaffold     |      No      |       No       |      No       |
| [`dev.ps1`](#devps1)                         | Día a día (atajos)         |    No (*)    |       No       |    No (**)    |
| [`RegisterAddin.ps1`](#registeraddinps1)     | Tras build; al instalar    |    **Sí**    |       No       |   Sí (HKCU)   |
| [`UnregisterAddin.ps1`](#unregisteraddinps1) | Al desinstalar             |    **Sí**    |       No       |   Sí (HKCU)   |
| [`Pack.ps1`](#packps1)                       | Antes de publicar release  |      No      |       No       |      No       |

(*) `dev.ps1 reg` / `dev.ps1 unreg` requieren PowerShell x86 (delegan
en los scripts x86).
(**) `dev.ps1 reg` / `dev.ps1 unreg` escriben en HKCU a través de los
scripts delegados.

---

## Flujo típico

```
┌───────────────────────────────────────────────────────────────┐
│  Primera vez (montar el repo)                                 │
│                                                               │
│   1. git clone <repo>                                         │
│   2. scripts\Scaffold.ps1        ← crea estructura            │
│   3. colocar los 23 MDs en inbox\                             │
│   4. scripts\CopyDocs.ps1        ← copia los MDs              │
│   5. scripts\dev.ps1 build       ← compila Debug              │
│   6. scripts\dev.ps1 test        ← tests sin integración      │
│   7. scripts\dev.ps1 reg         ← registrar en HKCU (x86)    │
│   8. abrir Access, VBE, Tools → NIM Terminal                  │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  Día a día (desarrollo)                                       │
│                                                               │
│   scripts\dev.ps1 build                                       │
│   scripts\dev.ps1 test                                        │
│   scripts\dev.ps1 status                                      │
│                                                               │
│   (atajos cortos, todo con dev.ps1)                           │
└───────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────┐
│  Publicar una release                                         │
│                                                               │
│   1. scripts\dev.ps1 test                                     │
│   2. scripts\dev.ps1 pack                                     │
│   3. subir el ZIP de la raíz al release                       │
└───────────────────────────────────────────────────────────────┘
```

---

## `Scaffold.ps1`

**Qué hace.** Crea la estructura inicial del repositorio: carpetas,
archivos de configuración (`.gitignore`, `.editorconfig`,
`.runsettings`, `Directory.Build.props`), los dos proyectos
(`.csproj` + `AssemblyInfo.cs`), la solución (`Balansoft.AIAddin.sln`),
y los documentos raíz (`README.md`, `CHANGELOG.md`,
`docs/MANUAL-TESTS.md`). Ejecuta `dotnet restore` al final.

**Qué NO hace.** No crea archivos `.cs` de implementación (los produce
el implementador a partir de `docs/design/`). No toca el registro. No
ejecuta `git init`.

**Cuándo se ejecuta.** Una sola vez, al montar el repo. También si
quieres recrear la estructura en otra máquina o si borras algo por
error.

**Requisitos.**
- PowerShell 5.1.
- .NET SDK (`dotnet`) para generar la solución y restaurar NuGet. Si
  no está, el script avisa y sigue (la solución habrá que crearla
  manualmente).
- Sin admin. Sin acceso al registro.

**Uso.**
```powershell
# Raíz por defecto: el directorio padre de scripts\
powershell -ExecutionPolicy Bypass -File scripts\Scaffold.ps1

# Raíz explícita
powershell -ExecutionPolicy Bypass -File scripts\Scaffold.ps1 -Root C:\dev\balansoft

# Sobrescribir archivos existentes
powershell -ExecutionPolicy Bypass -File scripts\Scaffold.ps1 -Force
```

**Parámetros.**

| Parámetro | Tipo     | Default      | Descripción                     |
|-----------|----------|--------------|---------------------------------|
| `-Root`   | `string` | `scripts\..` | Raíz del repositorio            |
| `-Force`  | `switch` | off          | Sobrescribe archivos existentes |

**Garantías.**
- **Idempotente.** Ejecutarlo N veces no rompe nada.
- **UTF-8 sin BOM** en todos los archivos de texto.
- **CRLF** en terminadores de línea.
- **Falla con mensaje claro** si la raíz no existe.
- **Degrada con `WARN`** si `dotnet` no está disponible.

**Salida esperada.**
```
╔══════════════════════════════════════════════════════════╗
║  Scaffolding completado                                  ║
╚══════════════════════════════════════════════════════════╝

Carpetas creadas:     24
Archivos creados:     12
Archivos respetados:  0
```

**Verificación post-ejecución.**
```powershell
scripts\dev.ps1 build
scripts\dev.ps1 test
```

---

## `CopyDocs.ps1`

**Qué hace.** Copia los 23 MDs (1 AGENTS + 4 ADRs + 7 diseños + 11
skills) desde `inbox/` a sus rutas finales. Verifica que no falta
ninguno y que las referencias cruzadas apuntan a archivos existentes.
Limpia `inbox/` al terminar (con `-KeepInbox`, no).

**Cuándo se ejecuta.** Una vez, tras `Scaffold.ps1` y tras colocar los
MDs en `inbox/`. También con `-Force` si actualizas algún MD y quieres
re-sincronizar.

**Requisitos.**
- PowerShell 5.1.
- Los 23 MDs presentes en `inbox/`.
- Sin admin.

**Uso.**
```powershell
# Copia sin sobrescribir; borra inbox/ al terminar
powershell -ExecutionPolicy Bypass -File scripts\CopyDocs.ps1

# Conservar inbox/ tras la copia
powershell -ExecutionPolicy Bypass -File scripts\CopyDocs.ps1 -KeepInbox

# Sobrescribir MDs existentes
powershell -ExecutionPolicy Bypass -File scripts\CopyDocs.ps1 -Force

# Inbox y raíz explícitos
powershell -ExecutionPolicy Bypass -File scripts\CopyDocs.ps1 `
    -Root C:\dev\balansoft -Inbox C:\temp\mds -Force
```

**Parámetros.**

| Parámetro    | Tipo     | Default        | Descripción                |
|--------------|----------|----------------|----------------------------|
| `-Root`      | `string` | `scripts\..`   | Raíz del repo              |
| `-Inbox`     | `string` | `<raíz>\inbox` | Carpeta con los MDs        |
| `-Force`     | `switch` | off            | Sobrescribe MDs existentes |
| `-KeepInbox` | `switch` | off            | No borra `inbox/`          |

**Manifiesto (23 archivos).**

| Categoría | Cantidad | Destino             |
|-----------|:--------:|---------------------|
| AGENTS    |    1     | `AGENTS.md` (raíz)  |
| ADRs      |    4     | `docs/design/`      |
| Diseños   |    7     | `docs/design/`      |
| Skills    |    11    | `.opencode/skills/` |

**Códigos de salida.**

| Exit | Significado                |
|:----:|----------------------------|
|  0   | Todo OK                    |
|  1   | Faltan archivos en inbox   |
|  2   | Falló alguna copia         |
|  3   | Faltan archivos en destino |
|  4   | Referencias cruzadas rotas |

**Garantías.**
- **Idempotente.** Sin `-Force`, respeta lo existente.
- **No borra el inbox** si algo falla.
- **Reporta** qué falta, qué se copió, qué se respetó.

---

## `dev.ps1`

**Qué hace.** Atajo local con subcomandos. Envuelve `msbuild`,
`dotnet test` y los demás scripts en un único punto de entrada.

**No es un orquestador.** No sabe de agentes ni de modelos. No hay DAG
ni scheduling. Es azúcar sintáctico para no escribir 3–4 comandos cada
vez.

**Cuándo se ejecuta.** Día a día, durante el desarrollo.

**Requisitos.**
- PowerShell 5.1 (x64 sirve para `build/test/pack/status`).
- MSBuild en PATH (para `build` / `release` / `all`).
- .NET SDK (para `test` / `restore`).
- Para `reg` / `unreg`: PowerShell **x86** (delegan en los scripts x86).

**Comandos.**

| Comando   | Qué hace                                           | Config  |
|-----------|----------------------------------------------------|---------|
| `build`   | Compila Debug x86                                  | Debug   |
| `release` | Compila Release x86                                | Release |
| `test`    | Tests sin integración NIM                          | Debug   |
| `testall` | Tests incluidos integración NIM                    | Debug   |
| `pack`    | Empaqueta release (delega en `Pack.ps1`)           | Release |
| `reg`     | Registra el add-in (delega en `RegisterAddin.ps1`) | Release |
| `unreg`   | Desregistra (delega en `UnregisterAddin.ps1`)      | Release |
| `restore` | Restaura paquetes NuGet                            | —       |
| `clean`   | Limpia `bin/obj/dist/*.zip`                        | —       |
| `all`     | `build` Release + `test` + `pack`                  | Release |
| `status`  | Estado del repo (git, build, registro, datos)      | —       |
| `help`    | Ayuda                                              | —       |

**Opciones.**

| Opción           | Aplica a                               | Descripción                    |
|------------------|----------------------------------------|--------------------------------|
| `-Configuration` | build, release, test, pack, reg, unreg | Fuerza Debug/Release           |
| `-Filter`        | test, testall                          | Filtro `dotnet test --filter`  |
| `-NoBuild`       | test, testall                          | No recompilar antes de testear |

**Uso.**
```powershell
.\scripts\dev.ps1 build
.\scripts\dev.ps1 release
.\scripts\dev.ps1 test
.\scripts\dev.ps1 test -Filter "FullyQualifiedName~Bridge"
.\scripts\dev.ps1 test -NoBuild

$env:NIM_API_KEY = "..."
.\scripts\dev.ps1 testall

.\scripts\dev.ps1 reg
.\scripts\dev.ps1 unreg
.\scripts\dev.ps1 pack
.\scripts\dev.ps1 all
.\scripts\dev.ps1 status
.\scripts\dev.ps1 clean
.\scripts\dev.ps1 help
```

**Ejemplo de `status`.**
```
=== Estado del repo ===

Git:
  rama:   main
  commit: a3f1c8e
  estado: limpio

Build:
  Debug:   01/15/2025 10:23:41
  Release: 01/15/2025 10:25:12

Packaging:
  Balansoft.AIAddin.Access-0.1.0-x86.zip (48.3 KB, 01/15/2025 10:25:20)

Registro (HKCU):
  registrado, LoadBehavior=3

Datos de usuario (%APPDATA%\Balansoft\AIAddin):
  apikey.bin: existe
  historial:  2 archivo(s)
  logs:       5 archivo(s)
```

**Nota sobre `reg` / `unreg` desde x64.**
`dev.ps1` lanza los scripts con `powershell -File`, lo que crea un
subproceso de la **misma arquitectura** que el padre. Si abres
PowerShell x64 y ejecutas `.\dev.ps1 reg`, fallará con "debe
ejecutarse en PowerShell x86".

Workaround:
```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" `
    -File .\scripts\dev.ps1 reg
```

---

## `RegisterAddin.ps1`

**Qué hace.** Registra el add-in COM en HKCU:
1. Verifica que se ejecuta en **PowerShell x86**.
2. Localiza `regasm` x86.
3. Desregistra previamente (idempotente).
4. `regasm /codebase <dll>`.
5. Escribe la clave Access Add-in en HKCU con `LoadBehavior = 3`.
6. Verifica el `LoadBehavior` tras escribirlo.

**Cuándo se ejecuta.**
- Tras un build exitoso, antes de abrir Access.
- Cuando cambia la ruta de la DLL.
- Cuando `LoadBehavior` se degrada a 2.

**Requisitos.**
- **PowerShell x86** (Windows PowerShell (x86) del menú Inicio).
- .NET Framework 4.8.
- DLL compilada.
- Sin admin.

**Uso.**
```powershell
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1 -Configuration Debug
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1 -DllPath "C:\ruta\custom\dll"
```

**Parámetros.**

| Parámetro        | Tipo     | Default                                     | Descripción         |
|------------------|----------|---------------------------------------------|---------------------|
| `-Configuration` | `string` | `Release`                                   | `Debug` o `Release` |
| `-DllPath`       | `string` | `bin\{Config}\Balansoft.AIAddin.Access.dll` | Ruta a la DLL       |

**Códigos de salida.**

| Exit | Significado          |
|:----:|----------------------|
|  0   | OK                   |
|  1   | No es PowerShell x86 |
|  2   | regasm no encontrado |
|  3   | DLL no encontrada    |
|  4   | regasm falló         |

**Garantías.**
- No toca HKLM.
- No requiere admin.
- Idempotente.
- Falla con mensaje claro.

**Verificación post-ejecución.**
```powershell
reg query "HKCU\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access"
# LoadBehavior debe ser 3
```

---

## `UnregisterAddin.ps1`

**Qué hace.** Desregistra el add-in:
1. Verifica PowerShell x86.
2. `regasm /unregister <dll>` (silencioso, tolerante a DLL ausente).
3. Elimina la clave HKCU del add-in.

**Cuándo se ejecuta.** Al desinstalar. Antes de re-registrar (aunque
`RegisterAddin.ps1` ya lo hace internamente).

**Requisitos.**
- **PowerShell x86.**
- Sin admin.

**Uso.**
```powershell
powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1 -Configuration Debug
```

**Parámetros.**

| Parámetro        | Tipo     | Default          |
|------------------|----------|------------------|
| `-Configuration` | `string` | `Release`        |
| `-DllPath`       | `string` | ruta por defecto |

**Códigos de salida.**

| Exit | Significado          |
|:----:|----------------------|
|  0   | OK                   |
|  1   | No es PowerShell x86 |

**Garantías.**
- No toca HKLM.
- Idempotente.
- Tolerante a DLL y clave ausentes.
- **Conserva** `%APPDATA%\Balansoft\AIAddin\` (API key, historial, logs).
  Para purgar todo:
  ```powershell
  Remove-Item "$env:APPDATA\Balansoft\AIAddin" -Recurse -Force
  ```

---

## `Pack.ps1`

**Qué hace.**
1. Compila Release x86.
2. Prepara `dist/` con: DLL, `RegisterAddin.ps1`,
   `UnregisterAddin.ps1`, `README.md`, `version.txt`.
3. `version.txt` con versión + commit (`git rev-parse`).
4. ZIP: `Balansoft.AIAddin.Access-{version}-x86.zip`.

**Cuándo se ejecuta.** Antes de publicar una release.

**Requisitos.**
- MSBuild en PATH.
- Git en PATH (para el commit).
- PowerShell 5.1.
- Sin admin.

**Uso.**
```powershell
powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1
powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1 -OutputDir C:\releases
```

**Parámetros.**

| Parámetro        | Tipo     | Default   |
|------------------|----------|-----------|
| `-Configuration` | `string` | `Release` |
| `-OutputDir`     | `string` | `dist/`   |

**Códigos de salida.**

| Exit | Significado                  |
|:----:|------------------------------|
|  0   | OK                           |
|  1   | Solución no encontrada       |
|  2   | msbuild no encontrado        |
|  3   | Build falló                  |
|  4   | DLL no encontrada tras build |

**Contenido del ZIP.**
```
Balansoft.AIAddin.Access-0.1.0-x86.zip
├── Balansoft.AIAddin.Access.dll
├── RegisterAddin.ps1
├── UnregisterAddin.ps1
├── README.md
└── version.txt
```

---

## Convenciones de los scripts

1. `#Requires -Version 5.1`. Sin sintaxis de PS7.
2. `[CmdletBinding()]` + `param(...)`.
3. `$ErrorActionPreference = "Stop"` (o `"Continue"` en los tolerantes).
4. Mensajes en español, con colores.
5. **No tocan HKLM. Nunca.**
6. **No requieren admin. Nunca.**
7. Rutas relativas a `$PSScriptRoot`.
8. Idempotentes.
9. Falla con mensaje claro y código de salida tipado.
10. UTF-8 sin BOM, CRLF.

---

## Resolución de problemas

### `Script no se puede cargar porque la ejecución de scripts está deshabilitada`

```powershell
powershell -ExecutionPolicy Bypass -File scripts\Scaffold.ps1
```

O cambia la política para el usuario actual:
```powershell
Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned
```

### `Este script debe ejecutarse en PowerShell x86`

`RegisterAddin.ps1` y `UnregisterAddin.ps1` requieren x86. Abre
**Windows PowerShell (x86)** del menú Inicio.

Alternativa desde la consola actual:
```powershell
& "$env:WINDIR\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" `
    -File scripts\RegisterAddin.ps1
```

### `regasm no encontrado`

Instala .NET Framework 4.8:
https://dotnet.microsoft.com/download/dotnet-framework/net48

Verifica que existe:
`%WINDIR%\Microsoft.NET\Framework\v4.0.30319\regasm.exe`

### `msbuild no encontrado`

`Pack.ps1` y `dev.ps1 build` necesitan MSBuild. Instala **Visual
Studio Build Tools** (gratis) o añade MSBuild al PATH.

Ruta típica:
`C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin`

Verifica:
```powershell
msbuild -version
```

### `dotnet no encontrado`

`Scaffold.ps1`, `CopyDocs.ps1` y `dev.ps1 test` lo usan. Instala el
.NET SDK:
https://dotnet.microsoft.com/download

### `LoadBehavior = 2` tras abrir Access

Significa que el add-in lanzó excepción en `OnConnection` o
`OnStartupComplete`. Mira el log:

```powershell
Get-Content "$env:APPDATA\Balansoft\AIAddin\logs\balansoft-$(Get-Date -Format yyyyMMdd).log" -Tail 50
```

Luego re-registra:
```powershell
.\scripts\dev.ps1 reg
```

---

## Referencias

- `AGENTS.md` — reglas duras.
- `docs/design/ADR-010-REGISTRATION-SCRIPTS.md` — diseño de
  `RegisterAddin.ps1`, `UnregisterAddin.ps1`, `Pack.ps1`.
- `docs/design/ADR-011-LOGGING-VERSION.md` — relación con
  `version.txt`.
- `docs/MANUAL-TESTS.md` — checklist pre-release.
- `PRD.md` §7 FR-06 — requisito de scripts de (des)registro.
- `.github/workflows/ci.yml` — CI de build + test.
- `.github/PULL_REQUEST_TEMPLATE.md` — checklist de PR.
