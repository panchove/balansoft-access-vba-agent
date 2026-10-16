---
version: 1.0.0
date: 2025-01-01
status: approved
iso_standard: [29148, 42010, 12207]
aligns_to: [PRD.md, AGENTS.md, ADR-002-PIPE-FRAMING.md, specs/002-vbe-bridge.md]
tags: [design, architecture, vbe-bridge, json-rpc, named-pipe]
---

# ADR-006 — VBE Bridge (JSON-RPC 2.0 sobre Named Pipe)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-01

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

---

## 1. Contexto

El bridge es el componente que permite al modelo NIM **actuar** sobre el
proyecto Access: leer/escribir módulos VBA, consultar selección, y leer
objetos de la base (tablas, queries, formularios).

Es la pieza central del MVP: sin bridge, la terminal solo sería un chat
de texto sin capacidad de actuar.

Cubre:
- PRD §3.2 (bridge JSON-RPC, 10 operaciones).
- PRD §7 FR-04 (bridge en `\\.\pipe\balansoft-opencode`).
- PRD §7 FR-12 (errores reales propagados).
- PRD §11 criterios 4, 7, 10.

Y materializa:
- ADR-002 (framing).
- ADR-003 (abort).
- ADR-004 (contrato de `object_detail`).

---

## 2. Alcance

### Incluido
- Servidor `NamedPipeServerStream` con framing `[uint32 LE][UTF-8 JSON]`.
- Whitelist de los 10 métodos (7 VBE + 3 Access read-only).
- Dispatch serializado al hilo UI (`IUiScheduler`).
- Propagación del `CancellationToken` del turno (ADR-003).
- Códigos de error reservados: `-32600` a `-32604`, `-32000` a `-32004`.
- Ciclo de vida: `Start()` en `AttachVbe`, `Stop()` en shutdown.
- Manejo de un solo cliente concurrente.
- Tests unitarios sin Office (usando `MockVbe`, `FakeAccessApp`,
  `TestUiScheduler`).

### Excluido (fuera de MVP)
- Múltiples clientes concurrentes.
- Batching JSON-RPC.
- Autenticación del cliente (confiamos en `CurrentUserOnly`).
- Compresión de frames.
- Streaming de respuestas (cada response es un frame único).
- Notificaciones JSON-RPC (siempre hay `id`).
- Soporte para métodos fuera de la whitelist de 10.

---

## 3. Interfaces nuevas

### 3.1 `IBlsBridgeServer` (Core/)
Contrato que ve `BalansoftAddin` para arrancar/parar el bridge.

```csharp
namespace Balansoft.AIAddin.Core;

public interface IBlsBridgeServer : IDisposable
{
    bool IsRunning { get; }
    void Start();
    Task StopAsync(TimeSpan timeout);
}
```

**Notas:**
- `Start()` no bloquea: lanza el `Task` de aceptación internamente.
- `StopAsync(timeout)` cancela el loop, cierra el pipe y espera el
  `Task` con el timeout indicado. Si expira, log `WARN` y continúa
  (no bloquea el shutdown de Access).
- `Dispose()` es idempotente.

### 3.2 `JsonRpcRequest` / `JsonRpcResponse` (Core/Protocol/)
Modelos de transporte. Serializados con `System.Text.Json`.

```csharp
public sealed record JsonRpcRequest(
    string JsonRpc,
    object Id,          // long o string, se replica tal cual
    string Method,
    JsonElement? Params);

public sealed record JsonRpcResponse(
    string JsonRpc,
    object Id,
    object? Result,
    JsonRpcError? Error);

public sealed record JsonRpcError(
    int Code,
    string Message,
    JsonRpcErrorData? Data);

public sealed record JsonRpcErrorData(
    string Category,
    string? Op,
    string? HResult,
    string? Stack);     // solo en Debug
```

**Notas:**
- `JsonRpc` siempre `"2.0"`.
- `Id` se guarda como `object` porque JSON-RPC admite number o string.
- `Stack` se rellena solo si `BlsVersion.IsDebug`.

### 3.3 `JsonRpcMethods` (Core/Protocol/)
Constantes de nombres de método. Evita strings mágicos.

```csharp
public static class JsonRpcMethods
{
    // VBE
    public const string ListModules      = "list_modules";
    public const string ReadModule       = "read_module";
    public const string WriteModule      = "write_module";
    public const string CreateModule     = "create_module";
    public const string DeleteModule     = "delete_module";
    public const string GetSelection     = "get_selection";
    public const string GetActiveModule  = "get_active_module";

    // Access read-only
    public const string ProjectSummary   = "project_summary";
    public const string ListObjects      = "list_objects";
    public const string ObjectDetail     = "object_detail";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal) { /* los 10 */ };
}
```

### 3.4 `JsonRpcErrorCodes` (Core/Protocol/)
Códigos reservados. Fuente única.

```csharp
public static class JsonRpcErrorCodes
{
    // JSON-RPC estándar
    public const int InvalidRequest   = -32600;
    public const int MethodNotFound   = -32601;
    public const int InvalidParams    = -32602;
    public const int InternalError    = -32603;

    // Aplicación (ADR-002, ADR-003)
    public const int ApplicationError = -32000;
    public const int FrameTooLarge    = -32001;
    public const int ToolTimeout      = -32002;
    public const int FrameIncomplete  = -32003;
    public const int Busy             = -32004;
}
```

### 3.5 `BlsPipeFraming` (Core/Protocol/)
Lectura/escritura de frames. Aislado para testear el framing sin pipe.

```csharp
public static class BlsPipeFraming
{
    public const int MaxFrameSize = 1024 * 1024; // 1 MB (ADR-002)

    public static async Task<byte[]?> ReadFrameAsync(
        Stream stream, CancellationToken ct);

    public static async Task WriteFrameAsync(
        Stream stream, ReadOnlyMemory<byte> payload, CancellationToken ct);
}
```

**Notas:**
- `ReadFrameAsync` devuelve `null` en EOF limpio.
- Lanza `BlsFrameTooLargeException` si `length > MaxFrameSize`.
- Lanza `BlsFrameIncompleteException` si timeout a mitad de frame.
- No conoce JSON: solo bytes.

### 3.6 `BlsVbeBridgeServer` (Core/)
Implementación de `IBlsBridgeServer`. Orquesta todo.

```csharp
public sealed class BlsVbeBridgeServer : IBlsBridgeServer
{
    public BlsVbeBridgeServer(
        IBlsVbe vbe,
        IBlsAccess access,
        IUiScheduler ui,
        IBlsLogger logger,
        string pipeName = @"\\.\pipe\balansoft-opencode");

    // IBlsBridgeServer
    public bool IsRunning { get; }
    public void Start();
    public Task StopAsync(TimeSpan timeout);
    public void Dispose();
}
```

**Dependencias inyectadas** (todas interfaces, cero acoplamiento a COM):

- `IBlsVbe`: automatización VBE (skill bls-vbe-automation).
- `IBlsAccess`: lectura de objetos Access (skill bls-vbe-automation / AccessIntegration).
- `IUiScheduler`: marshaling al hilo UI (skill bls-winforms-threading).
- `IBlsLogger`: logging (skill bls-logging-errors).
- `pipeName`: por defecto el de ADR-002, parametrizable para tests.

---

## 4. Invariantes afectados

| Fuente             | Invariante                                      | Cómo se cumple                                                   |
|--------------------|-------------------------------------------------|------------------------------------------------------------------|
| AGENTS.md regla 3  | Marshaling al hilo UI                           | Todo handler VBE/Access pasa por `IUiScheduler.RunAsync`         |
| AGENTS.md regla 5  | Errores reales                                  | Mapeo categoría → código JSON-RPC con mensaje original           |
| AGENTS.md regla 8  | Framing ADR-002                                 | `BlsPipeFraming` único punto                                     |
| AGENTS.md regla 10 | Abort ADR-003                                   | `CancellationToken` por turno propagado a handlers               |
| ADR-002            | `[uint32 LE][UTF-8 JSON]`, máx 1 MB             | `BlsPipeFraming`                                                 |
| ADR-002            | Segundo cliente → `-32004 busy`                 | Loop con una sola instancia de pipe                              |
| ADR-002            | Timeout idle entre frames → no cerrar           | El `CancellationToken` del loop no expira por inactividad        |
| ADR-003            | Cancelación antes del VBE → no ejecuta          | Comprobación de `ct.IsCancellationRequested` antes de `RunAsync` |
| ADR-003            | Cancelación dentro del VBE → termina y descarta | No se aborta mid-VBE (VBE no cancelable)                         |
| ADR-004            | `object_detail` con `includeSql` opt-in         | Handler de `object_detail` respeta los parámetros                |
| PRD FR-04          | 10 métodos exactos                              | `JsonRpcMethods.All` y whitelist en dispatch                     |
| PRD FR-12          | Errores con categoría                           | `JsonRpcErrorData.Category` siempre presente                     |

---

## 5. Flujo interno

```
AcceptLoop (Task long-running)
  │
  ├─ NamedPipeServerStream (1 instancia, Message, CurrentUserOnly)
  │
  ├─ WaitForConnectionAsync(ct)
  │
  ├─ Set IsRunning = true
  │
  └─ HandleClientAsync(pipe, ct)
       │
       └─ while (!ct.IsCancellationRequested)
            │
            ├─ bytes = BlsPipeFraming.ReadFrameAsync(pipe, ct)
            │     │
            │     ├─ null          → cliente desconectó, salir
            │     ├─ TooLarge      → WriteFrame(-32001); break
            │     ├─ Incomplete    → WriteFrame(-32003); break
            │     └─ bytes         → continuar
            │
            ├─ request = Deserialize(bytes)
            │     │
            │     └─ si falla → WriteFrame(-32600); continue
            │
            ├─ ValidateJsonRpc(request) → si no "2.0" → WriteFrame(-32600)
            │
            ├─ ValidateMethod(request)  → si no en whitelist → WriteFrame(-32601)
            │
            ├─ response = await DispatchAsync(request, ct)
            │
            └─ WriteFrame(pipe, Serialize(response), ct)
```

### `DispatchAsync` (resumen)
```
switch (request.Method)
  case list_modules:       return await VbeAsync(ct, () => vbe.ListModules());
  case read_module:        return await VbeAsync(ct, () => vbe.ReadModule(p.Name));
  case write_module:       return await VbeAsync(ct, () => vbe.WriteModule(p.Name, p.Code));
  case create_module:      return await VbeAsync(ct, () => vbe.CreateModule(p.Name, p.Code, ParseType(p.Type)));
  case delete_module:      return await VbeAsync(ct, () => vbe.DeleteModule(p.Name, p.Confirm));
  case get_selection:      return await VbeAsync(ct, () => vbe.GetSelection());
  case get_active_module:  return await VbeAsync(ct, () => vbe.GetActiveModule());
  case project_summary:    return await AccessAsync(ct, () => access.ProjectSummary());
  case list_objects:       return await AccessAsync(ct, () => access.ListObjects());
  case object_detail:      return await AccessAsync(ct, () => access.ObjectDetail(p.Name, p.Type, p.IncludeSql, p.MaxFields));
  default:                 return Error(-32601, "method_not_found");
```

### `VbeAsync` / `AccessAsync`
```
if (ct.IsCancellationRequested)
    return Cancelled(id);            // ADR-003: no entra al VBE

await _gate.WaitAsync(ct);            // serialización VBE
try
{
    var result = await _ui.RunAsync(op);   // marshaling al hilo UI
    return Ok(id, result);
}
catch (BlsException ex)              // skill bls-logging-errors
{
    return ErrorFrom(ex, method, id);
}
finally { _gate.Release(); }
```

---

## 6. Contratos de `params` por método

Cada handler valida estrictamente. Campos extra → `-32602`.

### `list_modules`
```json
{}
```

### `read_module`
```json
{ "name": "Utils" }
```

### `write_module`
```json
{ "name": "Utils", "code": "Public Function Suma(...)\n..." }
```

### `create_module`
```json
{ "name": "Utils", "code": "", "type": "Standard" }
```
`type` ∈ {`Standard`, `Class`, `Form`}.

### `delete_module`
```json
{ "name": "Utils", "confirm": true }
```
`confirm` debe ser literalmente `true` (no truthy).

### `get_selection`
```json
{}
```

### `get_active_module`
```json
{}
```

### `project_summary`
```json
{}
```

### `list_objects`
```json
{}
```

### `object_detail`
```json
{ "name": "Clientes", "type": "Table", "includeSql": false, "maxFields": 200 }
```
Defaults: `includeSql=false`, `maxFields=200`. `maxFields` ∈ [1, 1000].

---

## 7. Criterios de aceptación

Cada criterio lleva su test asociado (skill bls-testing-strategy).

### CA-01 — Framing
- [ ] `BlsPipeFraming.ReadFrameAsync` lee correctamente frames
      concatenados.
- [ ] `BlsPipeFraming.WriteFrameAsync` escribe `[uint32 LE][UTF-8 JSON]`.
- [ ] Frame > 1 MB → `BlsFrameTooLargeException`.
- [ ] Timeout a mitad de frame → `BlsFrameIncompleteException`.
- **Tests:** `Frame_EnvioSimple_LeeMismoPayload`,
  `Frame_PayloadConNewlines_NoRompeFraming`,
  `Frame_PayloadUtf8Multibyte_LengthEsEnBytes`,
  `Frame_Excede1MB_CierraConError`,
  `Frame_HeaderParcial_EsperaResto`,
  `Frame_PayloadParcial_EsperaResto`,
  `Frame_TimeoutInactivo_MitadDeFrame_Cierra`.

### CA-02 — JSON-RPC
- [ ] `jsonrpc != "2.0"` → `-32600`.
- [ ] `method` fuera de whitelist → `-32601`.
- [ ] `params` inválidos → `-32602`.
- [ ] Excepción no controlada → `-32603`.
- **Tests:** `Frame_Vacio_RespondeInvalidRequest`,
  `Frame_JsonInvalido_RespondeInvalidRequest`,
  `Bridge_MetodoDesconocido_RespondeMethodNotFound`,
  `Bridge_ParamsInvalidos_RespondeInvalidParams`,
  `Bridge_ExcepcionNoControlada_RespondeInternalError`.

### CA-03 — Los 10 métodos
- [ ] Los 7 VBE y 3 Access responden con `result` cuando se invocan
      con params válidos.
- **Tests:** `Bridge_ListModules_DevuelveLista`,
  `Bridge_ReadModule_DevuelveCodigo`,
  `Bridge_WriteModule_EscribeEnVbe`,
  `Bridge_CreateModule_CreaEnVbe`,
  `Bridge_DeleteModule_ConConfirm_Borra`,
  `Bridge_DeleteModule_SinConfirm_RespondeInvalidParams`,
  `Bridge_GetSelection_DevuelveRango`,
  `Bridge_GetActiveModule_DevuelveInfo`,
  `Bridge_ProjectSummary_DevuelveCounts`,
  `Bridge_ListObjects_DevuelveCategorias`,
  `Bridge_ObjectDetail_DevuelveCampos`.

### CA-04 — Marshaling
- [ ] Ningún handler toca `IBlsVbe`/`IBlsAccess` fuera de
      `IUiScheduler.RunAsync`.
- **Test:** `Bridge_HandlerVbe_PasaPorUiScheduler` (con un
  `TestUiScheduler` que registra las invocaciones).

### CA-05 — Cancelación (ADR-003)
- [ ] `ct` cancelado antes del handler → respuesta `cancelled`, sin
      llamar a `IBlsVbe`.
- [ ] `ct` cancelado durante el handler → el handler termina; la
      respuesta se descarta si el pipe ya cerró.
- **Tests:** `Turno_CanceladoAntesDeTool_NoEjecutaTool`,
  `Turno_CanceladoDuranteToolVbe_EsperaTerminarYDescarta`.

### CA-06 — Un solo cliente
- [ ] Segundo cliente → `-32004 busy` en el primer frame, luego cierra.
- **Test:** `Frame_SegundoCliente_RespondeBusy`.

### CA-07 — Ciclo de vida
- [ ] `Start()` arranca el loop sin bloquear.
- [ ] `StopAsync(5s)` cierra el pipe y espera el task.
- [ ] `Dispose()` idempotente.
- **Tests:** `Bridge_StartArrancaLoop_NoBloquea`,
  `Bridge_StopAsync_CierraPipeYEspera`,
  `Bridge_Dispose_Idempotente`.

### CA-08 — Propagación de errores
- [ ] Toda excepción `BlsException` se mapea a código con categoría +
      mensaje original.
- [ ] Cancelación de usuario **no** produce `error`, produce `result`
      cancelado.
- **Tests:** `Bridge_HandlerFalla_DevuelveErrorNoExito`,
  `Bridge_CancelacionUsuario_NoEsError`.

---

## 8. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── Core/
│   ├── BalansoftConnect.cs                    (ya existe)
│   ├── BalansoftAddin.cs                      (ya existe)
│   ├── IBlsBridgeServer.cs                    (nuevo)
│   ├── BlsVbeBridgeServer.cs                  (nuevo)
│   └── Protocol/
│       ├── JsonRpcRequest.cs                  (nuevo)
│       ├── JsonRpcResponse.cs                 (nuevo)
│       ├── JsonRpcError.cs                    (nuevo)
│       ├── JsonRpcErrorData.cs                (nuevo)
│       ├── JsonRpcMethods.cs                  (nuevo)
│       ├── JsonRpcErrorCodes.cs               (nuevo)
│       ├── BlsPipeFraming.cs                  (nuevo)
│       ├── BlsFrameTooLargeException.cs       (nuevo)
│       ├── BlsFrameIncompleteException.cs     (nuevo)
│       └── JsonRpcDispatcher.cs               (nuevo)
└── ...

tests/Balansoft.AIAddin.Tests/
├── Core/Protocol/
│   ├── BlsPipeFramingTests.cs                 (nuevo)
│   ├── JsonRpcDeserializationTests.cs         (nuevo)
│   └── JsonRpcDispatcherTests.cs              (nuevo)
├── Core/
│   └── BlsVbeBridgeServerTests.cs             (nuevo)
└── Fakes/
    ├── MockVbe.cs                             (nuevo)
    ├── FakeAccess.cs                          (nuevo)
    ├── TestUiScheduler.cs                     (nuevo)
    └── FakeLogger.cs                          (nuevo)
```

---

## 9. Decisiones internas (no requieren ADR)

### 9.1 Un solo `Task` de aceptación, no un pool
El pipe tiene una sola instancia. No hay pool. Cuando el cliente
desconecta, se reabre la instancia en el siguiente `WaitForConnectionAsync`.
Esto simplifica el estado y evita carreras.

### 9.2 Serialización con `System.Text.Json`
Alineado con AGENTS.md y skill bls-jsonrpc-pipe-bridge. Sin Newtonsoft. Fuente única.

### 9.3 `JsonElement` para `params`, no `object`
Recibir `params` como `JsonElement?` permite deserializar al tipo
concreto por método y validar campos extra. `object` perdería esa
posibilidad.

### 9.4 Validación estricta de campos extra
Cada handler deserializa a un `record` con `[JsonExtensionData]` prohibido.
Campos desconocidos → `-32602`. Esto evita que un modelo alucine campos
y el handler los ignore silenciosamente.

### 9.5 Sin `async void` en el loop
El loop de aceptación es un `Task` que se guarda en un campo. Se espera
en `StopAsync`. Nada de `async void`.

### 9.6 `CancellationToken` del loop ≠ token del turno
- El loop tiene su propio `CancellationToken` (de `StopAsync`).
- El token del turno (ADR-003) se propaga solo a los handlers.
- Esto permite que `StopAsync` corte el loop sin afectar turnos en vuelo
  (que terminarán al cancelarse su propio token).

### 9.7 Cierre ante `frame_too_large`
Se responde `-32001` y **luego** se cierra la conexión. El cliente debe
poder leer el error antes del cierre. Esto se garantiza con un
`FlushAsync` antes del `Dispose`.

---

## 10. Fuera de alcance (recordatorio)

- Batching JSON-RPC.
- Notificaciones (sin `id`).
- Múltiples clientes.
- Compresión.
- Cifrado del pipe (confiamos en `CurrentUserOnly` + ACL del perfil).
- Reintentos automáticos en el servidor (el cliente NIM reintenta la
  tool si quiere, no el bridge).
- Streaming de respuestas (cada response = 1 frame).

---

## 11. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (reglas duras).
2. `.opencode/skills/bls-project-context/SKILL.md` (contexto general).
3. `.opencode/skills/bls-com-late-binding/SKILL.md` (envoltura COM).
4. `.opencode/skills/bls-vbe-automation/SKILL.md` (contrato `IBlsVbe`, `IUiScheduler`).
5. `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (este dominio).
6. `.opencode/skills/bls-logging-errors/SKILL.md` (mapeo de errores).
7. `.opencode/skills/bls-testing-strategy/SKILL.md` (tests obligatorios).
8. `docs/design/ADR-002-pipe-framing.md`.
9. `docs/design/ADR-003-abort-model.md`.
10. `docs/design/ADR-004-object-detail.md`.

Opcional pero recomendado:
- `.opencode/skills/bls-winforms-threading/SKILL.md` (cómo funciona el
  `IUiScheduler`).

---

## 12. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests (sin integración NIM)
dotnet test --no-build --settings .runsettings

# Filtro específico del bridge
dotnet test --no-build --filter "FullyQualifiedName~Bridge"
```

Criterio de éxito: build verde + todos los tests del bridge verdes.

---

## 13. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas (no cambiar sin volver).
- [ ] Los criterios de aceptación del §7 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados a su fuente (AGENTS/ADR).
- [ ] El alcance del §2 está claro (incluido / excluido).
- [ ] Los skills del §11 están listados.

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-08.
- [ ] No modificar ninguna interfaz del §3.
- [ ] Referenciar este doc en el commit:
      `feat: bridge JSON-RPC (refs design/20250101-vbe-bridge.md)`.

---

## 14. Referencias

- `AGENTS.md` reglas 3, 5, 8, 10.
- `docs/design/ADR-002-pipe-framing.md`.
- `docs/design/ADR-003-abort-model.md`.
- `docs/design/ADR-004-object-detail.md`.
- `PRD.md` §3.2, §7 FR-04, FR-12, §11.
- `.opencode/skills/bls-com-late-binding/SKILL.md`.
- `.opencode/skills/bls-vbe-automation/SKILL.md`.
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md`.
- `.opencode/skills/bls-logging-errors/SKILL.md`.
- `.opencode/skills/bls-testing-strategy/SKILL.md`.
```

---

# Cómo usarlo en opencode

## Paso 1 — Nemotron 3 Ultra implementa

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. docs/design/20250101-vbe-bridge.md (el diseño)
4. Los skills citados en §11 del diseño
5. Los ADRs referenciados en §14 del diseño

Implementa la feature 'VBE Bridge' exactamente como está especificado.
No cambies interfaces del §3. No añadas features del §10.
Entrega diff completo + tests + comando de verificación del §12."
```

## Paso 2 — Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/bls-workflow-nemotron/SKILL.md (checklist de rechazo)
3. docs/design/20250101-vbe-bridge.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill bls-workflow-nemotron. Si todo pasa, aprueba con
'LGTM'. Si algo falla, indica la regla violada y el skill a releer.
NO reescribas el código tú mismo."
```