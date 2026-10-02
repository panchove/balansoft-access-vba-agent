# MEMORY.md — Balansoft Access VBA Agent (NIM Direct)

> Contexto operativo y de mantenimiento para sesiones futuras.
> Actualizar al cerrar cada bloque de trabajo relevante.
> Última actualización: 2026-10-02 (SDD completo: constitución + 9 specs).

---

## 1. Identidad del proyecto

| Campo    | Valor                                                                 |
|----------|-----------------------------------------------------------------------|
| Qué es   | COM Add-in para Access 2010+ (x86) que integra Nvidia NIM en el VBE   |
| ProgID   | `Balansoft.AIAddin.Access`                                            |
| CLSID    | `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`                              |
| Stack    | C# 10 · .NET Framework 4.8 · WinForms · `System.Text.Json` 8.0.5      |
| Target   | `net48`, `PlatformTarget=x86`, `Prefer32Bit=true`, `Nullable=annotations` |
| Solución | `Balansoft.AIAddin.slnx` (formato XML nuevo, 2 proyectos)             |
| Tamaño   | ~3.700 LOC en `src/`, ~2.370 LOC en `tests/`                          |

**Nota de naming:** el repo se llama `balansoft-access-vba-agent`, la solución
`Balansoft.AIAddin.*`. Son nombres distintos a propósito; no unificar sin
revisar los scripts de registro, que usan el ProgID.

---

## 2. Reglas duras (no negociables)

1. **Nunca** referencia a PIAs de Office (`Microsoft.Office.Interop.*`).
2. **Nunca** `Marshal.GetActiveObject` ni ROT para obtener VBE/Application.
3. Todo acceso a VBE/Access desde hilos no-UI pasa por `IUiScheduler`.
4. API key **solo** en DPAPI (`CurrentUser`). Prohibido en logs, config, JSON, registry.
5. Errores COM/NIM se propagan como excepción con categoría + mensaje original.
   Prohibido devolver "éxito falso" ante fallo.
6. `PlatformTarget=x86` obligatorio. No tocar a AnyCPU ni x64.
7. `TreatWarningsAsErrors=true` — los warnings bloquean merge.
   **Aplicado** en `Directory.Build.props`; build limpio 0 warnings en Debug y
   Release x86.
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

**La CI está operativa** y ejecuta: build Debug/Release x86, tests 99/99, gates de PIAs, `dynamic`, `Thread.Sleep`, documentos, tests ADR y referencias cruzadas. En `push` a `main` ejecuta `Pack.ps1` y sube el ZIP.

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
                   no hay 20250105) + 20250101-vbe-bridge.prompt.md
specs/             README.md + 001..009 (contratos SDD por feature)
docs/              PRD.md, constitution.md
scripts/           Scaffold, RegisterAddin, UnregisterAddin, Pack, CopyDocs, dev
.opencode/skills/  00-project-context .. 10-workflow-big-pickle-nemotron (11 skills)
.github/           workflows/ci.yml, PULL_REQUEST_TEMPLATE.md
```

---

## 5. Protocolo Bridge

10 métodos JSON-RPC, whitelist en `JsonRpcMethods.All`:

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
- Modelo por defecto: `nvidia/nemotron-3-ultra-550b-a55b`
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

**Suite verde.** Rebuild limpio de ambos proyectos, **99/99 tests**. Reparto:
`BlsNimClientTests` 28 · `JsonRpcDispatcherTests` 17 · `NimStreamParserTests` 14
· `BlsVbeBridgeServerTests` 12 · `NimHttpRequestBuilderTests` 11 ·
`BlsPipeFramingTests` 10 · `JsonRpcDeserializationTests` 7.

Implementado: bridge JSON-RPC completo, cliente NIM con streaming SSE y
retries, secure store DPAPI, historial por proyecto, Terminal con loop de
herramientas funcional.

**Lo que no significa "verde":** el producto no carga en Access. Falta el
punto de entrada COM. El estado por feature está en `specs/README.md`
(003 `implementado`; 002/004/005/006 `aceptado`; 001/007/008/009 `borrador`).

### Pendiente

- **Lifecycle del add-in** (`specs/001-addin-lifecycle.md`):
  `OnConnection`, menú Tools, construcción de dependencias, arranque/parada
  del bridge y shutdown. Nada de esto está cableado todavía.
- **Implementaciones reales de `IBlsVbe` e `IBlsAccess`.** Solo hay interfaz
  y dobles de test. Y **`BlsComHelper` no existe**, así que el `dynamic` de
  P4 no tiene dónde vivir.
- **`BlsLogger` y `BlsVersion` no existen.** `IBlsLogger` solo tiene
  `FakeLogger`: ningún log se escribe en producción, así que ningún fallo es
  diagnosticable. `BlsPaths.LogsDir` está definido y sin usar.
- **`BlsConstants` no existe.** `.env.example` referencia
  `BlsConstants.NimReasoningEffort` y `BlsConstants.NimDefaultLanguage`; no
  hay ninguna clase así en el código. O se implementa o se actualiza el
  `.env.example`.
- **Tests ausentes en 004, 005 y 006.** ~600 líneas de WinForms, DPAPI e
  historial JSON sin una sola aserción. Para la Terminal hay que extraer
  `RunToolLoopAsync` fuera del `Form` antes de poder testearlo.
- **Historial con tool calls huérfanos.** `HistoryMessage` no tiene campo
  `ToolCalls`. Al recargar historial y reconstruir el request se envía un
  `role="tool"` sin el `assistant` con `tool_calls` que lo precede; NIM
  debería responder 400. Hoy cada turno pide `project_summary` de nuevo, así
  que rara vez se nota. Arreglo limpio: añadir `ToolCalls` a `HistoryMessage`.
- **Drift de documentación:** `docs/design/20250103-nim-terminal.md:285-334`
  describe el `OnToolCall`/`OnDone` antiguos, que provocaban una carrera
  (el turno se cerraba a mitad del loop y el CTS se liberaba antes de que
  la tool ejecutara). El código ya no los sigue. `specs/004` documenta el
  comportamiento real; falta decidir si se actualiza el doc.
- **`scripts/Scaffold.ps1` no parsea.** 4 errores en líneas 762-775
  (comillas tipográficas). Verificar con
  `[System.Management.Automation.Language.Parser]::ParseFile()` antes de usarlo.
- **Nombres de test desalineados con la CI.** La CI busca 7 tests de ADR que
  no existen, y dos de los que sí existen tienen otro nombre
  (`Turno_CanceladoDuranteStream_DetieneYEmitCancelled`,
  `Turno_TimeoutChunk_LanzaTimeoutNoCancelled`). Además
  `Frame_Vacio_RespondeInvalidRequest` y
  `Frame_JsonInvalido_RespondeInvalidRequest` están duplicados en dos clases.
- **Limpieza:** `src/Balansoft.AIAddin.Access/Class1.cs` es residuo del
  scaffold. `balansoft-aiaddin-access/` en la raíz es un repositorio git
  vacío (un único commit del árbol vacío), no una carpeta de trabajo.

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

- `RegisterAddin.ps1` / `UnregisterAddin.ps1` deben correr en PowerShell
  **x86** (menú "Windows PowerShell (x86)").
- **Rutas arregladas**: `RegisterAddin.ps1`, `UnregisterAddin.ps1`,
  `Pack.ps1` y `dev.ps1` usan `bin\x86\<Config>\net48\` (output de
  `dotnet build -p:Platform=x86`). Verificado: `Pack.ps1` produce ZIP válido.
- `Directory.Build.props` existe con versión `0.1.0`; `Pack.ps1` la lee y
  `version.txt` sale con `0.1.0` (+ commit SHA en CI).
- **Ningún script se ha ejecutado en Access real** (punto de entrada COM
  pendiente, spec 001). Los scripts son sintácticamente correctos, compilan y
  empaquetan, pero la verificación de `LoadBehavior = 3` requiere que el add-in
  cargue.
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

- `docs/constitution.md` (v1.1): principios P1-P12, restricciones, gates,
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