---
version: 1.0.0
date: 2026-10-15
status: borrador
iso_standard: [29148, 12207]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-010-REGISTRATION-SCRIPTS.md]
tags: [spec, registration, packaging, deployment, powershell]
---

# 009 — Registro y despliegue

**Norma de referencia:** ISO/IEC 29148:2018 (Requirements Engineering) / ISO/IEC 12207:2017 (Deployment)  
**Estado:** `borrador`
**PRD:** FR-06
**ADRs:** ADR-010 (registro y empaquetado)
**Spec de diseño:** `docs/design/ADR-010-REGISTRATION-SCRIPTS.md`
**Tests:** 0 (scripts de PowerShell)

---

## Contexto

FR-06 pide un registro sin privilegios de administrador, que funcione en
PowerShell x86 y que deje el add-in visible en Access → Complementos con
`LoadBehavior = 3`.

Los scripts existen (`RegisterAddin.ps1`, `UnregisterAddin.ps1`, `Pack.ps1`) y
la estrategia está bien pensada: `HKCU` + `regasm x86`, sin admin. Pero están
**rotos** y, sobre todo, no se pueden probar porque el add-in no existe todavía
(spec 001).

---

## Alcance

**Entra:**

- Registro COM con `regasm` x86 en `HKCU`.
- Clave de add-in de Access con `LoadBehavior = 3`.
- Desregistro idempotente.
- Empaquetado de la release en un ZIP distribuible.

**No entra:**

- Instalación por GPO o MSI (fuera del MVP; se distribuye el ZIP).
- Registro por máquina (`HKLM`), que requeriría admin.
- Actualización automática.

---

## Requisitos

### R001 — Solo `HKCU`

Ni `regasm` ni la clave de Access tocan `HKLM`. Registro por usuario: un
instalador sin administrador es un requisito explícito del PRD, y además
evita que el DLL quede bloqueado para otros usuarios del equipo.

### R002 — Arquitectura coincidente (x86 o x64)

El script acepta `-Architecture x86|x64` (default `x86`) y **verifica** que el
proceso PowerShell coincida con esa arquitectura: si no, aborta con el mensaje
de abrir la consola de la arquitectura correcta. Registrar con un `regasm` de
arquitectura distinta a la del add-in produce una entrada COM en la vista de
registro equivocada: Access no lo ve y no hay mensaje de error que lo explique.

> **Actualización (2026-10-16):** alineado con el modelo CPU-agnóstico (x86 y
> x64). La redacción original exigía PowerShell x86 y abortaba ante x64.

### R003 — Idempotencia

`RegisterAddin.ps1` desregistra antes de registrar (`/unregister` silencioso).
Registrar dos veces no debe fallar ni dejar claves duplicadas.

### R004 — Verificación final

Tras escribir la clave, el script **vuelve a leer** `LoadBehavior` y lo
compara con 3. Un registro que escribe pero que Access no ve no se detecta de
otra forma.

### R005 — Empaquetado

El ZIP de distribución contiene la DLL, `RegisterAddin.ps1`,
`UnregisterAddin.ps1`, `README.md` y `version.txt` con versión y commit.
`dist/` y `*.zip` están en `.gitignore`.

### R006 — Rutas correctas

Los scripts resuelven la DLL por ruta: `src\Balansoft.AIAddin.Access\bin\x86\<Config>\net48\Balansoft.AIAddin.Access.dll`. No usan la solución; compilan los `.csproj` con `dotnet build -p:Platform=x86`, que garantiza la ruta `bin\x86\<Config>\net48\`.

---

## Criterios de aceptación

```
Dado PowerShell x86 y una DLL compilada
Cuando se ejecuta RegisterAddin.ps1
Entonces la CLSID queda registrada en HKCU
Y HKLM no se modifica
Y la clave Access tiene LoadBehavior = 3

Dado el add-in ya registrado
Cuando se ejecuta RegisterAddin.ps1 otra vez
Entonces termina con éxito sin duplicar claves

Dado PowerShell x64
Cuando se ejecuta RegisterAddin.ps1 -Architecture x86
Entonces aborta con código 1
Y explica que hay que usar la consola de la arquitectura indicada
Y no escribe nada en el registro

Dado un directorio de distribución
Cuando se ejecuta Pack.ps1
Entonces el ZIP contiene DLL, Register, Unregister, README y version.txt
Y version.txt tiene versión y commit

Dado la solución real `Balansoft.AIAddin.slnx`
Cuando se ejecuta cualquier script
Entonces no falla por buscar la solución (los scripts usan `dotnet build` sobre los `.csproj`)
```

---

## Tests obligatorios

Ninguno. No hay framework de tests para PowerShell en el repositorio.

| Test | Cubre |
|---|---|
| `Register_X86_RegistraClsidEnHKCU` | R001 |
| `Register_ArquitecturaIncorrecta_AbortaConCodigo1` | R002 |
| `Register_DosVeces_NoDuplicaClaves` | R003 |
| `Register_VerificaLoadBehaviorAlFinal` | R004 |
| `Register_NoTocaHKLM` | R001 |
| `Pack_ZipContieneLosCincoArchivos` | R005 |
| `Pack_VersionTxtIncluyeVersionYCommit` | R005 |
| `Scripts_ResuelvenSolucionSlnx` | R006 |

Los tres primeros son de integración y requieren un PowerShell de la misma
arquitectura que `-Architecture`; los de
`Pack` pueden correr en x86 o x64.

---

## Desviaciones y riesgos

- **`Balansoft.AIAddin.sln` no existe.** La solución real es
  `Balansoft.AIAddin.slnx`. **Ya no se usa**: `Pack.ps1`, `RegisterAddin.ps1`,
  `UnregisterAddin.ps1` y `dev.ps1` compilan los `.csproj` directamente con
  `dotnet build -p:Platform=x86`, que produce la ruta correcta
  `bin\x86\<Config>\net48\`.
- **`Directory.Build.props` creado** (versión 0.1.0, `TreatWarningsAsErrors`,
  CPU-agnóstico —sin `PlatformTarget`/`Prefer32Bit` forzados—, `LangVersion=10`,
  `Deterministic`).
  `Pack.ps1` lo lee y `version.txt` sale con `0.1.0` (más commit SHA en CI).
- **`Pack.ps1` compila el proyecto de tests con `dotnet build -c Release
  -p:Platform=x86`** y copia desde `bin\x86\Release\net48\`. **Verificado**:
  `scripts\Pack.ps1` ejecuta de principio a fin y produce el ZIP con 4 entradas.
- **`RegisterAddin.ps1` / `UnregisterAddin.ps1`** apuntan a
  `bin\x86\$Configuration\net48\Balansoft.AIAddin.Access.dll`. Mensaje de ayuda
  actualizado al comando real (`dotnet build ...`).
- **`dev.ps1` reescrito**: usa `dotnet build` / `dotnet vstest` sobre los
  `.csproj`. Build y tests 99/99 verificado en local.
- **`Scaffold.ps1`** tiene 4 errores de parser (líneas 762-775) y genera
  comandos obsoletos (`msbuild .sln`, `dotnet test --settings`). **No se usa**
  en el build ni en la CI; es un generador de repositorio nuevo.
- **CI arreglada** (`.github/workflows/ci.yml`):
  - Compila los `.csproj` (Debug y Release x86) con `dotnet build`.
  - Ejecuta `dotnet vstest` con `.runsettings` sobre net48 x86 (99/99).
  - Publica el `.trx` de `TestResults/`.
  - Gates automáticos: build, tests, 39 documentos obligatorios, sin PIAs,
    sin `dynamic` fuera de `Helpers/BlsComHelper.cs`, sin `Thread.Sleep` en
    tests, tests de ADR implementados presentes, referencias cruzadas.
  - En `push` a `main` ejecuta `Pack.ps1` y sube el ZIP como artefacto.
- **Referencias cruzadas corregidas**: el gate de CI escanea `specs/`,
  `AGENTS.md`, `MEMORY.md`, `docs/` y `.opencode/skills/`. Los nombres de
  skills ahora resuelven (`00-` a `10-`).
- **Ningún script se ha ejecutado en Access real** (punto de entrada COM
  pendiente, spec 001). Los scripts son sintácticamente correctos, compilan y
  empaquetan, pero la verificación de `LoadBehavior = 3` requiere que el add-in
  cargue.