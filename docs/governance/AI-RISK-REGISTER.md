---
version: 1.0.0
date: 2026-11-19
status: approved
iso_standard: [42001, 23894, 27001, 25010]
aligns_to: [AGENTS.md, AI-POLICY.md, OWASP-AGENTIC-THREAT-MODEL.md]
tags: [governance, risk-register, iso-42001, iso-23894]
---

# Registro de Riesgos de IA — Balansoft Access VBA Agent

> Marco: ISO/IEC 42001:2023 §6.1 · ISO/IEC 23894:2023 · 25 Pilares (Pilar 1).
> Escala: Probabilidad e Impacto en {B, M, A}. Nivel = P × I → Bajo / Medio / Alto / Crítico.
> Owner: propietario del producto. Revisión: cada release y tras incidente.

## 1. Riesgos

| ID   | Riesgo                                                                          | Pilar  | P | I | Nivel       | Control actual                                              | Control planificado                                                          | Estado              |
|------|---------------------------------------------------------------------------------|--------|---|---|-------------|-------------------------------------------------------------|------------------------------------------------------------------------------|---------------------|
| R-01 | El LLM emite un tool call destructivo sin aprobación humana                     | 4, 2   | M | A | **Crítico** | `confirm` era un bool entregado por el propio modelo        | `BlsHumanApprovalGate` + MessageBox ("No" por defecto) + `payload_sha256`    | 🟢 Cerrado en F2    |
| R-02 | Falsa confirmación de éxito ante un fallo (p. ej. `DeleteModule` hace `return`) | 3, 16  | M | A | **Crítico** | Ninguno; el system prompt lo prohíbe pero el código lo hace | DbC: `BlsPreconditionException` + JSON `{"error":...}` sin bridge            | 🟢 Cerrado en F2    |
| R-03 | Fuga de PII/connection strings al endpoint externo                              | 24     | M | A | **Crítico** | Truncamiento a 32 KB (no es saneamiento)                    | PII scrubber + consentimiento (F5)                                           | 🔴 Abierto          |
| R-04 | Logs no auditables (texto plano, sin hash encadenado)                           | 6, 20  | A | M | **Alto**    | Texto plano sin encadenar                                   | `BlsLogger` JSONL + `BlsAuditChain.Verify` + saneamiento de secretos (F3)    | 🟢 Cerrado en F3    |
| R-05 | Sin trazabilidad punta a punta de un turno                                      | 20     | A | M | **Alto**    | `JsonRpcErrorData` sin `trace_id`                           | `BlsCorrelationContext` + ids en `JsonRpcRequest` y logs del dispatcher (F3) | 🟢 Cerrado en F3    |
| R-06 | Reenvío duplicado de un frame ejecuta dos veces una escritura                   | 19     | M | A | **Alto**    | `BlsIdempotencyKey` SHA-256 por turno + caché TTL 5 min / 256 entradas | `specs/018` + gate 8e + `JsonRpcIdempotencyTests` (9)                        | 🟢 Cerrado en F4    |
| R-07 | Instalación de modelo/prompt no verificado (supply chain)                       | 10     | B | A | **Alto**    | Modelo duplicado en 5 artefactos                            | AI-BOM con hash del prompt                                                   | 🟡 Mitigado parcial |
| R-08 | Atribución incorrecta de autoría de IA                                          | 5      | A | B | **Medio**   | Trailer global apunta a Claude Code                         | Trailer `AI-Assistant` en AGENTS.md (F1)                                     | 🟢 Cerrado en F1    |
| R-09 | Backoff sin jitter ni circuit breaker satura el proveedor                       | 22     | M | M | **Medio**   | Full jitter 1–30 s + breaker 5 fallos / 30 s / HalfOpen      | `specs/018` + gate 8e + `BlsNimResilienceTests` (9)                           | 🟢 Cerrado en F4    |
| R-10 | Deriva documentación ↔ código                                                   | 17     | A | M | **Alto**    | Historial de drift ya documentado                           | Gate de cross-refs + reindex MCP                                             | 🟡 Mitigado parcial |
| R-11 | Entropía fija DPAPI debilita la protección de la API key                        | 21, 18 | B | A | **Alto**    | DPAPI CurrentUser con entropía fija                         | Derivar entropía de datos no secretos del equipo                             | 🔴 Abierto          |
| R-12 | Bucle infinito de tool calls                                                    | 3      | B | A | **Alto**    | `MaxToolIterations = 8`                                     | `BlsSafetyGate`: kill switch cancela el turno y bloquea nuevos envíos        | 🟢 Cerrado en F2    |

## 2. Criterios de aceptación del riesgo

- Todo riesgo **Crítico** debe estar cerrado antes de habilitar la tool loop en producción.
- Todo riesgo **Alto** debe tener control planificado y fecha en el plan de trabajo.
- La aceptación de riesgos requiere firma humana; el agente no puede aceptar riesgos por su cuenta.

## 3. Trazabilidad

Cada riesgo se vincula a un pilar de `AI_GOVERNANCE.md` y a una mitigación con evidencia
en CI o en código. La tabla de amenazas técnica está en `docs/governance/OWASP-AGENTIC-THREAT-MODEL.md`.

## 4. Cierre de F4 (R-06 y R-09)

`specs/018-IDEMPOTENCY-RESILIENCE.md` y `ADR-017-IDEMPOTENCY-RESILIENCE.md`
documentan los controles que cerraron ambos riesgos:

- **R-06** — `BlsBridgeClient` deriva `SHA256(correlation_id|method|params)` y
  `JsonRpcDispatcher` solo cachea respuestas exitosas de tools `Medium`/`High`
  (TTL 5 min, 256 entradas, evicción FIFO). El replay lleva
  `idempotent_replay = true` y el `id` del frame actual. No se cachean lecturas,
  errores ni cancelaciones; la clave se registra truncada a 8 caracteres.
- **R-09** — `BlsNimClient` usa full jitter (`random(0, min(30 s, 1 s·2^n))`)
  con `Retry-After` prevaleciendo sobre el jitter, y un breaker de 5 fallos /
  30 s con un único sondeo en HalfOpen. La cancelación de usuario (ADR-003) se
  comprueba antes del breaker.

Evidencia: 18 tests nuevos, 257/257 verdes en x86 y x64, gate 8e de CI.
Siguen abiertos R-03 (PII, F5), R-07/R-10 (parciales), R-11 (entropía DPAPI).
