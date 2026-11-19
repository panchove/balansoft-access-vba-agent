---
version: 1.0.0
date: 2026-11-19
status: implementado
iso_standard: [29148, 25010, 42001, 27001, 27701]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-016-AUDIT-LOGGING.md]
tags: [spec, audit, logging, sha256, correlation-id, slo]
---

# Spec 017 — Registro de auditoría y trazabilidad

## Cabecera

| Campo | Valor |
|-------|-------|
| Estado | `implementado` |
| PRD | FR-07, FR-12 + gobernanza (Pilares 6, 20, 21) |
| ADRs | ADR-011 (parcialmente superado), ADR-015, ADR-016 |
| Tests | 51 verdes (6 ficheros nuevos) + gate de CI |

## Contexto

F2 dejó los eventos de seguridad (kill switch, HITL, rechazo sin bridge) en
líneas de texto plano, sin encadenar ni correlacionar. No era posible demostrar
la integridad del registro ni reconstruir un turno completo a partir del log.

## Alcance

- Incluye: JSON Lines, cadena SHA-256 `prev_hash + hash`, verificación,
  `correlation_id`/`trace_id` por turno, propagación al dispatcher por JSON-RPC,
  saneamiento de secretos, métricas SLO y gate de CI.
- Excluye: rotación de 7 días y versión/commit en la primera línea (deuda de
  spec 008), PII scrubber del prompt (F5), idempotencia (F4), telemetría remota.

## Requisitos

- **R001** — `IBlsLogger` conserva sus cuatro firmas; ningún consumidor existente se modifica.
- **R002** — Cada evento se escribe como un objeto JSON con `schema_version`, `ts` (UTC ISO-8601), `level`, `component`, `message`, `correlation_id`, `trace_id`, `prev_hash` y `hash`.
- **R003** — `hash = SHA256(prev_hash + "|" + payload_json)` con `prev_hash = "GENESIS"` en el primer evento del día.
- **R004** — El fichero es append-only: nunca se trunca ni se reescribe; la cadena de un día es verificable.
- **R005** — `BlsAuditChain.Verify(path)` detecta modificación del mensaje, del `prev_hash` y del `hash`, informando la primera línea inválida.
- **R006** — Si el último hash del fichero no es válido, el logger no escribe (fail-safe).
- **R007** — Un `correlation_id` y un `trace_id` envuelven cada turno; fluyen por `AsyncLocal` y se envían en el request JSON-RPC al dispatcher.
- **R008** — `JsonRpcRequest` acepta los ids como campos opcionales; su ausencia sigue siendo válida (compatibilidad hacia atrás).
- **R009** — Antes de escribir, el mensaje y el mensaje de la excepción se sanean: API keys, Bearer y contraseñas se redactan; saltos de línea se escapan; longitud ≤ 4000.
- **R010** — El detalle de excepción incluye tipo, mensaje saneado y HResult en hex para COM; nunca el stack trace.
- **R011** — Un fallo de escritura no propaga, pero se contabiliza en las métricas SLO.
- **R012** — Las métricas exponen tasa de escritura, p95, tasa de turnos y rechazos; los objetivos son ≥ 99,9 %, ≤ 50 ms y ≥ 95 %.

## Criterios de aceptación

1. **Dado** un evento escrito, **cuando** se parsea su línea, **entonces** contiene todos los campos de R002 y `Verify` devuelve válido.
2. **Dados** cuatro eventos, **cuando** se verifica el fichero, **entonces** el `prev_hash` del segundo coincide con el `hash` del primero y `Count == 4`.
3. **Dado** un fichero auditado, **cuando** se cambia una palabra del mensaje, **entonces** `Verify` marca esa línea como inválida.
4. **Dado** un turno con ids explícitos, **cuando** se registra desde la terminal y desde el dispatcher, **entonces** ambos eventos llevan los mismos ids.
5. **Dado** un mensaje con `NIM_API_KEY`, `Bearer` y `password`, **cuando** se escribe, **entonces** ningún valor secreto aparece en el fichero.
6. **Dado** un directorio de log inaccesible, **cuando** se escribe, **entonces** no se lanza excepción y `FailedWrites == 1`.
7. **Dadas** 100 escrituras concurrentes, **cuando** se verifica, **entonces** las 100 líneas son íntegras.

## Tests obligatorios

| Fichero | Tests |
|---------|-------|
| `BlsAuditLoggerTests` | 11 |
| `BlsAuditChainTests` | 11 |
| `BlsLogSanitizerTests` | 10 |
| `BlsCorrelationContextTests` | 7 |
| `BlsSloMetricsTests` | 8 |
| `JsonRpcCorrelationTests` | 4 |

Gate de CI: *Auditoría JSON, hash encadenado y correlación (F3)*. Tests de ADR
obligatorios: `Logger_CadenaUnEvento_EsVerificable`, `Verify_TamperMensaje_DetectaCorrupcion`
y `Dispatcher_ConCorrelacion_LosEscribeEnElLog`.

## Desviaciones y riesgos

- Leer el último hash en cada escritura es O(n) por línea; suficiente para el
  volumen actual, a revisar si el log crece.
- La cadena se reinicia cada día; verificar varios días requiere ejecutar
  `Verify` por fichero.
- Sin rotación de 7 días ni versión/commit (spec 008): deuda heredada, no
  introducida por F3.
- El `trace_id` no se envía a NVIDIA NIM; la traza es local al add-in.
- R-03 (PII al proveedor) permanece abierto y se trata en F5.
