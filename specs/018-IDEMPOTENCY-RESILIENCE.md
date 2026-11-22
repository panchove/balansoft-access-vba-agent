---
version: 1.0.0
date: 2026-11-19
status: implementado
iso_standard: [29148, 25010, 42001, 42010, 12207]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-017-IDEMPOTENCY-RESILIENCE.md]
tags: [spec, idempotency, retry, jitter, circuit-breaker, reliability]
---

# Spec 018 — Idempotencia y resiliencia del cliente

> **Norma de referencia:** ISO/IEC/IEEE 29148:2018 (requisitos), ISO/IEC 25010:2023
> (fiabilidad y eficiencia), ISO/IEC 42001:2023 (AIMS) e ISO/IEC/IEEE 42010:2022
> (arquitectura).

## Cabecera

| Campo | Valor |
|-------|-------|
| Estado | `implementado` |
| PRD | FR-03, FR-04, FR-09 + gobernanza (Pilares 3, 19, 22) |
| ADRs | ADR-002 (framing), ADR-003 (cancelación), ADR-015 (HITL), ADR-017 |
| Riesgos | Cierra R-06 (idempotencia) y R-09 (jitter/breaker) |
| Tests | 18 nuevos (9 de idempotencia + 9 de resiliencia); 295/295 verdes en x86 y x64 tras F5 |

## Contexto

Dos riesgos altos/medios siguen abiertos tras F3:

- **R-06**: `JsonRpcRequest.Id` solo correlaciona la respuesta. Si un frame se
  reenvía (reconexión del pipe, tool call duplicada del modelo), el dispatcher
  ejecuta otra vez la escritura: dos `write_module`, dos `execute_sql` o dos
  migraciones. El bridge no tiene forma de distinguir un reenvío de una
  operación nueva.
- **R-09**: `BlsNimClient` usa un backoff fijo de 1/2/4 s. Varios turnos o
  varias instancias que fallen al mismo tiempo se sincronizan y vuelven a
  golpear al proveedor en la misma ventana. No hay circuit breaker: un
  proveedor caído sigue recibiendo un POST por turno indefinitely.

Ambos son problemas de fiabilidad (ISO/IEC 25010) y de fail-safe (Pilar 3),
no de funcionalidad. La solución debe ser compatible hacia atrás: los frames
antiguos sin idempotency key y los constructores actuales de `BlsNimClient`
deben seguir funcionando.

## Alcance

- **Incluye**: campo opcional `idempotency_key` en `JsonRpcRequest`; clave
  determinista generada en `BlsBridgeClient`; caché de respuestas exitosas de
  escrituras en `JsonRpcDispatcher` con TTL y evicción; exponential backoff con
  *full jitter*; circuit breaker con estados Closed/Open/HalfOpen; excepción
  tipada `BlsNimCircuitOpenException`; tests y gate de CI.
- **Excluye**: persistencia de la caché entre reinicios (es en memoria por
  proceso); idempotencia de extremo a extremo en el proveedor NIM (solo aplica
  al bridge local); PII scrubber (implementado en F5 / spec 019); rotación de logs y `BlsVersion`
  (deuda de spec 008).

## Requisitos

### Idempotencia (R-06)

- **R001** — `JsonRpcRequest` acepta `idempotency_key` como campo opcional. Su
  ausencia es válida y se comporta como hoy (compatibilidad hacia atrás).
- **R002** — `BlsBridgeClient` deriva la clave de
  `SHA256(correlation_id | method | params_json)`, con `correlation_id` fijo
  por turno (ADR-016). Un reenvío del mismo frame dentro del turno produce la
  misma clave; un turno distinto produce otra.
- **R003** — La caché solo retiene respuestas **exitosas** de tools con riesgo
  `Medium` o `High` según `BlsToolRiskPolicy`. Lecturas (`Low`), errores y
  respuestas canceladas nunca se cachean.
- **R004** — Ante una clave ya cacheada y no expirada, el dispatcher devuelve
  la respuesta original con el `id` del request actual y
  `idempotent_replay = true`, **sin invocar el handler**.
- **R005** — La caché tiene TTL configurable (5 min por defecto) y un número
  máximo de entradas (256 por defecto). Al llenarse, evicta las más antiguas.
- **R006** — El acierto de caché se registra en `IBlsLogger` con los primeros
  8 caracteres de la clave y el `correlation_id`; nunca la clave completa ni
  los parámetros (Pilar 24).
- **R007** — El constructor público de `JsonRpcDispatcher` conserva su firma
  de 4 argumentos; la caché se inyecta por un constructor sobrecargado.

### Resiliencia (R-09)

- **R008** — El constructor público de `BlsNimClient` usa exponential backoff
  con *full jitter*: `delay = random(0, min(30 s, 1 s · 2^attempt))`.
- **R009** — `Retry-After` del servidor se respeta tal cual (tope 30 s) y
  **prevalece sobre el jitter**: es una instrucción del proveedor, no una
  sugerencia estadística.
- **R010** — Los 4xx distintos de 429 siguen siendo terminales y no cuentan
  como fallo del breaker. Solo cuentan 429/5xx agotados, timeouts de chunk y
  fallos de conexión.
- **R011** — Circuit breaker inyectable con umbral 5 fallos, ventana abierta
  30 s y un solo sondeo en HalfOpen. En Open, `SendAsync` no hace ningún
  request y emite `BlsNimCircuitOpenException` por `OnError`.
- **R012** — Un éxito en HalfOpen cierra el breaker y resetea el contador; un
  fallo lo devuelve a Open y reinicia la ventana.
- **R013** — La cancelación de usuario (ADR-003) se comprueba **antes** del
  breaker y de cada espera. Con el breaker abierto y el token cancelado gana
  la cancelación: `OnCancelled()`, nunca `OnError()`.
- **R014** — Los constructores actuales de `BlsNimClient` (público de 5 args e
  interno de 7 con `TimeSpan[]`) conservan su firma. El interno usa una
  política fija y un breaker no-op para no alterar los 28 tests existentes.

## Criterios de aceptación

1. **Dado** un `write_module` ejecutado con clave K, **cuando** llega un frame
   idéntico con K e `id` distinto, **entonces** el handler se invoca una sola
   vez y la segunda respuesta replica el resultado con el nuevo `id`.
2. **Dada** una respuesta con error, **cuando** se reenvía la misma clave,
   **entonces** el handler se ejecuta de nuevo (los fallos no se cachean).
3. **Dado** un frame sin `idempotency_key`, **cuando** se envía dos veces,
   **entonces** el handler se ejecuta dos veces.
4. **Dado** un turno con `correlation_id`, **cuando** se reenvía la misma tool
   call, **entonces** ambas peticiones llevan la misma clave; con otro turno,
   distinta.
5. **Dados** tres intentos con jitter 0, 0,5 y 1, **cuando** se calcula el
   delay, **entonces** son 0, 500 ms y 1000 ms para `base = 1 s` y
   `attempt = 0`.
6. **Dado** un `Retry-After: 0` del servidor, **cuando** se reintenta,
   **entonces** la política de jitter no se invoca.
7. **Dado** un breaker con 5 fallos, **cuando** se envía una petición,
   **entonces** `RequestCount == 0` y el sink recibe
   `BlsNimCircuitOpenException`.
8. **Dado** un breaker abierto, **cuando** avanza el reloj 31 s y llega un
   éxito, **entonces** pasa a Closed y el contador se resetea.
9. **Dado** un breaker abierto, **cuando** el token de turno está cancelado,
   **entonces** el sink recibe `OnCancelled()` y no `OnError()`.

## Tests obligatorios

| Fichero | Tests |
|---------|-------|
| `JsonRpcIdempotencyTests` | 9 |
| `BlsNimResilienceTests` | 9 |

Tests de ADR bloqueantes (añadidos al gate de CI):
`Idempotency_FrameDuplicado_NoEjecutaHandlerDosVeces`,
`Retry_JitterDistribuyeRetrasos` y
`Breaker_Abierto_NoHaceRequestYEmiteCircuitOpen`.

Gate de CI: *Idempotencia y resiliencia (F4)*.

## Desviaciones y riesgos

- La deduplicación es **por turno**, no global: la clave incluye el
  `correlation_id`. Dos tool calls idénticas en turnos distintos son dos
  operaciones legítimas y se ejecutan ambas.
- La caché es en memoria. Reiniciar el add-in la pierde; no se persiste porque
  un frame reenviado tras un reinicio no puede distinguirse de una tool call
  nueva del modelo.
- Solo se cachean escrituras (`Medium`/`High`). Una lectura repetida vuelve a
  ejecutarse: se prefiere frescura de datos frente a una caché que podría
  devolver el estado de VBE de hace minutos.
- El *full jitter* puede producir un delay de 0 ms. Es intencionado: rompe la
  sincronización entre clientes y el número de intentos sigue acotado a 3.
- En HalfOpen solo se permite **un** sondeo: si falla, el breaker se abre de
  inmediato (R012) en lugar de consumir los 3 reintentos. Evita que un solo
  sondeo se convierta en tres peticiones contra un proveedor caído.
- El error emitido por un sondeo fallido es el error original (429/5xx), no
  `BlsNimCircuitOpenException`: ese último se reserva para la siguiente
  petición, que ya encuentra el circuito abierto.
- El breaker es por instancia de `BlsNimClient`. La terminal crea una sola
  instancia, así que en la práctica es global al add-in.
- R-03 (PII al proveedor) fue cerrado en F5 por `specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md`.
