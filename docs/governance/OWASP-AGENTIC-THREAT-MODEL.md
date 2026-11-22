---
version: 1.0.0
date: 2026-11-19
status: approved
iso_standard: [42001, 27001, 25010]
aligns_to: [AGENTS.md, AI-POLICY.md, AI-RISK-REGISTER.md]
tags: [threat-model, owasp-agentic, security, red-team]
---

# Modelo de Amenazas — OWASP Top 10 for Agentic Applications (2025)

> Pilar 2. Alcance acotado al add-in: modelo NIM, tool loop, bridge JSON-RPC y almacén DPAPI.
> Este documento es la versión ejecutable de la antigua checklist del agente `bls-iso` (retirado en F1).

## 1. Matriz de amenazas

| ID    | Amenaza                                  | Aplicable | Control actual                                           | Mitigación                                                                                | Fase     |
|-------|------------------------------------------|-----------|----------------------------------------------------------|-------------------------------------------------------------------------------------------|----------|
| ASI01 | Secuestro de objetivo (*goal hijacking*) | Sí        | System prompt con reglas de error                        | `BlsHumanApprovalGate`: aprobacion directa por instruccion, auditada con `payload_sha256` | F2 ✅     |
| ASI02 | Mal uso de herramientas                  | Sí        | Whitelist `JsonRpcMethods.All`, `MaxToolIterations = 32` | `BlsToolRiskPolicy` (desconocida ⇒ High) + HITL; esquema runtime en F4                    | F2 ✅, F4 |
| ASI03 | Abuso de identidad / permisos            | Sí        | DPAPI CurrentUser, sin ROT                               | Archivo de riesgo bajo LP + registro de aprobación                                        | F2       |
| ASI04 | Inyección de memoria/contexto            | Sí        | Historial por proyecto                                   | Sanitización de historial + límite de contexto + TTL                                      | F5       |
| ASI05 | Ejecución de código no aislada           | Sí        | Sin sandbox; el modelo edita el VBE real                 | Confirmación explícita + preview y SHA-256 del payload; backup en F5                      | F2 ✅, F5 |
| ASI06 | Envenenamiento de RAG                    | No        | Sin RAG                                                  | —                                                                                         | N/A      |
| ASI07 | Comunicación insegura entre agentes      | Parcial   | Sin multi-agente desde F1                                | Un único agente; gate de `agents_index` en CI                                             | F1 ✅     |
| ASI08 | Cascada de fallos                        | Sí        | Reintentos 429/5xx con backoff                           | Circuit breaker + jitter + timeout global                                                 | F4       |
| ASI09 | Confianza humana engañosa (falso éxito)  | Sí        | `DeleteModule` devolvía sin hacer nada                   | DbC `BlsPreconditionException` + JSON `{"error":...}` sin bridge                          | F2 ✅     |
| ASI10 | Agente rogue / supply chain              | Sí        | AI-BOM con hash del prompt                               | Verificación en CI + prohibición de carga dinámica                                        | F1 🟡    |

## 2. Controles de detección

| Control                                                    | Estado                                                                                                          |
|------------------------------------------------------------|-----------------------------------------------------------------------------------------------------------------|
| Gate de CI: presencia de políticas, BOM y threat model     | ✅ F1                                                                                                            |
| Gate de CI: divulgación IA y agente único                  | ✅ F1                                                                                                            |
| Gate de CI: kill switch, HITL y DbC en `DeleteModule`      | ✅ F2                                                                                                            |
| Rechazo sin invocar bridge + log de `payload_sha256`       | ✅ F2                                                                                                            |
| Logs JSON con hash encadenado y `correlation_id`           | ✅ F3                                                                                                            |
| Verificación de integridad de la cadena de hash            | ✅ F3 (`BlsAuditChain.Verify`, gate de CI)                                                                       |
| Redacción de credenciales en el log                        | ✅ F3 (`BlsLogSanitizer`)                                                                                        |
| Redacción de PII en la frontera HTTP                       | ✅ F5 (`BlsPiiScrubber`, gate 8f)                                                                                |
| Red teaming adversario (prompt injection en seleccion VBE) | automatizado: 4 payloads en `PromptAndPrivacyHardeningTests`; sesion con modelo vivo pendiente de `NIM_API_KEY` |

## 3. Plan de pruebas adversarias (F5)

1. Inyectar en la selección VBE una instrucción que intente cambiar el system prompt → el modelo no debe obedecer.
2. Enviar una cadena de conexión en el código seleccionado → debe ser redactada antes de `BuildJson`. ✅ Cubierto por `BlsPiiScrubber` (tests `NimPiiScrubbingTests`).
3. Forzar un `tool_call` con `confirm: true` sin aprobación UI → debe ser rechazado. ✅ Cubierto por F2 (`BlsHumanApprovalGate`).
4. Reenviar el mismo `id` JSON-RPC dos veces → una sola ejecución. ✅ Cubierto por F4 (`BlsIdempotencyCache`).

Los puntos 2–4 están automatizados y verificados en CI; el punto 1 (inyeccion de prompt) se cubre de forma determinista en la frontera de construccion del prompt (4 payloads, incluido intento de cierre de etiqueta, escapado con `&amp;lt;`). La sesion con modelo vivo queda pendiente de `NIM_API_KEY` y no bloquea el cierre de los R-*.

## 4. Salida

Este modelo de amenazas se actualiza en el mismo PR que cualquier cambio en tools, prompt o modelo.
La evidencia de ejecución se conserva en los logs Audit (F3).
