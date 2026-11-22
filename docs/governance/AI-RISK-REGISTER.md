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

| ID   | Riesgo                                                                          | Pilar  | P | I | Nivel       | Control actual                                                              | Control planificado                                                          | Estado           |
|------|---------------------------------------------------------------------------------|--------|---|---|-------------|-----------------------------------------------------------------------------|------------------------------------------------------------------------------|------------------|
| R-01 | El LLM emite un tool call destructivo sin aprobación humana                     | 4, 2   | M | A | **Crítico** | `confirm` era un bool entregado por el propio modelo                        | `BlsHumanApprovalGate` + MessageBox ("No" por defecto) + `payload_sha256`    | 🟢 Cerrado en F2 |
| R-02 | Falsa confirmación de éxito ante un fallo (p. ej. `DeleteModule` hace `return`) | 3, 16  | M | A | **Crítico** | Ninguno; el system prompt lo prohíbe pero el código lo hace                 | DbC: `BlsPreconditionException` + JSON `{"error":...}` sin bridge            | 🟢 Cerrado en F2 |
| R-03 | Fuga de PII/connection strings al endpoint externo                              | 24     | M | A | **Crítico** | `BlsPiiScrubber` en la frontera HTTP + `BlsPiiConsentGate` (No por defecto) | `specs/019` + gate 8f + `BlsPiiScrubberTests`/`BlsPiiConsentGateTests`       | 🟢 Cerrado en F5 |
| R-04 | Logs no auditables (texto plano, sin hash encadenado)                           | 6, 20  | A | M | **Alto**    | Texto plano sin encadenar                                                   | `BlsLogger` JSONL + `BlsAuditChain.Verify` + saneamiento de secretos (F3)    | 🟢 Cerrado en F3 |
| R-05 | Sin trazabilidad punta a punta de un turno                                      | 20     | A | M | **Alto**    | `JsonRpcErrorData` sin `trace_id`                                           | `BlsCorrelationContext` + ids en `JsonRpcRequest` y logs del dispatcher (F3) | 🟢 Cerrado en F3 |
| R-06 | Reenvío duplicado de un frame ejecuta dos veces una escritura                   | 19     | M | A | **Alto**    | `BlsIdempotencyKey` SHA-256 por turno + caché TTL 5 min / 256 entradas      | `specs/018` + gate 8e + `JsonRpcIdempotencyTests` (9)                        | 🟢 Cerrado en F4 |
| R-07 | Instalación de modelo/prompt no verificado (supply chain)                       | 10     | B | A | **Alto**    | `BlsModelCatalog` como fuente única; prompt compilado                       | AI-BOM con hash verificado + gate 8f (literal único y sin carga dinámica)    | 🟢 Cerrado en F5 |
| R-08 | Atribución incorrecta de autoría de IA                                          | 5      | A | B | **Medio**   | Trailer global apunta a Claude Code                                         | Trailer `AI-Assistant` en AGENTS.md (F1)                                     | 🟢 Cerrado en F1 |
| R-09 | Backoff sin jitter ni circuit breaker satura el proveedor                       | 22     | M | M | **Medio**   | Full jitter 1–30 s + breaker 5 fallos / 30 s / HalfOpen                     | `specs/018` + gate 8e + `BlsNimResilienceTests` (9)                          | 🟢 Cerrado en F4 |
| R-10 | Deriva documentación ↔ código                                                   | 17     | A | M | **Alto**    | Gate de cross-refs + índice de specs + reindex MCP                          | Gate 13 ampliado a rutas de código en MDs; verificación en cada fase         | 🟢 Cerrado en F5 |
| R-11 | Entropía fija DPAPI debilita la protección de la API key                        | 21, 18 | B | A | **Alto**    | `DerivedEntropyDpapiDataProtector` (SHA-256 equipo+usuario+v2)              | Migración transparente con fallback legacy solo de lectura (F5)              | 🟢 Cerrado en F5 |
| R-12 | Bucle infinito de tool calls                                                    | 3      | B | A | **Alto**    | `MaxToolIterations = 32`                                                    | `BlsSafetyGate`: kill switch cancela el turno y bloquea nuevos envíos        | 🟢 Cerrado en F2 |

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

Evidencia: 18 tests nuevos, 327/327 verdes en x86 y x64, gate 8e de CI.

## 5. Cierre de F5 (R-03, R-07, R-10 y R-11)

`specs/019-PRIVACY-SUPPLY-CHAIN-CONFIG.md` y
`docs/design/ADR-018-PRIVACY-SUPPLY-CHAIN.md` documentan los controles:

- **R-03** — `BlsPiiScrubber` redacta una copia de los mensajes en
  `NimHttpRequestBuilder.BuildJson`, siempre, sin opción de desactivarlo; el
  `BlsPiiConsentGate` exige aprobación UI con "No" por defecto cuando hay
  hallazgos. Un rechazo llama a `OnCancelled()` y aborta antes de
  `RunToolLoopAsync`: cero peticiones HTTP y nunca éxito falso.
- **R-07** — `BlsModelCatalog.DefaultModel` es el único literal de modelo en
  `src/`; el AI-BOM registra los SHA-256 del prompt y del catálogo, y el gate 8f
  los recalcula y prohíbe carga dinámica de prompts/modelos.
- **R-10** — el gate 13 verifica rutas de código citadas en `specs/` y `docs/`,
  comprueba que todo `NNN-*.md` esté en `specs/README.md` y se cierra con
  reindex MCP y verificación de cobertura.
- **R-11** — la entropía v2 se deriva con SHA-256 de datos no secretos del
  equipo; `BlsSecureStore` lee con el protector legacy solo cuando falla v2 y
  reencripta antes de devolver la key. El usuario no vuelve a pegarla; si ambos
  fallan se propaga `BlsSecureStoreException`.

Evidencia: 32 tests nuevos, 327/327 verdes en x86 y x64, 13 gates de CI.
**Todos los riesgos R-01 a R-12 están cerrados.**

## 6. Auditoria de gobernanza 2026-11-22 (Fase 0)

Auditoria de solo lectura seguida de correcciones aprobadas por el propietario. No cerro ningun riesgo nuevo: los R-01..R-12 permanecen cerrados por sus controles originales.

| Hallazgo                                             | Correccion                                                      | Evidencia                                      |
|------------------------------------------------------|-----------------------------------------------------------------|------------------------------------------------|
| Gate 11 en rojo por un comentario con `Thread.Sleep` | Comentario reescrito                                            | Gate reproducido: 0 coincidencias              |
| Cinco tests de ADR vacios contabilizados como verdes | Tres implementados, dos retirados, gate 12 rechaza cuerpo vacio | `ProjectKeyAndObjectDetailTests.cs` + `ci.yml` |
| Test de SLO dependiente del reloj real               | Asercion de latencia eliminada                                  | 327/327 en ejecuciones consecutivas            |
| Step Release compilaba el proyecto de tests          | Compila el add-in                                               | `.github/workflows/ci.yml`                     |

Riesgos de proceso detectados y **aun abiertos** (no son R-*):

- **CS-008**: fail-safe y HITL no se comprueban en la frontera del pipe (`JsonRpcDispatcher`). Remediacion: Fase 1.
- **CS-009**: cadena SHA-256 no verificada en ejecucion y fallos de escritura silenciados. Remediacion: Fase 2.

La suite queda en **327/327** porque se retiraron dos tests ficticios y se convirtieron tres vacios en aserciones reales; no se elimino funcionalidad.
## 7. Cierre del hallazgo CS-008 (Fase 1, 2026-11-22)

El riesgo R-01 ("tool call destructiva sin aprobacion humana") estaba cerrado en la terminal, pero la superficie real de ataque es el named pipe. La Fase 1 cierra el hallazgo:

- HITL y kill switch se comprueban ahora en `JsonRpcDispatcher` ademas de en `BlsNimTerminal`.
- El token de aprobacion es de un solo uso y esta ligado al metodo, al turno y al hash del payload.
- El tope de 32 iteraciones se refuerza en el servidor.
- La ACL del pipe se restringe al SID del usuario actual.

Evidencia: 8 tests nuevos (`JsonRpcSafetyTests`, `BlsPipeSecurityTests`), 327/327 verdes en x86, gate 8c ampliado con tokens de regresion. CS-008 queda cerrado; CS-009 (auditoria) permanece abierto para la Fase 2.
## 8. Cierre del hallazgo CS-009 (Fase 2, 2026-11-22)

La cadena SHA-256 era verificable solo en tests. Ahora se verifica al arranque, abarca varios dias y un fallo dispara el kill switch. Los fallos de escritura dejan de silenciarse y ADR-005 incorpora postcondiciones e invariantes.

Evidencia: 10 tests nuevos (`BlsAuditChainDirectoryTests`, `BlsPostconditionTests`), 327/327 verdes en x86, gate 8d ampliado. CS-008 y CS-009 quedan cerrados; quedan abiertos los hallazgos de las fases 3 y 4 del plan.
## 9. Fase 3 - simplificacion documental (2026-11-22)

El hallazgo H7 de la auditoria (skills con orquestacion retirada) queda cerrado: de 11 skills se pasa a 4 canonicos, sin referencias a `bls-implementer` ni handoffs entre modelos. El gate 3 sigue exigiendo `agents_index = 1` y el gate 8 la presencia de los cuatro skills. No se cierra ningun riesgo R-* nuevo.
## 10. Fase 4 - resiliencia, privacidad y observabilidad (2026-11-22)

Cerrados los ultimos hallazgos de la auditoria: clave de idempotencia derivada en servidor (R-06 reforzado), timeout global de turno (BalanceSoft-003), delimitacion de contexto no confiable (R-03/ASI04 reforzado), NIF/IBAN en el scrubber, version NuGet fija y SBOM en CI (R-07 reforzado), y SLO estructurado en la cadena de auditoria (R-04/R-05 reforzados).

Evidencia: 5 tests nuevos, 327/327 verdes en x86 y x64, 6 builds sin warnings, SBOM con 11 dependencias. Los doce riesgos R-01..R-12 siguen cerrados.
## 11. Evaluación de impacto, arquitectura y respuesta a incidentes (2026-11-22)

Se cierra el ultimo hallazgo documental de la auditoria (H8): `AI-IMPACT-ASSESSMENT.md` cubre el Pilar 1, `ARCHITECTURE-C4.md` aporta los diagramas C4/Mermaid de los Pilares 9 y 25, y `AI-INCIDENT-PLAYBOOK.md` establece contencion, recuperacion y simulacro del Pilar 14. Los gates 3 y 8 verifican su presencia.
## 12. Verificacion en producto y cierre de bugs historicos (2026-11-22)

Access 2010 x86 + `tests/BalansoftTest.accdb`: add-in `Connect=True`, menu `Tools -> NIM Terminal`, bridge responde `list_modules` por el named pipe y la cadena SHA-256 del log real es 48/48 valida. Se cierran BalanceSoft-001 (`.accde`) y BalanceSoft-002 (streams largos). Red teaming determinista de ASI04 con 4 payloads; la sesion con modelo vivo requiere `NIM_API_KEY` y queda registrada como pendiente, sin bloquear los R-*.
## 13. Aprobacion directa por instruccion (2026-11-22)

R-01 queda mitigado por la instruccion explicita del usuario, auditada con `payload_sha256`, en lugar de un dialogo modal. No se elimina ningun control de fail-safe: kill switch, token de un solo uso, ACL del pipe, auditoria y tope de 32 iteraciones siguen activos y verificados en CI.