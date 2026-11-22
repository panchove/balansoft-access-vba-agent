# MEMORY.md — Balansoft Access VBA Agent (NIM Direct)

> Contexto operativo y de mantenimiento para sesiones futuras.
> Actualizar al cerrar cada bloque de trabajo relevante.
> Última actualización: 2026-11-19 (F1 gobernanza IA + F2 fail-safe/HITL + F3 auditoría + F4 idempotencia/resiliencia + F5 privacidad/supply chain; 321 tests verdes, 13 gates de CI, R-01…R-12 cerrados).

---

## 1. Identidad del proyecto

| Campo    | Valor                                                                                |
|----------|--------------------------------------------------------------------------------------|
| Qué es   | COM Add-in para Access 2010+ (x86) que integra Nvidia NIM en el VBE                  |
| ProgID   | `Balansoft.AIAddin.Access`                                                           |
| CLSID    | `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`                                             |
| Stack    | C# 10 · .NET Framework 4.8 · WinForms · `System.Text.Json` 8.0.5                     |
| Target   | `net48`, CPU-agnóstico (`PlatformTarget` x86\|x64 vía `-p:`), `Nullable=annotations` |
| Solución | `Balansoft.AIAddin.slnx` (formato XML nuevo, 2 proyectos)                            |
| Tamaño   | ~3.700 LOC en `src/`, ~2.370 LOC en `tests/`                                         |

**Nota de naming:** el repo se llama `balansoft-access-vba-agent`, la solución
`Balansoft.AIAddin.*`. Son nombres distintos a propósito; no uznificar sin
revisar los scripts de registro, que usan el ProgID.

---

## 2. Reglas duras (no negociables)

1. **Nunca** referencia a PIAs de Office (`Microsoft.Office.Interop.*`).
2. **Nunca** `Marshal.GetActiveObject` ni ROT para obtener VBE/Application.
3. Todo acceso a VBE/Access desde hilos no-UI pasa por `IUiScheduler`.
4. API key **solo** en DPAPI (`CurrentUser`). Prohibido en logs, config, JSON, registry.
5. Errores COM/NIM se propagan como excepción con categoría + mensaje original.
   Prohibido devolver "éxito falso" ante fallo.
6. **CPU-agnóstico:** la arquitectura (x86|x64) se pasa con `-p:PlatformTarget`; no forzar `PlatformTarget` ni `Prefer32Bit` en los proyectos. La arquitectura del add-in debe coincidir con la de Access instalada.
7. `TreatWarningsAsErrors=true` — los warnings bloquean merge.
   **Aplicado** en `Directory.Build.props`; build limpio 0 warnings en Debug y
   Release x86 y x64.
8. Framing del Named Pipe: 4 bytes LE (longitud) + UTF-8 JSON. Ver ADR-002.
9. Identidad de proyecto: SHA256(ruta normalizada). Ver ADR-001.
10. Abort: un `CancellationTokenSource` por turno de usuario. Ver ADR-003.

---

## 3. Comandos canónicos

`dotnet build` (el MSBuild del SDK resuelve `Microsoft.NET.Sdk`; un MSBuild de VS sin el workload de .NET falla con MSB4236):

```powershell
dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86 -v:minimal --nologo
dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86 -v:minimal --nologo
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86
```

Registro (requiere **PowerShell x86**):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1
```

**Existen** `.runsettings`, `Directory.Build.props`, y la solución real `Balansoft.AIAddin.slnx`. Los comandos usan los `.csproj` directos (la solución no propaga `Platform` a proyectos SDK-style).

**La CI está operativa** y ejecuta 13 gates: build Debug/Release x86 y x64, tests 321/321, gobernanza IA, fail-safe/HITL, auditoría JSON/hash/correlación (F3), idempotencia y resiliencia (F4), privacidad/supply chain/doc sync (F5), PIAs, `dynamic`, `Thread.Sleep`, documentos, tests ADR y referencias cruzadas (rutas de código + índice de specs). En `push` a `main` ejecuta `Pack.ps1` y sube el ZIP.

---

## 4. Layout

```
src/Balansoft.AIAddin.Access/
  Core/            BlsVbeBridgeServer, IBlsBridgeServer
  Core/Protocol/   JsonRpc{Request,Response,Error,ErrorCodes,ErrorData,Methods,Dispatcher},
                   BlsPipeFraming, BlsFrameTooLargeException
  UI/              BlsNimTerminal, BlsNimClient, BlsBridgeClient, ApiKeyPromptForm,
                   SystemPromptBuilder, VbeContextProvider, WinFormsUserInputDialog
  UI/Nim/          ChatMessage, NimHttpRequestBuilder, NimStreamParser, SseLineReader,
                   ToolCallComplete, ToolDefinition
  Helpers/         BlsChatHistory, BlsSecureStore, BlsPaths, DpapiDataProtector
  Helpers/Exceptions/  BlsException + Com/Access/Vbe/Nim/SecureStore/InvalidParams
  VbeIntegration/  IBlsVbe
  AccessIntegration/IBlsAccess
tests/Balansoft.AIAddin.Tests/
  Core/            BlsVbeBridgeServerTests
  Core/Protocol/   BlsPipeFramingTests, JsonRpcDispatcherTests, JsonRpcDeserializationTests
  UI/Nim/          BlsNimClientTests, NimHttpRequestBuilderTests, NimStreamParserTests
  Fakes/           FakeAccess, FakeHttpHandler, FakeLogger, FakeSecureStore,
                   MockVbe, TestUiScheduler
  Infrastructure/  TaskTestExtensions
docs/design/       ADR-001..004 + 7 docs de diseño (20250101-02-03-04-06-07-08,
                   no hay 20250105) + ADR-006-VBE-BRIDGE.prompt.md
specs/             README.md + 001..009 (contratos SDD por feature)
docs/              PRD.md, constitution.md
scripts/           Scaffold, RegisterAddin, UnregisterAddin, Pack, CopyDocs, dev
.opencode/skills/  00-project-context .. 10-workflow-big-pickle-nemotron (11 skills)
.github/           workflows/ci.yml, PULL_REQUEST_TEMPLATE.md
```

---

## 5. Protocolo Bridge

19 métodos JSON-RPC, whitelist en `JsonRpcMethods.All`:

- **VBE (7):** `list_modules`, `read_module`, `write_module`, `create_module`,
  `delete_module`, `get_selection`, `get_active_module`
- **Access read-only (3):** `project_summary`, `list_objects`, `object_detail`

Códigos de error relevantes: `-32603` internal, `-32000` application,
`-32001` frame too large, `-32002` tool timeout, `-32003` frame incomplete,
`-32004` bridge busy.

`JsonRpcErrorData` expone `Category` / `Op` / `HResult` / `Stack` (los
nombres son `Category` y `Op`, **no** `Kind` ni `Method`).

---

## 6. NIM

- Endpoint: `https://integrate.api.nvidia.com/v1/chat/completions`
- Modelo por defecto: `nvidia/nemotron-3-super-120b-a12b`
- Precedencia de modelo: parámetro > `NIM_MODEL` > default.
- Retry: 429/5xx con backoff 1s → 2s → 4s (3 intentos). `Retry-After` se
  respeta pero se **corta a 30s**. 4xx es terminal.
- Timeout de chunk: 120s sin datos ⇒ `BlsNimTimeoutException`.
- Tool calls se ensamblan por `index`; JSON inválido ⇒ `BlsNimProtocolException`.

### Gotcha: nombre del pipe

`NamedPipeServerStream` y `NamedPipeClientStream` **anteponen** `\\.\pipe\`
por su cuenta. Pasar el nombre completo lo duplica y servidor y cliente
terminan en pipes distintos. Usar siempre el nombre pelado
(`BlsPipeFraming.DefaultPipeName`) o `BlsPipeFraming.NormalizePipeName()`.

### Gotcha: instancias del pipe

`maxNumberOfServerInstances` es 2. Con un cliente conectado solo queda 1
instancia libre. El accept loop debe crear **una instancia por iteración** y
reintentar si `CreatePipe` devuelve `null`; nunca hacer `break` (cerraría el
pipe del cliente entrante y el segundo recibiría EOF en vez de `busy`).

---

## 7. Estado actual

**Suite verde.** Rebuild limpio de ambos proyectos en x86 y x64, **321/321
tests** (32 nuevos en F5). El reparto completo por fichero está en
`specs/README.md`.

Implementado: ciclo de vida COM (spec 001), bridge JSON-RPC completo, cliente
NIM con streaming SSE y reintentos, secure store DPAPI, historial por proyecto,
Terminal con loop de herramientas, divulgación IA persistente (F1),
fail-safe/HITL con kill switch (F2), auditoría JSONL con hash encadenado,
correlación y métricas SLO (F3), idempotencia del bridge con full jitter y
circuit breaker en NIM (F4), y saneamiento PII + consentimiento + entropía
derivada + modelo inventariado (F5). El add-in carga en Access.

**Lo que no significa "verde":** los doce riesgos R-01…R-12 están cerrados, pero
la verificación final en Access real con `BalansoftTest.accdb` no está
automatizada, y sigue habiendo deuda heredada (specs 004/005/006/008/009).
Estado por feature en `specs/README.md` (015/016/017/018/019 `implementado`).

### Pendiente

- **F4 — resiliencia (hecho):** `BlsIdempotencyKey` SHA-256 por turno,
  `BlsIdempotencyCache` TTL 5 min/256 entradas, replay con
  `idempotent_replay = true`; `BlsExponentialJitterRetryPolicy` con
  `Retry-After` prevalente y `BlsCircuitBreaker` Closed/Open/HalfOpen. Cierra
  R-06 y R-09. Contratos: spec 018 y ADR-017.
- **F5 — privacidad (hecho):** `BlsPiiScrubber` en la frontera HTTP,
  `BlsPiiConsentGate` con "No" por defecto, `BlsModelCatalog` como fuente única,
  entropía DPAPI derivada con migración transparente y gate 8f. Cierra R-03,
  R-07, R-10 y R-11. Contratos: spec 019 y ADR-018.
- **Red teaming de inyección de contexto:** pendiente (plan de pruebas
  adversarias del threat model); no bloquea el cierre de R-*.
- **Deuda heredada de spec 008:** rotación de logs de 7 días y `BlsVersion`
  (versión + commit en la primera línea del log). ADR-011 queda parcialmente
  superado por ADR-016.
- **Calidad:** spec 004 (Terminal, 1845 líneas WinForms sin tests directos),
  005/006 (código sin aserciones) y 009 (registro incompleto).
- **Historial con tool calls huérfanos.** `HistoryMessage` no tiene campo
  `ToolCalls`; al recargar se envía `role="tool"` sin su `assistant` previo.
  Arreglo limpio: añadir `ToolCalls` a `HistoryMessage`.
- **Drift de documentación:** `docs/design/ADR-008-NIM-TERMINAL.md:285-334`
  describe los antiguos `OnToolCall`/`OnDone`, ya no seguidos por el código.
- **`scripts/Scaffold.ps1` no parsea** (4 errores en 762-775). **No usar.**
- **Limpieza:** `balansoft-aiaddin-access/` en la raíz es un repositorio git
  vacío, no una carpeta de trabajo.

---

## 8. Restricciones de `net48` (causan fallos de compilación)

No disponibles aunque existen en .NET moderno:

- `Task.WaitAsync` — usar `tests/.../TaskTestExtensions.cs`
- Raw strings de C# 11 (el proyecto es `LangVersion 10`) — usar `"{\"a\":1}"`
- `HttpClient` de `System.Net.Http.Json`
- APIs async de E/S de archivos en `net48`

`Nullable=annotations` y el código **sí** usa anotaciones nullable (`?` en tipos
de referencia). Con `disable` el compilador emitía 88 avisos CS8632. La
propiedad ahora coincide con el estilo real del código.

---

## 9. Gotchas operativos

- `RegisterAddin.ps1` / `UnregisterAddin.ps1` deben correr en PowerShell de la
  **misma arquitectura que el Access instalado** (x86 o x64); el script detecta
  la arquitectura y elige la carpeta de salida.
- **Rutas arregladas**: `RegisterAddin.ps1`, `UnregisterAddin.ps1`,
  `Pack.ps1` y `dev.ps1` usan `bin\x86\<Config>\net48\` (output de
  `dotnet build -p:Platform=x86`). Verificado: `Pack.ps1` produce ZIP válido.
- `Directory.Build.props` existe con versión `0.1.0`; `Pack.ps1` la lee y
  `version.txt` sale con `0.1.0` (+ commit SHA en CI).
- **Ningún script se ha ejecutado en Access real** en esta sesión. Los scripts
  son sintácticamente correctos, compilan y empaquetan; la verificación de
  `LoadBehavior = 3` requiere una prueba manual en Access.
- Tests de integración NIM requieren `NIM_API_KEY` en env (o `.env`).
- `Scaffold.ps1` es idempotente; `-Force` sobrescribe. **Está roto** (4 errores
  de parser, líneas 762-775) y genera comandos obsoletos. No usarlo.
- Los tests de pipe usan nombres únicos por test
  (`balansoft-opencode-test-{guid}`) para evitar colisiones al ejecutarse en
  paralelo. `BlsVbeBridgeServerTests.Cleanup` deja 100 ms de margen para que
  el pipe se libere.
- Los tests de `named pipe` con cliente real son sensibles al timing: el pipe
  busy se libera con un delay de 250 ms (`BusyDrainDelay`) porque en modo
  mensaje `Dispose` descarta lo que no se ha transferido.

---

## 10. Spec-Driven Development

- `docs/CONSTITUTION.md` (v1.1): principios P1-P12, restricciones, gates,
  penalidades y diagnóstico verificado del árbol.
- `specs/README.md`: formato, estados, índice, orden de implementación,
  trazabilidad PRD↔spec y ADR↔spec.
- `specs/001` a `specs/009`: un contrato por feature, con requisitos
  numerados, criterios Given/When/Then, nombres exactos de tests y
  desviaciones.
- Regla: un spec describe el **comportamiento real**. Cuando el código y el
  doc de diseño discrepan, gana el código y la discrepancia se anota en
  "Desviaciones". `implementado` exige tests verdes.
- Antes de implementar, leer el spec del frente y su ADR. Si el cambio altera
  un ADR, actualiza el ADR y el spec en el mismo PR.

---

## 11. Flujo entre modelos

- **Big Pickle** (este modelo): diseño de interfaces, contratos, revisión.
- **Nemotron 3 Ultra**: implementación, scripts, tests.
- Antes de implementar, leer los skills 00-10 en `.opencode/skills/`.
- Si un ADR contradice un skill, gana el ADR y hay que actualizar el skill.
- Si el código contradice un doc de diseño, gana el código: actualizar el doc
  o marcarlo como sustituido, pero no dejar los dos diciendo cosas distintas.