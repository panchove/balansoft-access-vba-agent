# ADR-003 — Modelo de cancelación (abort)

## Estado
Aceptado — resuelve gap del PRD §3.3 y §7 FR-09.

## Fecha
2025-01-01

## Contexto
El usuario puede pulsar "Detener" en cualquier momento durante un turno de
conversación. En ese turno coexisten **dos flujos concurrentes**:

1. Llamada HTTP/2 streaming a NIM (`BlsNimClient`).
2. Ejecución de tool calls vía bridge JSON-RPC → VBE/Access.

Hay que decidir:

- **Granularidad** del cancel (por request, por turno, por sesión).
- **Propagación** del token a ambos flujos.
- **Qué hacer** si la cancelación llega cuando ya se está dentro de una
  operación VBE (no cancelable de forma segura).
- **Cómo se reporta** al usuario y al historial.

El VBE es STA y no expone cancelación cooperativa. Una operación VBE
iniciada debe terminar o dejar el proyecto en estado inconsistente.

## Decisión
**Un `CancellationTokenSource` por turno de usuario.**

### Definición de "turno"
Desde que el usuario envía un prompt (Enter) hasta que ocurre **uno** de:

- NIM devuelve `data: [DONE]` y no hay tool calls pendientes.
- El usuario pulsa "Detener".
- El usuario cierra la terminal.
- Ocurre un error fatal (auth, timeout, excepción COM no recuperable).

Un turno puede contener **múltiples round-trips** a NIM (tool calls
intermedios). El CTS es el mismo durante todo el turno.

### Propiedad del CTS
- `BlsNimTerminal` posee `_currentTurnCts` (nullable).
- Se crea **al enviar** el prompt: `_currentTurnCts = new CancellationTokenSource()`.
- Se libera al finalizar el turno: `Dispose()` + `_currentTurnCts = null`.
- Nunca se reutiliza entre turnos.

### Propagación del token
```
Terminal
  └── _currentTurnCts.Token
        ├── BlsNimClient.SendAsync(..., ct)         → cancela stream HTTP
        └── Bridge client.InvokeAsync(method, params, ct)
                └── BlsVbeBridgeServer.DispatchAsync(..., ct)
                        └── Handler VBE/Access (respeta ct si aún no entró)
```

### Semántica de cancelación por capa

| Capa                            | Comportamiento al cancelar                                                                                                                                     |
|---------------------------------|----------------------------------------------------------------------------------------------------------------------------------------------------------------|
| **BlsNimClient**                | Cierra el stream HTTP, libera el `HttpResponseMessage`, emite `OnCancelled()`.                                                                                 |
| **Bridge client**               | Aborta la espera de respuesta; no envía más frames; el servidor puede seguir procesando (ver abajo).                                                           |
| **Bridge server (handler VBE)** | Si el token se cancela **antes** de entrar a la operación VBE → aborta sin tocar el VBE. Si ya entró → **deja terminar** la operación y descarta la respuesta. |
| **Handler Access read-only**    | Igual que VBE: si ya entró, termina; si no, aborta.                                                                                                            |
| **Terminal**                    | Muestra "Cancelado por el usuario", deshabilita el botón, libera el CTS.                                                                                       |

### Por qué "dejar terminar" una operación VBE
- El VBE no es reentrante ni cancelable.
- Abortar a mitad de `WriteModule` puede dejar el módulo en estado
  parcial (código truncado).
- Mejor terminar y descartar la respuesta que corromper el proyecto.
- Coste: latencia máxima de una operación VBE (típicamente < 200 ms).

### Timeouts independientes (NO son cancelación de usuario)
Estos timeouts **no** activan `OnCancelled()`; activan errores tipados:

| Timeout                       | Valor | Excepción                            |
|-------------------------------|-------|--------------------------------------|
| Chunk HTTP sin datos          | 120 s | `BlsNimTimeoutException`             |
| Tool sin respuesta del bridge | 30 s  | `-32002 tool_timeout` (JSON-RPC)     |
| Frame incompleto en el pipe   | 30 s  | `-32003 frame_incomplete` (JSON-RPC) |
| Pipe idle entre frames        | —     | No cierra (ver ADR-002)              |

### Diferencia cancelación vs timeout (crítica)
- **Cancelación**: acción del usuario. Se reporta como `OnCancelled()`,
  se loguea `INFO`, no se envía al modelo como fallo.
- **Timeout**: fallo del sistema/red. Se reporta como error tipado, se
  loguea `WARN`/`ERROR`, y el modelo **sí** lo ve (puede reintentar o
  informar al usuario).

### Reporte al usuario y al historial
- UI: mensaje "Cancelado por el usuario" en color neutro (no rojo).
- Historial: el turno se guarda con `"cancelled": true` en el último
  mensaje `assistant`. El contenido parcial recibido se conserva.
- Log: `INFO [BlsNimTerminal] Turno cancelado por el usuario (turnId=...)`.

### Casos límite

1. **Cancelar antes de que NIM responda nada**: no se añade mensaje
   `assistant`; solo se marca el turno como cancelado.
2. **Cancelar durante un tool call en vuelo**: el bridge puede terminar
   la operación VBE. El historial guarda el tool result si llegó antes
   del cancel; si no, se marca `"tool_cancelled": true`.
3. **Cerrar la terminal con turno activo**: se cancela el CTS, se espera
   2 s (skill bls-winforms-threading), luego `Dispose`. Si el bridge no responde, se descarta.
4. **Pulsar "Detener" dos veces**: el segundo click es no-op (CTS ya
   cancelado o `null`).
5. **Cancelación durante retry de NIM (429/5xx)**: el retry respeta el
   token; no reintenta si está cancelado.

## Alternativas descartadas

| Alternativa                                 | Motivo de descarte                                      |
|---------------------------------------------|---------------------------------------------------------|
| CTS por request HTTP                        | El usuario cancela el turno completo, no un round-trip. |
| CTS global de sesión                        | No permite cancelar un turno sin cerrar la terminal.    |
| Abortar operaciones VBE a mitad             | Riesgo de corrupción del proyecto VBA.                  |
| `Thread.Abort`                              | Prohibido en .NET moderno, peligroso con COM.           |
| Esperar a que NIM termine antes de cancelar | Ignora la intención del usuario.                        |

## Consecuencias

### Positivas
- Un solo botón "Detener" cancela todo el turno, sin ambigüedad.
- VBE nunca queda en estado parcial.
- Cancelación y timeout son distinguibles en UI, log e historial.
- El modelo puede recibir tool results parciales si llegaron antes del
  cancel (mejor contexto).

### Negativas / mitigaciones
- **Latencia de cancelación** = tiempo de la operación VBE en curso
  (típicamente < 200 ms). Aceptable.
- **Historial puede quedar "a medias"** con `"cancelled": true`. Es
  informativo, no un bug.
- **No hay cancel selectivo** (ej. cancelar solo el tool call). Fuera de
  MVP; si se necesita, se añade después con un CTS por tool.

## Implementación

```csharp
// BlsNimTerminal
private CancellationTokenSource? _currentTurnCts;

private async Task SendPromptAsync(string prompt)
{
    if (_currentTurnCts != null) return; // turno en curso
    _currentTurnCts = new CancellationTokenSource();
    var ct = _currentTurnCts.Token;
    try
    {
        await _nimClient.SendAsync(prompt, ct);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        AppendStatus("Cancelado por el usuario");
        _logger.Info("Turno cancelado por el usuario");
    }
    catch (BlsNimTimeoutException ex)
    {
        AppendError(ex.Message); // timeout ≠ cancelación
    }
    finally
    {
        _currentTurnCts?.Dispose();
        _currentTurnCts = null;
    }
}

private void OnDetenerClick(object sender, EventArgs e)
{
    _currentTurnCts?.Cancel();
}
```

```csharp
// BlsVbeBridgeServer handler típico
public async Task<JsonRpcResponse> WriteModuleAsync(Params p, CancellationToken ct)
{
    if (ct.IsCancellationRequested)
        return JsonRpcResponse.Cancelled(p.Id);

    return await _uiScheduler.RunAsync(() =>
    {
        // Ya dentro del hilo UI: no comprobamos ct más.
        _vbeService.WriteModule(p.Name, p.Code);
        return JsonRpcResponse.Ok(p.Id);
    });
}
```

## Tests obligatorios
- `Turno_CanceladoDuranteStream_DetieneYEmitCancelled`
- `Turno_CanceladoAntesDeTool_NoEjecutaTool`
- `Turno_CanceladoDuranteToolVbe_EsperaTerminarYDescarta`
- `Turno_TimeoutChunk_LanzaTimeoutNoCancelled`
- `Turno_DobleDetener_EsNoOp`
- `Turno_CerrarTerminalConTurnoActivo_CancelaYDispose`
- `Turno_CanceladoDuranteRetry_NoReintenta`
- `Turno_Cancelado_HistorialMarcaCancelled`

## Referencias
- PRD §3.3 (cliente NIM), §7 FR-09.
- `.opencode/skills/bls-nim-client-streaming/SKILL.md` (token en streaming).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (tool_timeout y dispatch).
- `.opencode/skills/bls-winforms-threading/SKILL.md` (botón Detener, cierre de ventana).
- `.opencode/skills/bls-logging-errors/SKILL.md` (INFO vs ERROR).
- ADR-002 (códigos -32002, -32003).
