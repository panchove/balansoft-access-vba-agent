# ADR-002 — Framing del Named Pipe JSON-RPC

## Estado
Aceptado — resuelve gap del PRD §3.2.

## Fecha
2025-01-01

## Contexto
El PRD especifica:

- Named Pipe `\\.\pipe\balansoft-opencode`
- JSON-RPC 2.0 con 10 métodos (7 VBE + 3 Access)
- Uso desde `BlsVbeBridgeServer`

Pero **no define cómo delimitar mensajes**. Los payloads incluyen código
VBA con `\n`, `\r`, comillas, y strings arbitrarios. Un delimitador por
línea es frágil. Necesitamos un framing determinista, eficiente y trivial
de parsear en .NET Framework 4.8.

## Decisión
**Framing por longitud prefijada, pipe en modo mensaje.**

### Transporte
- Pipe: `\\.\pipe\balansoft-opencode`
- `PipeTransmissionMode.Message`
- `PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`
- `PipeDirection.InOut`

### Frame
```
+-------------------+------------------------------+
|  uint32 LE length |  bytes UTF-8 JSON payload    |
|  (4 bytes)        |  (length bytes)              |
+-------------------+------------------------------+
```

- `length` = tamaño del payload JSON en **bytes** (UTF-8), no en chars.
- No incluye los 4 bytes del header.
- Un frame = **un mensaje JSON-RPC completo** (request o response).
- Sin frames parciales: el lector espera hasta tener header + payload.
- Sin JSON-RPC batching: arrays de requests → `-32600` (Invalid Request).

### Límites
- Máximo por frame: **1 MB** (1 048 576 bytes).
- Si `length > 1 MB` → responder `-32001 "frame_too_large"` y **cerrar**
  la conexión (no intentar recuperar).
- Si `length == 0` → `-32600 "empty_frame"`, no cerrar (podría ser un
  cliente buggy pero vivo).
- Mínimo razonable: 2 bytes (`{}`). Frames de 1 byte → `-32600`.

### Timeouts
- **Lectura inactiva**: 30 s sin recibir ningún byte de un frame en curso.
  Si expira **a mitad de frame** → cerrar conexión con `-32003
  "frame_incomplete"`.
- Si expira **entre frames** (idle) → **no cerrar** (el cliente puede
  estar pensando con NIM).
- **Escritura**: 10 s por frame. Si el pipe está lleno y no drena,
  cerrar conexión.

### Serialización
- `System.Text.Json` (única dependencia, sin Newtonsoft).
- `JsonSerializerOptions`:
  - `PropertyNamingPolicy = null` (respetar nombres JSON-RPC exactos).
  - `DefaultIgnoreCondition = WhenWritingNull`.
  - `WriteIndented = false`.
- Las respuestas de error siguen el esquema JSON-RPC 2.0.

### Lectura (algoritmo canónico)
```
loop:
    header = ReadExactly(4)            // puede bloquear hasta 30s
    length = BitConverter.ToUInt32(header, 0)
    if length == 0: emit -32600; continue
    if length > MAX: emit -32001; close; return
    payload = ReadExactly(length)      // puede bloquear hasta 30s
    msg = JsonSerializer.Deserialize(payload)
    dispatch(msg)
```

`ReadExactly` es un helper propio (no existe en .NET 4.8) que garantiza
leer N bytes o fallar.

### Escritura (algoritmo canónico)
```
bytes = JsonSerializer.SerializeToUtf8Bytes(response)
header = BitConverter.GetBytes((uint)bytes.Length)
Write(header)
Write(bytes)
Flush()
```

## Alternativas descartadas

| Alternativa                                    | Motivo de descarte                                                                                                         |
|------------------------------------------------|----------------------------------------------------------------------------------------------------------------------------|
| Newline-delimited (`\n`)                       | Rompe con strings multilínea (código VBA).                                                                                 |
| `\0`-delimited                                 | Rompe si el payload contiene NUL legítimo (raro pero posible).                                                             |
| Content-Length estilo HTTP                     | Redundante, más parsing, sin ventaja.                                                                                      |
| Confiar solo en `PipeTransmissionMode.Message` | No garantiza límites a nivel de aplicación si el cliente escribe en varias `Write()` calls; necesitamos framing explícito. |
| WebSocket / gRPC                               | Overkill para un pipe local; dependencias extra.                                                                           |
| Protocol Buffers                               | No es JSON-RPC 2.0 estándar; dificulta debugging.                                                                          |

## Consecuencias

### Positivas
- Determinista y sin ambigüedad con strings arbitrarios.
- Trivial de implementar y testear en .NET 4.8.
- Compatible con cualquier cliente que respete el framing (PowerShell,
  Python, Node, otro .NET).
- Debugging sencillo: se puede dumpear header + payload como hex.

### Negativas / mitigaciones
- **Cliente debe conocer el framing** para hablar el protocolo.
  - Mitigación: helper `BlsPipeClient` reutilizable en tests y en
    herramientas de diagnóstico.
- **1 MB limita payloads grandes** (ej. módulo VBA muy grande).
  - Mitigación: `write_module` con código > 1 MB se rechaza con error
    claro; el caller debe dividir en `create_module` + `write_module`
    parciales (fuera de MVP, o el modelo lo hace en turnos).
- **Cierre forzado en `frame_too_large`** puede sorprender al cliente.
  - Mitigación: responder el error **antes** de cerrar.

## Reglas para implementación
1. `PipeOptions.CurrentUserOnly` es **obligatorio** (seguridad).
2. Un solo cliente activo. Segundo `CreateFile` sobre el mismo pipe:
   el servidor mantiene la conexión anterior y responde al nuevo con
   `-32004 "busy"` en el primer frame, luego cierra.
3. El listener corre en hilo dedicado (`Task` con
   `TaskCreationOptions.LongRunning`).
4. El despacho al hilo UI (VBE) se hace vía el scheduler del skill bls-vbe-automation.
5. Nunca bloquear el hilo del pipe con operaciones VBE lentas: usar cola
   + worker (ver skill bls-jsonrpc-pipe-bridge).
6. Toda excepción no controlada en el loop de lectura:
   - log `ERROR`
   - intentar enviar `-32603 "internal_error"` si el pipe sigue vivo
   - cerrar y reiniciar listener

## Códigos de error reservados (JSON-RPC)

| Code   | Name              | Cuándo                                    |
|--------|-------------------|-------------------------------------------|
| -32600 | Invalid Request   | Frame vacío, batching, JSON no parseable  |
| -32601 | Method not found  | `method` fuera de la whitelist de 10      |
| -32602 | Invalid params    | `params` no cumple el contrato del método |
| -32603 | Internal error    | Excepción no controlada en el handler     |
| -32000 | Application error | Error COM/Access/VBE con mensaje original |
| -32001 | frame_too_large   | `length > 1 MB`                           |
| -32002 | tool_timeout      | Tool sin respuesta en 30 s (ver ADR-003)  |
| -32003 | frame_incomplete  | Timeout a mitad de frame                  |
| -32004 | busy              | Ya hay un cliente activo                  |

## Tests obligatorios
- `Frame_EnvioSimple_LeeMismoPayload`
- `Frame_PayloadConNewlines_NoRompeFraming`
- `Frame_PayloadUtf8Multibyte_LengthEsEnBytes`
- `Frame_Excede1MB_CierraConError` (emite -32001 y cierra)
- `Frame_HeaderParcial_EsperaResto`
- `Frame_PayloadParcial_EsperaResto`
- `Frame_Vacio_RespondeInvalidRequest`
- `Frame_JsonInvalido_RespondeInvalidRequest`
- `Frame_SegundoCliente_RespondeBusy`
- `Frame_TimeoutInactivo_MitadDeFrame_Cierra`
- `Frame_TimeoutIdle_EntreFrames_NoCierra`

## Referencias
- PRD §3.2 (bridge JSON-RPC), §7 FR-04.
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (implementación).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (tests).
- ADR-003 (abort y tool_timeout).
