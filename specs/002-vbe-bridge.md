# 002 — Bridge VBE

**Estado:** `aceptado`
**PRD:** FR-04, FR-03
**ADRs:** ADR-001 (identidad de proyecto), ADR-002 (framing)
**Spec de diseño:** `docs/design/20250101-vbe-bridge.md`
**Tests:** 46 verdes — 12 en `BlsVbeBridgeServerTests`, 17 en
  `JsonRpcDispatcherTests`, 10 en `BlsPipeFramingTests`, 7 en
  `JsonRpcDeserializationTests`

---

## Contexto

El bridge expone el proyecto VBA a la Terminal a través de JSON-RPC 2.0 sobre
named pipe. Es el único camino por el que el modelo puede leer o modificar
código VBA, y por tanto la superficie que decide si el modelo puede hacer
daño.

El servidor, el dispatcher, el framing y el cliente están implementados y
testados. Lo que **no** existe es la implementación real de `IBlsVbe`: solo
hay interfaz y `MockVbe` de test.

---

## Alcance

**Entra:**

- Servidor JSON-RPC sobre named pipe (`BlsVbeBridgeServer`).
- Dispatcher con whitelist de métodos (`JsonRpcDispatcher`).
- Framing `[uint32 LE][UTF-8 JSON]` con máximo 1 MB (ADR-002).
- 7 métodos de lectura/escritura del proyecto VBA.
- Identidad estable de proyecto para el historial (ADR-001).
- Cliente del bridge (`BlsBridgeClient`).
- Regla de `busy` y cancelación por turno (ADR-003).

**No entra:**

- Los 3 métodos de Access de solo lectura (spec 007).
- Cliente NIM (spec 003).

---

## Requisitos

### R001 — Framing (ADR-002)

- Payload = 4 bytes de longitud en little-endian, seguido de JSON UTF-8.
- Longitud máxima 1 MB. Un frame mayor lanza `BlsFrameTooLargeException`
  (`-32001`).
- Lectura parcial: EOF a mitad de header o payload lanza
  `BlsFrameIncompleteException` (`-32003`).
- EOF limpio al inicio de frame devuelve null y cierra la conexión.
- La longitud se cuenta en **bytes**, no en caracteres: un payload con
  acentos o emojis debe leerse correctamente.
- Los newlines dentro del payload no rompen el framing.
- Timeout de inactividad: mitir frame ⇒ cierra; estar simplemente idle entre
  frames ⇒ **no** cierra. Un add-in que no recibe peticiones durante horas no
  debe perder la conexión.

### R002 — Whitelist de métodos

Los 10 métodos viven en `JsonRpcMethods.All`. Cualquier otro responde
`-32601` (Method Not Found) sin tocar el VBE.

VBE (7): `list_modules`, `read_module`, `write_module`, `create_module`,
`delete_module`, `get_selection`, `get_active_module`.

### R003 — Marshaling al hilo de UI (P5)

Todo handler que toque el VBE se despacha vía `IUiScheduler`. El dispatcher
no invoca un handler de VBE desde el hilo de lectura del pipe.

`Bridge_HandlerVbe_PasaPorUiScheduler` lo verifica.

### R004 — Errores reales, nunca éxito falso (P8)

- Excepción COM en un handler → respuesta de error con `category` y mensaje
  original. Nunca `result` con un valor inventado.
- Excepción no controlada → `-32603` (Internal Error) y se registra en el logger.
- Excepción tipada (`BlsVbeException`, `BlsComException`, …) conserva su
  categoría en `JsonRpcErrorData.Category`; no se degrada a internal error.

### R005 — Cancelación (ADR-003)

- Un token por turno. Si se cancela antes de ejecutar un handler, el handler
  **no** se invoca.
- La cancelación no es error: la respuesta lleva `result.cancelled`, no `error`.

### R006 — `busy`

Un solo cliente activo. Un segundo cliente conectado recibe
`-32004` (Bridge Busy) y su pipe se cierra.

### R007 — Identidad de proyecto (ADR-001)

`GetProjectKey()` devuelve `SHA256` de la ruta normalizada del proyecto,
identificador estable entre sesiones. Sin ruta (proyecto sin guardar) marca
`Ephemeral` y el historial no se persiste.

### R008 — Nombre del pipe

`BlsPipeFraming.NormalizePipeName` acepta tanto `balansoft-opencode` como
`\\.\pipe\balansoft-opencode` y devuelve siempre la forma sin prefijo, porque
`NamedPipeServerStream` y `NamedPipeClientStream` anteponen el prefijo por su
cuenta. Pasar el nombre completo lo duplica y servidor y cliente terminan en
pipes distintos: nunca coinciden.

### R009 — instancias del pipe

`maxNumberOfServerInstances` es 2. Con un cliente conectado queda 1 instancia
libre. El accept loop crea **una instancia por iteración** y reintenta si
`CreatePipe` devuelve null; nunca hace `break`, porque eso cerraría el pipe del
cliente entrante y el segundo recibiría EOF en lugar de `busy`.

### R010 — Cliente: un request, un response

El protocolo es estrictamente secuencial. `BlsBridgeClient` serializa las
invocaciones con un `SemaphoreSlim` para que dos llamadas concurrentes no
intercalen frames ni roben la respuesta de la otra.

Un pipe roto o una cancelación fuerzan desconexión para la siguiente llamada
reconecte.

---

## Criterios de aceptación

```
Dado un frame con payload UTF-8 multibyte
Cuando se escribe y se lee por el pipe
Entonces el payload llega idéntico

Dado un payload de más de 1 MB
Cuando se envía
Entonces el servidor responde -32001 y cierra

Dado un método fuera de la whitelist
Cuando se invoca por JSON-RPC
Entonces responde -32601 sin tocar el VBE

Dado un handler de VBE que lanza COMException
Cuando se invoca
Entonces la respuesta tiene error con category y el mensaje original
Y nunca tiene result

Dado un turno cancelado antes de un handler de VBE
Cuando llega la petición
Entonces el handler no se ejecuta
Y la respuesta indica cancelación, no error

Dado un cliente ya conectado
Cuando un segundo cliente se conecta
Entonces recibe -32004 y su pipe se cierra

Dado un servidor construido con el nombre con prefijo \\.\pipe\
Y un cliente construido con el nombre sin prefijo
Entonces ambos conectan al mismo pipe

Dado un cliente conectado
Cuando se envían dos peticiones a la vez
Entonces cada una recibe su propia respuesta sin frames intercalados
```

---

## Tests obligatorios

Ya implementados y verdes:

| Test | Cubre |
|---|---|
| `Frame_EnvioSimple_LeeMismoPayload` | R001 |
| `Frame_PayloadUtf8Multibyte_LengthEsEnBytes` | R001 |
| `Frame_PayloadConNewlines_NoRompeFraming` | R001 |
| `Frame_Excede1MB_CierraConError` | R001 |
| `Frame_HeaderParcial_EsperaResto` | R001 |
| `Frame_PayloadParcial_EsperaResto` | R001 |
| `Frame_Vacio_RespondeInvalidRequest` | R001 |
| `Frame_JsonInvalido_RespondeInvalidRequest` | R001 |
| `Frame_TimeoutInactivo_MitadDeFrame_Cierra` | R001 |
| `Frame_TimeoutIdle_EntreFrames_NoCierra` | R001 |
| `Bridge_MetodoDesconocido_RespondeMethodNotFound` | R002 |
| `Bridge_ParamsInvalidos_RespondeInvalidParams` | R002 |
| `Bridge_ListModules_DevuelveLista` | R002 |
| `Bridge_ReadModule_DevuelveCodigo` | R002 |
| `Bridge_WriteModule_EscribeEnVbe` | R002 |
| `Bridge_CreateModule_CreaEnVbe` | R002 |
| `Bridge_DeleteModule_ConConfirm_Borra` | R002 |
| `Bridge_DeleteModule_SinConfirm_RespondeInvalidParams` | R002 |
| `Bridge_GetSelection_DevuelveRango` | R002 |
| `Bridge_GetActiveModule_DevuelveInfo` | R002 |
| `Bridge_HandlerVbe_PasaPorUiScheduler` | R003 |
| `Bridge_HandlerFalla_DevuelveErrorNoExito` | R004 |
| `Bridge_ErrorTipado_NoSeReportaComoInternalError` | R004 |
| `Bridge_ExcepcionNoControlada_RegistraEnLogger` | R004 |
| `Bridge_ExcepcionNoControlada_RespondeInternalError` | R004 |
| `JsonRpcErrorData_SoloStackEnDebug` | R004 |
| `JsonRpcResponse_SerializaCancelled` | R005 |
| `Turno_CanceladoAntesDeTool_NoEjecutaTool` | R005 |
| `Turno_NoCancelado_EjecutaTool` | R005 |
| `Bridge_CancelacionUsuario_NoEsError` | R005 |
| `Frame_SegundoCliente_RespondeBusy` | R006 |
| `Bridge_ClienteReal_ConectaYRecibeRespuesta` | R008 |
| `Bridge_DosInvocacionesConcurrentes_NoIntercalanFrames` | R010 |
| `Bridge_StartArrancaLoop_NoBloquea` | R003 |
| `Bridge_StopAsync_CierraPipeYEspera` | R003 |
| `Bridge_Dispose_Idempotente` | R003 |
| `JsonRpcRequest_DeserializaConIdNumerico` | R001 |
| `JsonRpcRequest_DeserializaConIdString` | R001 |
| `JsonRpcRequest_ParamsNull_DeserializaComoNull` | R001 |
| `JsonRpcResponse_SerializaOk` | R001 |
| `JsonRpcResponse_SerializaError` | R004 |

**Pendiente** (la CI los busca por nombre y hoy avisa):
`ProjectKey_BaseGuardada_RetornaHash32Hex`,
`ProjectKey_BaseSinGuardar_RetornaHashConFlagEphemeral`,
`ProjectKey_DescriptionOverride_UsaValorDeclarado`.

---

## Desviaciones y riesgos

- **Falta la implementación real de `IBlsVbe`.** Solo hay `MockVbe`. El
  bridge está probado contra un doble, no contra el VBE. Ningún test de este
  spec puede revelar un fallo de enlace tardío.
- **`BlsComHelper` no existe.** P4 exige que el `dynamic` viva ahí, pero el
  archivo no está. Hoy no hay `dynamic` en el proyecto, así que la regla se
  cumple por vacío.
- **`JsonRpcErrorData` expone `Category` y `Op`**, no `Kind` ni `Method`.
  Documentado para evitar confusion al escribir tests.
- **`IBlsVbe.GetProjectKey()`** ya existe en la interfaz y `MockVbe` lo
  soporta, así que R007 está a medio implementar: falta la versión real.
- **`Frame_Vacio_RespondeInvalidRequest` y `Frame_JsonInvalido_RespondeInvalidRequest`**
  existen en dos clases a la vez (`BlsVbeBridgeServerTests` y
  `BlsPipeFramingTests`). MSTest los ejecuta como dos tests distintos porque
  viven en clases distintas, así que no rompen la suite, pero el nombre ya no
  identifica un test. Conviene renombrar uno de los dos.
- **La CI busca dos tests de ADR-003 con otros nombres:**
  `Turno_CanceladoDuranteStream_DetieneYEmitCancelled` y
  `Turno_TimeoutChunk_LanzaTimeoutNoCancelled`. Los equivalentes reales son
  `SendAsync_CanceladoDuranteBackoff_NoReintenta` y
  `SendAsync_SinDatos_LanzaTimeoutReportadoComoError`. O se renombran los
  tests, o se corrige la lista de la CI. Hoy la CI avisa de 7 nombres
  ausentes.