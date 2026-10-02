# SKILL 04 — Bridge JSON-RPC 2.0 sobre Named Pipe

> **Cuándo cargar este skill:** al implementar o modificar
> `Core/BlsVbeBridgeServer.cs`, el framing del pipe, el dispatch de
> métodos, la whitelist, o cualquier handler que ejecute operaciones VBE
> o Access a petición del modelo.

---

## Transporte (ADR-002)

- **Pipe:** `\\.\pipe\balansoft-opencode`
- **Modo:** `PipeTransmissionMode.Message`
- **Opciones:** `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`
- **Dirección:** `PipeDirection.InOut`
- **Instancias:** 1 (un solo cliente a la vez)

`CurrentUserOnly` es **obligatorio**: garantiza que solo procesos del
mismo usuario puedan conectarse (evita que otro usuario de la máquina
inyecte comandos VBE).

---

## Framing (ADR-002)

```
+-------------------+------------------------------+
|  uint32 LE length |  bytes UTF-8 JSON payload    |
|  (4 bytes)        |  (length bytes)              |
+-------------------+------------------------------+
```

- `length` = bytes UTF-8 del payload, sin contar el header.
- Un frame = un mensaje JSON-RPC 2.0 completo.
- Máximo 1 MB por frame → `-32001 frame_too_large` + cierre.
- `length == 0` → `-32600 empty_frame`, no cerrar.
- Sin batching (arrays JSON-RPC → `-32600`).

### Lectura canónica
```csharp
private static async Task<byte[]?> ReadExactAsync(
    Stream s, int count, CancellationToken ct)
{
    var buf = new byte[count];
    int read = 0;
    while (read < count)
    {
        int n = await s.ReadAsync(buf, read, count - read, ct);
        if (n == 0) return null;   // EOF
        read += n;
    }
    return buf;
}
```

### Escritura canónica
```csharp
private static async Task WriteFrameAsync(
    Stream s, object payload, CancellationToken ct)
{
    var bytes  = JsonSerializer.SerializeToUtf8Bytes(payload);
    var header = BitConverter.GetBytes((uint)bytes.Length);
    await s.WriteAsync(header, 0, 4, ct);
    await s.WriteAsync(bytes, 0, bytes.Length, ct);
    await s.FlushAsync(ct);
}
```

---

## Protocolo JSON-RPC 2.0

### Request
```json
{ "jsonrpc":"2.0", "id":1, "method":"write_module",
  "params":{ "name":"Utils", "code":"..." } }
```

### Response OK
```json
{ "jsonrpc":"2.0", "id":1, "result":{ ... } }
```

### Response Error
```json
{ "jsonrpc":"2.0", "id":1,
  "error":{ "code":-32000, "message":"...", "data":{ ... } } }
```

### Reglas
- `id` se replica **tal cual** (número o string).
- `jsonrpc` debe ser exactamente `"2.0"`; si no → `-32600`.
- `method` fuera de la whitelist → `-32601`.
- `params` con forma incorrecta → `-32602`.
- Nunca se hace *notification* (siempre hay respuesta; no hay métodos
  sin `id` en MVP).

---

## Whitelist de métodos (10)

### VBE (7)
| Método              | Handler                         | Params                                                                    |
|---------------------|---------------------------------|---------------------------------------------------------------------------|
| `list_modules`      | `BlsVbeService.ListModules`     | `{}`                                                                      |
| `read_module`       | `BlsVbeService.ReadModule`      | `{ "name": string }`                                                      |
| `write_module`      | `BlsVbeService.WriteModule`     | `{ "name": string, "code": string }`                                      |
| `create_module`     | `BlsVbeService.CreateModule`    | `{ "name": string, "code": string, "type": "Standard"\|"Class"\|"Form" }` |
| `delete_module`     | `BlsVbeService.DeleteModule`    | `{ "name": string, "confirm": true }`                                     |
| `get_selection`     | `BlsVbeService.GetSelection`    | `{}`                                                                      |
| `get_active_module` | `BlsVbeService.GetActiveModule` | `{}`                                                                      |

### Access read-only (3)
| Método            | Handler                                 | Params                                                                       |
|-------------------|-----------------------------------------|------------------------------------------------------------------------------|
| `project_summary` | `BlsAccessObjectService.ProjectSummary` | `{}`                                                                         |
| `list_objects`    | `BlsAccessObjectService.ListObjects`    | `{}`                                                                         |
| `object_detail`   | `BlsAccessObjectService.ObjectDetail`   | `{ "name": string, "type": string, "includeSql"?: bool, "maxFields"?: int }` |

Cualquier otro → `-32601 method_not_found`.

---

## Códigos de error reservados

| Code   | Name              | Cuándo                                                   |
|--------|-------------------|----------------------------------------------------------|
| -32600 | Invalid Request   | JSON inválido, batching, `jsonrpc != "2.0"`, frame vacío |
| -32601 | Method not found  | `method` fuera de la whitelist                           |
| -32602 | Invalid params    | `params` no cumple el contrato                           |
| -32603 | Internal error    | Excepción no controlada en el handler                    |
| -32000 | Application error | Error COM/VBE/Access con mensaje original                |
| -32001 | frame_too_large   | `length > 1 MB`                                          |
| -32002 | tool_timeout      | Tool sin respuesta en 30 s (ADR-003)                     |
| -32003 | frame_incomplete  | Timeout a mitad de frame (30 s)                          |
| -32004 | busy              | Ya hay un cliente activo                                 |

Los códigos `-32000` a `-32004` van en `data.category` para que el
modelo pueda decidir:

```json
{ "jsonrpc":"2.0", "id":1, "error":{
    "code": -32000,
    "message": "Módulo 'X' no encontrado",
    "data": { "category": "VbeException", "op": "write_module" }
} }
```

---

## Ciclo de vida del servidor

### Arranque
- `Start()` se llama en `AttachVbe` (después de obtener VBE y arrancar
  el menú Tools).
- Crea `NamedPipeServerStream` y lanza un `Task` con
  `TaskCreationOptions.LongRunning` para el loop de aceptación.
- Una sola instancia de pipe (un cliente).

### Loop
```
while (!ct.IsCancellationRequested):
    pipe = new NamedPipeServerStream(PipeName, InOut, 1, Message,
                                     Asynchronous, CurrentUserOnly)
    await pipe.WaitForConnectionAsync(ct)
    await HandleClientAsync(pipe, ct)   // bloquea hasta desconexión
    pipe.Dispose()
```

### Parada
- `Stop()` en `OnDisconnection` y `OnBeginShutdown`.
- Cancela el `CancellationToken` del loop.
- Cierra el pipe (si hay cliente, desconecta).
- `await` del task con timeout 5 s.

---

## Concurrencia

El VBE no es reentrante. Todas las operaciones VBE se serializan:

```csharp
private readonly SemaphoreSlim _vbeGate = new(1, 1);

private async Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest req, CancellationToken ct)
{
    return req.Method switch
    {
        "list_modules"      => await HandleVbeAsync(ct, () => _vbe.ListModules()),
        "write_module"      => await HandleVbeAsync(ct, () => _vbe.WriteModule(...)),
        "object_detail"     => await HandleAccessAsync(ct, () => _access.ObjectDetail(...)),
        // ...
        _ => JsonRpcResponse.Error(req.Id, -32601, "method_not_found", req.Method)
    };
}

private async Task<JsonRpcResponse> HandleVbeAsync<T>(
    CancellationToken ct, Func<T> op)
{
    if (ct.IsCancellationRequested)
        return JsonRpcResponse.Cancelled(ct);

    await _vbeGate.WaitAsync(ct);
    try
    {
        var result = await _ui.RunAsync(op);   // marshaling al hilo UI
        return JsonRpcResponse.Ok(result);
    }
    finally { _vbeGate.Release(); }
}
```

**Reglas:**
- Un solo handler VBE en vuelo (semáforo).
- El handler **no** corre dentro del callback de lectura del pipe; se
  encola en un worker (evita bloquear el loop mientras VBE trabaja).
- El token del turno (ADR-003) se propaga al handler.
- Si el token se cancela antes de entrar al VBE → respuesta `cancelled`
  sin ejecutar.
- Si ya entró → termina, y la respuesta se descarta si el cliente ya
  canceló (protocolo: no enviar respuesta si la conexión cerró).

---

## Marshaling al hilo UI

`BlsVbeBridgeServer` recibe `IUiScheduler` (skill 02). **Ningún** handler
llama a `BlsVbeService` directamente desde el hilo del pipe.

```
Pipe thread  →  DispatchAsync  →  _vbeGate  →  _ui.RunAsync  →  UI thread
```

---

## Seguridad

1. `PipeOptions.CurrentUserOnly` obligatorio.
2. Whitelist de `method` **antes** de despachar.
3. Validación estricta de `params` por método (rechazar campos extra no
   documentados).
4. Nunca aceptar rutas de archivo arbitrarias en `params`.
5. Nunca exponer `Application.Run` ni equivalente.
6. Todo error COM/Access se devuelve con mensaje original **sin** stack
   en Release (stack solo en Debug).
7. El servidor no loguea payloads completos de código VBA (solo longitud
   y primeras 80 chars).

---

## Anti-patrones prohibidos (rechazo automático en PR)

| Anti-patrón                                                | Por qué                                   |
|------------------------------------------------------------|-------------------------------------------|
| Framing por línea (`\n`)                                   | Rompe con código VBA multilínea (ADR-002) |
| Confiar solo en `PipeTransmissionMode.Message` sin framing | Sin garantía a nivel app                  |
| Ejecutar handler en el callback de lectura                 | Bloquea el pipe                           |
| Sin `SemaphoreSlim` para VBE                               | Reentrancia → corrupción                  |
| Tocar VBE desde el hilo del pipe                           | Crash del host (skill 01/06)              |
| Sin whitelist de métodos                                   | Superficie de ataque                      |
| Devolver stack en Release                                  | Fuga de información                       |
| Ignorar `-32002 tool_timeout`                              | Viola ADR-003                             |
| Cerrar el pipe por idle entre frames                       | Viola ADR-002                             |

---

## Checklist antes de commitear cambios en el bridge

- [ ] ¿El framing es `[uint32 LE][UTF-8 JSON]` (ADR-002)?
- [ ] ¿`CurrentUserOnly` está activado?
- [ ] ¿La whitelist cubre exactamente los 10 métodos?
- [ ] ¿Todos los handlers VBE pasan por `_vbeGate` + `_ui.RunAsync`?
- [ ] ¿El token del turno (ADR-003) se propaga?
- [ ] ¿La cancelación antes del VBE devuelve `cancelled` sin ejecutar?
- [ ] ¿Los códigos de error son los reservados?
- [ ] ¿`frame_too_large` responde **antes** de cerrar?
- [ ] ¿`Stop()` cancela el loop y espera con timeout 5 s?
- [ ] ¿No se loguea el payload completo de código VBA?

---

## Referencias

- `AGENTS.md` reglas 1, 2, 3, 5.
- `docs/design/ADR-002-pipe-framing.md`.
- `docs/design/ADR-003-abort-model.md`.
- `docs/design/ADR-004-object-detail.md`.
- PRD §3.2, §7 FR-04, FR-12.
- `skills/01-com-late-binding.md`.
- `skills/02-vbe-automation.md` (contrato `IBlsVbe`).
- `skills/03-nim-client-streaming.md` (origen de los tool calls).
- `skills/06-winforms-threading.md` (marshaling).
- `skills/08-logging-errors.md` (propagación).
- `skills/09-testing-strategy.md` (fake `PipeStream`).
