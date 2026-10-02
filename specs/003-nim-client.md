# 003 — Cliente NIM

**Estado:** `implementado`
**PRD:** FR-09
**ADRs:** ADR-003 (abort)
**Spec de diseño:** `docs/design/20250102-nim-client.md`
**Tests:** 53 verdes — 28 en `BlsNimClientTests`, 14 en `NimStreamParserTests`,
  11 en `NimHttpRequestBuilderTests`

---

## Contexto

El cliente habla con `https://integrate.api.nvidia.com/v1/chat/completions`
usando streaming SSE. Es el único componente que ve la API key y el único que
consume red.

Implementado y testado con un `FakeHttpHandler` inyectado, sin llamadas reales.
Los tests de integración contra NIM requieren `NIM_API_KEY` en el entorno y
están marcados `Category=Integration`.

---

## Alcance

**Entra:**

- POST con streaming SSE, tool calls y reintentos.
- Lectura de línea SSE con cancelación real.
- Parser de fragmentos que ensambla tool calls.
- Construcción del cuerpo JSON de la petición.
- Selección de modelo y resolución de credenciales.

**No entra:**

- El loop de herramientas (spec 004).
- La persistencia del historial (spec 006).

---

## Requisitos

### R001 — Endpoint y autenticación

- Endpoint: `https://integrate.api.nvidia.com/v1/chat/completions`.
- La API key viaja en el header `Authorization: Bearer <key>`, nunca en el
  query string ni en el cuerpo.
- `Accept: text/event-stream`.

### R002 — Sin API key, no hay request

Si no hay key, se emite `OnError` con `BlsNimAuthException` **sin** construir
ni enviar la petición. Nunca se registra la key.

### R003 — Modelo

Precedencia: parámetro explícito > variable `NIM_MODEL` > default
`nvidia/nemotron-3-ultra-550b-a55b`.

### R004 — Reintentos

- Solo 429 y 5xx son reintentables.
- Backoff fijo: 1 s → 2 s → 4 s, tres intentos en total.
- `Retry-After` del servidor se respeta, pero se **corta a 30 s**. Un
  `Retry-After: 600` espera 30 s, no 600.
- 4xx (excepto 429) es terminal: no se reintenta y el body se preserva en el
  mensaje del error.
- Al agotar los intentos se emite el error tipado correspondiente
  (`BlsNimRateLimitException` para 429, `BlsNimServerException` para 5xx).
  El error del último intento **sí** se reporta; no se traga el fallo.

### R005 — Cancelación (ADR-003)

- Un token por turno. Cancelar antes del POST no hace request y emite
  `OnCancelled`.
- Cancelar durante el backoff aborta el reintento: `Task.Delay` recibe el token.
- La cancelación nunca se reporta como `OnError`.

### R006 — Timeout de chunk

120 s sin recibir datos ⇒ `BlsNimTimeoutException`, reportado como **error**
(no cancelación). Implementado con un `CancellationTokenSource` enlazado por
lectura, vía `SseLineReader`.

### R007 — Ensamblado de tool calls

Las tool calls llegan fragmentadas en varios chunks SSE. El parser:

- Acumula los fragmentos por `index`.
- Emite las tool calls **ordenadas por `index`**, no por orden de llegada.
- `Complete()` es idempotente: llamarlo dos veces no emite duplicados.
- Una tool call sin `id` o sin `name` no se emite.
- Argumentos que no son JSON válido ⇒ `OnError` con
  `BlsNimProtocolException` y esa tool call no se emite; el resto del stream
  continúa.

### R008 — Cuerpo de la petición

- `stream` siempre `true`.
- `temperature` y `max_tokens` fijos.
- Las tools se envuelven como `{ type: "function", function: {...} }`.
- Sin tools, el cuerpo no incluye `tools` ni `tool_choice`.
- `tool_call_id` solo se serializa en mensajes `role="tool"`.

---

## Criterios de aceptación

```
Dado un stream con varios chunks de texto
Cuando se consume
Entonces OnTextChunk se emite en orden y OnDone al final

Dado un tool call fragmentada en tres chunks
Cuando se consume el stream
Entonces se emite una sola ToolCallComplete con los argumentos ensamblados

Dado dos tool calls con index 1 y 0 que llegan en ese orden
Cuando se consume el stream
Entonces se emiten ordenadas por index: 0 antes que 1

Dado un 429 seguido de un 200
Cuando se envía la petición
Entonces se reintenta una vez y el segundo intento devuelve éxito

Dado un Retry-After de 600 segundos
Cuando se reintenta
Entonces espera como máximo 30 segundos

Dado un 400
Cuando se envía la petición
Entonces no se reintenta
Y el body del servidor aparece en el mensaje de error

Dado un turno cancelado durante el backoff
Cuando expira el delay
Entonces no se hace el siguiente intento y se emite OnCancelled

Dado un stream que no emite datos durante más de 120 segundos
Cuando expira el timeout
Entonces se emite BlsNimTimeoutException como error, no como cancelación

Dado un turno sin API key configurada
Cuando se envía
Entonces no se hace ninguna petición HTTP
Y ningún log contiene la key
```

---

## Tests obligatorios

Ya implementados y verdes:

| Test | Cubre |
|---|---|
| `SendAsync_AuthorizationVaComoHeaderDeRequest` | R001 |
| `SendAsync_SinApiKey_EmiteAuthErrorYNoHaceRequest` | R002 |
| `SendAsync_NoRegistraLaApiKeyEnLogs` | R002, P7 |
| `BuildJson_ModeloParametro_TienePrioridadSobreEnv` | R003 |
| `BuildJson_ModeloEnv_SeUsaCuandoNoHayParametro` | R003 |
| `SendAsync_429_ReintentaYEventuallyFunciona` | R004 |
| `SendAsync_RetryRespetaRetryAfter` | R004 |
| `SendAsync_RetryAfterSeAcotaA30Segundos` | R004 |
| `SendAsync_RetryUsaBackoffEntreIntentos` | R004 |
| `SendAsync_429_Agotado_EmiteRateLimitException` | R004 |
| `SendAsync_500_ReintentaYEventuallyFunciona` | R004 |
| `SendAsync_500_Agotado_EmiteServerException` | R004 |
| `SendAsync_400_NoReintentaYPreservaBody` | R004 |
| `SendAsync_401_EmiteAuthErrorYNoReintenta` | R004 |
| `SendAsync_CanceladoAntesDelPost_NoHaceRequestYEmiteCancelled` | R005 |
| `SendAsync_CanceladoDuranteBackoff_NoReintenta` | R005 |
| `SendAsync_SinDatos_LanzaTimeoutReportadoComoError` | R006 |
| `SendAsync_ToolCallsFragmentados_EmiteEnOrdenDeIndex` | R007 |
| `Feed_MultiplesToolCalls_EmiteEnOrdenDeIndex` | R007 |
| `Complete_ToolCallConArgumentosInvalidos_EmiteOnErrorYNoToolCall` | R007 |
| `Complete_LlamadoDosVeces_NoEmiteDuplicados` | R007 |
| `Feed_ToolCallSinIdNiName_NoEmite` | R007 |
| `Feed_JsonInvalido_LanzaBlsNimProtocolException` | R007 |
| `BuildJson_ConTools_EnvuelveEnTypeFunction` | R008 |
| `BuildJson_SinTools_NoIncluyeToolsNiToolChoice` | R008 |
| `BuildJson_MensajeTool_IncluyeToolCallId` | R008 |

---

## Desviaciones y riesgos

- **Nunca se ha llamado a la API real.** Todos los tests usan
  `FakeHttpHandler`. El contrato SSE asumido está verificado contra fixtures
  escritos a mano, no contra respuestas reales de Nvidia. Los tests
  `Category=Integration` existen pero no se ejecutan en CI.
- **Backoff inyectable por constructor interno.** Necesario para que los tests
  no tarden 7 s, pero significa que la producción usa el backoff real sin que
  ningún test lo valide.
- **`BlsConstants` no existe**, pero `.env.example` referencia
  `BlsConstants.NimReasoningEffort` y `BlsConstants.NimDefaultLanguage`. El
  cliente no lee esas dos variables todavía.
- **El timeout de chunk no se valida en tiempo real** (120 s). El test
  inyecta un timeout corto, así que la lógica está cubierta pero la
  configuración de producción no.
- **La CI espera `Turno_CanceladoDuranteStream_DetieneYEmitCancelled` y
  `Turno_TimeoutChunk_LanzaTimeoutNoCancelled`.** Los nombres reales del
  cliente son `SendAsync_CanceladoAntesDelPost_NoHaceRequestYEmiteCancelled`,
  `SendAsync_CanceladoDuranteBackoff_NoReintenta` y
  `SendAsync_SinDatos_LanzaTimeoutReportadoComoError`. La lista de la CI está
  desalineada con el código; hay que corregir una de las dos partes.