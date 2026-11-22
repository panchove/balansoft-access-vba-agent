# AGENTS.md — Balansoft Access VBA Agent (NIM Direct)

COM Add-in para Microsoft Access 2010+ (x86) que integra NVIDIA NIM en el VBE.
Stack: C# 10, .NET Framework 4.8 (net48), WinForms, sin PIAs, enlace tardío vía `dynamic`.
ProgID: `Balansoft.AIAddin.Access` | CLSID: `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`.

> Estado: el add-in carga en Access con ciclo de vida COM completo (spec 001). El bridge JSON-RPC, el cliente NIM, el almacén DPAPI, la supervision humana (F2), la auditoría JSON con hash encadenado (F3), la idempotencia con resiliencia (F4) y la privacidad/cadena de suministro (F5) están implementados con 305 tests verdes. Los 12 riesgos R-01…R-12 están cerrados.

## Reglas duras (CI las verifica y bloquea el merge)

1. **CPU-agnóstico (x86 y x64):** pasar `-p:PlatformTarget=x86|x64` al build. La arquitectura del add-in debe coincidir con la de Access instalada. No forzar `PlatformTarget` ni `Prefer32Bit` en los archivos de proyecto.
2. **Sin PIAs de Office:** nunca `Microsoft.Office.Interop.*`.
3. **Sin ROT:** nunca `Marshal.GetActiveObject` ni Running Object Table.
4. **`dynamic` solo en `Helpers/BlsComHelper.cs`** (CI lo comprueba en `src/`).
5. **Ningún `Thread.Sleep` en `tests/`** (CI lo comprueba).
6. **`TreatWarningsAsErrors=true`** — 0 warnings bloquean el build.
7. **API key solo en DPAPI** (`BlsSecureStore`, `CurrentUser`). Nunca en logs, excepciones, JSON, registry ni disco.
8. **Errores COM/NIM se propagan** con categoría + mensaje original. Prohibido éxito falso ni `catch (Exception) { }`.
9. **Nullable=annotations** (no `disable`).
10. **Todo acceso a VBE/Access desde hilos no-UI** pasa por marshaling explícito al hilo de UI (`IUiScheduler`).
11. **Supervision humana obligatoria (F2/ADR-015):** ninguna tool `Medium`/`High` se ejecuta sin aprobacion UI registrada; el kill switch (`BlsSafetyGate`) se comprueba antes de cada turno y tool call; un rechazo NUNCA invoca el bridge ni reporta exito falso. Fase 1 (auditoria 2026-11-22): el fail-safe, el HITL y el tope de 8 iteraciones tambien se refuerzan en `JsonRpcDispatcher`, y el pipe solo concede acceso al SID del usuario actual (`BlsVbeBridgeServer.BuildPipeSecurity`).
12. **PII y supply chain (F5/ADR-018):** el scrubber se aplica siempre en `NimHttpRequestBuilder.BuildJson`; el único literal de modelo en `src/` está en `Helpers/BlsModelCatalog.cs`; la entropía DPAPI es derivada y el protector fijo solo se usa para leer. CI lo verifica en el gate 8f.

## Gobernanza de IA (ISO/IEC 42001 · 25 Pilares)

- **Política de IA:** `docs/governance/AI-POLICY.md`. **Riesgos:** `docs/governance/AI-RISK-REGISTER.md`.
- **AI-BOM:** `docs/governance/AI-BOM.md`. **Amenazas:** `docs/governance/OWASP-AGENTIC-THREAT-MODEL.md`.
- **Divulgación:** la terminal muestra `AiDisclosure` de forma persistente (EU AI Act Art. 50).
- **Agente único:** solo `bls-architect` está activo; la auditoría ISO se ejecuta como gates de CI,
  no como agentes que revisan. `agents_index` debe contener exactamente un agente.
- **Supervisión humana (F2, ADR-015):** `delete_module`, `execute_sql`, `migrate_*` y cualquier
  tool `Medium`/`High` exigen aprobación UI con `payload_sha256`; el kill switch bloquea
  nuevas acciones sin intervención del modelo. F3 cierra R-04/R-05 (logs JSON
  auditados y trazabilidad). F4 cierra R-06 (idempotencia por clave derivada
  del turno, caché TTL/FIFO) y R-09 (full jitter + circuit breaker). Siguen
  abiertos. F5 cierra R-03 (`BlsPiiScrubber` + `BlsPiiConsentGate`), R-07
  (`BlsModelCatalog` y hash del prompt en el AI-BOM), R-10 (cross-refs de
  código + índice de specs) y R-11 (entropía DPAPI derivada con migración
  transparente). No quedan R-* abiertos.
- **Trailer de commits con asistencia de IA:** `AI-Assistant: OpenCode Big Pickle`.
  Este trailer **sustituye** el `Co-Authored-By: Claude Code <noreply@anthropic.com>`
  de la configuración global: atribuir la autoría a otro modelo violaría el Pilar 5.

## Comandos canónicos

Compilar los `.csproj` directamente, **no** la solución `.slnx` (no propaga `Platform` a proyectos SDK-style). Pasar tanto `-p:Platform=x86|x64` (carpeta de salida) como `-p:PlatformTarget=x86|x64` (encabezado PE) según la arquitectura de Access instalada:

```powershell
# Build Debug x86
dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86 -p:PlatformTarget=x86 -v:minimal --nologo

# Build Debug x64
dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x64 -p:PlatformTarget=x64 -v:minimal --nologo

# Build tests
dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86 -p:PlatformTarget=x86

# Tests unitarios (sin integración NIM; .runsettings filtra TestCategory=Integration)
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86
```

Registro / desregistro / empaquetado requieren **PowerShell** (x86 o x64 según la arquitectura de Access instalada). El script detecta la arquitectura y usa la carpeta de salida correspondiente:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1 -Configuration Release
```

## Entorno y gotchas

- **Access:** 2010 o superior. La arquitectura del add-in (x86/x64) debe coincidir con la de Access instalada. Access 32-bit no carga ensamblados x64 y viceversa.
- **net48 limita APIs:** no `Task.WaitAsync`, no raw strings (C# 11), no `System.Net.Http.Json`, no E/S de archivos async. Para waits con timeout usar `tests/.../TaskTestExtensions.cs`.
- **Named pipe:** framing `[uint32 LE longitud][UTF-8 JSON]`, máx 1 MB. Las clases `NamedPipe*` anteponen `\\.\pipe\` solas — pasar el nombre completo lo duplica. Usar el nombre pelado (`BlsPipeFraming.DefaultPipeName`).
- **Scaffold.ps1 está roto** (4 errores de parser, líneas 762-775). **No usar.**
- **Modelo:** `nvidia/nemotron-3.5-lightning-30b-a3b` (override por variable de entorno `NIM_MODEL`).
- **Tests de integración NIM** requieren `NIM_API_KEY` en el entorno (o `.env`); quedan fuera de la CI por defecto.
- **`.env`** se copia de `.env.example`; nunca se commitea (está en `.gitignore`).
- **API key en DPAPI:** nunca en logs, excepciones, JSON, registry ni disco (`BlsSecureStore`, `CurrentUser`). Véase AGENTS.md regla 7.

## Estructura

- `src/Balansoft.AIAddin.Access/` — add-in COM, Core, Helpers, UI (WinForms)
- `tests/Balansoft.AIAddin.Tests/` — 305 tests MSTest (fakes, sin COM real). `BalansoftTest.accdb` es la base de prueba para verificar en Access real (no se usa en la suite por defecto).
- `specs/` — contratos SDD (001–019, MAYÚSCULAS `NNN-<SLUG>.md`); `specs/README.md` es el índice con estados y tests por spec.
- `docs/design/` — ADR (`ADR-NNN-<SLUG>.md`)
- `docs/governance/` — política de IA, registro de riesgos, AI-BOM y threat model
- `.opencode/skills/` — skills de OpenCode (bls-*)
- `.opencode/agents/` — un único agente activo (`bls-architect`)
- `Balansoft.AIAddin.slnx` — solución real; **no se compila** directamente

## Gestión de sesiones (historial de chat)

- `BlsChatHistory` implementa `IBlsChatHistory` con:
  - `ListSessionsAsync(projectKey, ct)` — lista metadatos de sesiones por proyecto.
  - `LoadSessionAsync(projectKey, sessionId, ct)` — carga una sesión específica.
  - `CreateSessionAsync(projectKey, title, ct)` — crea una nueva sesión.
  - `DeleteSessionAsync(projectKey, sessionId, ct)` — elimina una sesión específica (o todo el historial si `sessionId` es null).
  - `ClearAsync(projectKey, ct)` — elimina todo el historial de un proyecto.
- La Terminal WinForms (`BlsNimTerminal`) expone un botón **Eliminar** junto al selector de sesiones, con confirmación de borrado.
- El historial persiste en `%APPDATA%\Balansoft\AIAddin\history\{projectKey}\` con catálogo `sessions.json`.

## Internacionalización (i18n)

- `BlsStrings` (`Helpers/BlsStrings.cs`) provee textos localizados por clave sin dependencias externas.
- Idioma por defecto: español (`es`). Inglés (`en`), portugués (`pt`) y francés (`fr`) disponibles.
- Uso: `BlsStrings.Get("AppTitle")` o `BlsStrings.Get("MsgSessionLoaded", nombre)` con formato.
- Cambiar idioma: `BlsStrings.CurrentLanguage = "en"` (valida que el idioma esté soportado).
- Fallback: si una clave no existe en el idioma actual, se devuelve la versión en español; si no existe en ningún lado, se devuelve la propia clave.
- Los textos de la Terminal (`BlsNimTerminal`) ya usan `BlsStrings` para título, botones y mensajes principales, incluyendo un ComboBox de selección de idioma.

## Ejecutar un test o categoría enfocada

Con `vstest` directamente (no `dotnet test`, que sustituye el host en net48):

```powershell
# Un test por nombre completo
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86 /TestCaseFilter:"FullyQualifiedName~BlsPipeFramingTests"

# Una categoría (integración requiere NIM_API_KEY)
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /TestCaseFilter:"TestCategory=Integration" /Platform:x86
```

El filtro `-Filter` también está en `scripts\dev.ps1 test -Filter "<área>"`.

## Variables de entorno

Copia `.env.example` a `.env` (nunca se commitea; está en `.gitignore`):

| Variable               | Obligatorio               | Valor por defecto                                      |
|------------------------|---------------------------|--------------------------------------------------------|
| `NIM_API_KEY`          | sí (solo integración NIM) | —                                                      |
| `NIM_MODEL`            | no                        | `nvidia/nemotron-3.5-lightning-30b-a3b`                |
| `NIM_ENDPOINT`         | no                        | `https://integrate.api.nvidia.com/v1/chat/completions` |
| `NIM_REASONING_EFFORT` | no                        | `default`                                              |
| `NIM_DEFAULT_LANGUAGE` | no                        | `english`                                              |

## Convenciones de commit y PR

Mensajes convencionales con referencia al doc de diseño:

```
feat: <nombre> (refs design/YYYYMMDD-feature.md)
fix: <descripción> (closes #<issue>)
docs: ADR-00X <título>
test: <descripción>
chore: <descripción>
```

Los commits generados o asistidos por IA deben incluir el trailer de divulgación:

```
AI-Assistant: OpenCode Big Pickle
```

Un solo commit por feature a `main`. El PR usa `.github/PULL_REQUEST_TEMPLATE.md` con checklist de rechazo (cualquier ítem en negativo ⇒ rechazo automático salvo justificación explícita en "Notas para el revisor"). Items clave del checklist: sin PIAs, sin ROT, `dynamic` solo en `Helpers/BlsComHelper.cs`, marshaling UI por `IUiScheduler`, API key solo DPAPI, errores propagados, x86, `TreatWarningsAsErrors`, sin `Thread.Sleep` en tests, tests ADR obligatorios presentes.

## Orden de specs (SDD)

El spec **001 (ADDIN-LIFECYCLE) ya está implementado**: el add-in carga en Access con `OnConnection`/`OnDisconnection` y 5 tests de ciclo de vida. El orden histórico era 001 → 008 (logging) → 009 (registro) → 002–007 (transporte y funcionalidad). La gobernanza incremental se entregó después: 015 (divulgación, F1), 016 (HITL, F2), 017 (auditoría, F3) y 018 (idempotencia y resiliencia, F4). Siguen como deuda 004 (terminal con 1845 líneas WinForms sin tests directos), 005/006 (sin aserciones) y 008/009 (pendientes parciales). Índice y estados exactos en `specs/README.md`.

## Nueva dependencia NuGet

Requiere ADR (documentar por qué y evaluar compatibilidad net48/x86). No se agrega sin ADR aprobado.

## Versionado y releases

- Versión automática con **Nerdbank.GitVersioning** (`version.json`, SemVer 2).
- Push de tag `vX.Y.Z` en `main` → `release.yml` → build Release x86 y x64 → `Pack.ps1` → GitHub Release con ZIP por arquitectura (`-x86.zip` y `-x64.zip`) + `version.txt`.
- Un solo commit por feature a `main`.

## Flujo de trabajo (SDD)

1. Spec en `specs/` con criterios de aceptación verificables (primero).
2. Implementar + tests (x86 y x64, sin PIAs, `dynamic` aislado).
3. Verificar: build Debug + Release en **x86 y x64** y suite de tests verde en ambas arquitecturas.
4. PR → CI ejecuta 13 gates: build, tests (305/305 en x86 y x64), gobernanza IA (política, BOM, divulgación y agente único), fail-safe/HITL (kill switch + DbC), auditoría (JSONL, cadena SHA-256, correlación y saneamiento, F3), idempotencia y resiliencia (clave SHA-256, caché TTL/FIFO, jitter y breaker, F4), privacidad/supply chain/doc sync (scrubber, hash del prompt y catálogo, entropía derivada, F5), documentos obligatorios, ausencia de PIAs, aislamiento de `dynamic`, ausencia de `Thread.Sleep` en tests, tests de ADR, referencias cruzadas en MDs (incluye rutas de código e índice de specs).
5. Merge a `main` → tag `v*` → release automático.
