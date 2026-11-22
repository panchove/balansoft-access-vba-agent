---
version: 1.0.0
date: 2026-11-19
status: implementado
iso_standard: [29148, 25010, 42001, 27001]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-015-HUMAN-IN-THE-LOOP.md]
tags: [spec, hitl, fail-safe, kill-switch, safety]
---

# Spec 016 — Supervisión humana y fail-safe

## Cabecera

| Campo | Valor |
|-------|-------|
| Estado | `implementado` |
| PRD | Requisito no funcional de seguridad y supervisión (Pilares 3 y 4) |
| ADRs | ADR-003, ADR-005, ADR-015 |
| Tests | 22 verdes (18 `BlsSafetyGateTests`, 3 `BlsComHelperSafetyTests`, 1 de i18n) + gate de CI |

## Contexto

El modelo puede pedir tool calls destructivas. Antes de este spec, `confirm` era un booleano
entregado por el propio modelo y `DeleteModule` podía devolver éxito sin borrar nada.

## Alcance

- Incluye: kill switch, clasificación de riesgo, aprobación UI, rechazo sin bridge, DbC en
  `DeleteModule`, textos localizados y registro de decisiones con hash del payload.
- Excluye: logger JSON con cadena SHA-256 (F3), idempotencia de reenvíos (F4),
  redacción de PII (implementada en F5 / spec 019).

## Requisitos

- **R001** — Debe existir un kill switch que bloquee nuevos turnos y tool calls sin que el modelo pueda desactivarlo.
- **R002** — El kill switch debe cancelar el turno en curso y deshabilitar el envío hasta su reactivación humana.
- **R003** — Toda tool debe clasificarse en `Low`/`Medium`/`High`; una tool desconocida se clasifica `High`.
- **R004** — Las tools `Low` no requieren aprobación; `Medium` y `High` sí.
- **R005** — El diálogo de aprobación debe usar "No" como botón por defecto y mostrar riesgo, argumentos y SHA-256.
- **R006** — Un rechazo o fallo del diálogo debe devolver JSON de error y **no** invocar el bridge.
- **R007** — El registro de la decisión debe contener `payload_sha256` pero nunca el payload en claro.
- **R008** — `BlsComHelper.DeleteModule` debe lanzar `BlsPreconditionException` sin `confirm` o sin VBE.
- **R009** — Las claves i18n de seguridad deben existir en es/en/pt/fr.

## Criterios de aceptación

1. **Given** un gate disparado, **when** se intenta un turno o una tool, **then** se lanza `BlsSafetyTrippedException` y no se llama al bridge.
2. **Given** un `Reset` humano, **when** se envía de nuevo, **then** el turno se ejecuta con normalidad.
3. **Given** `delete_module` o `execute_sql`, **when** el usuario responde "No", **then** el resultado contiene `"code":"HITL_DENIED"` y el bridge no se invoca.
4. **Given** un payload con una cadena sensible, **when** se aprueba, **then** el log contiene el hash y no la cadena.
5. **Given** `DeleteModule(..., confirm:false)`, **when** se invoca, **then** se lanza `BlsPreconditionException` con `confirm:true` en el mensaje.

## Tests obligatorios

- `BlsSafetyGateTests` — 5 tests de parada/reanudación.
- `BlsToolRiskPolicyTests` — 5 tests de clasificación y fail-safe.
- `BlsApprovalRequestTests` — 3 tests de hash y truncamiento.
- `BlsHumanApprovalGateTests` — 5 tests de decisión y privacidad.
- `BlsComHelperSafetyTests` — 3 tests de DbC.
- `BlsStringsTests.SafetyStrings_PresentInAllLanguages`.
- Gate de CI: *Fail-safe y supervision humana (HITL)*.

## Desviaciones y riesgos

- La aprobación se realiza con `Invoke` síncrono al hilo UI; si el hilo UI no responde, el turno
  queda esperando. Mitigación pendiente: timeout de diálogo con denegación automática.
- La política de riesgo no verifica en tiempo de ejecución que sus nombres siguen siendo un
  subconjunto de `JsonRpcMethods.All` (restricción de dependencias Helpers ← Core).
- Los eventos HITL usan el logger de texto plano actual; la cadena de hash y `correlation_id`
  llegan en F3. Hasta entonces, la auditabilidad es parcial (R-04 sigue abierto).

## Requisitos anadidos en Fase 1 (2026-11-22)

- **R007**: toda tool `Medium`/`High` recibida por `JsonRpcDispatcher` debe presentar un token de autorizacion valido, no reutilizado y no caducado. Un rechazo responde `HITL_DENIED` (-32005) y no invoca el handler.
- **R008**: el dispatcher comprueba el kill switch antes de despachar cualquier metodo; si esta disparado responde `SAFETY_TRIPPED` (-32006).
- **R009**: el numero de tool calls por `correlation_id` se limita a 8 tambien en el servidor; el exceso responde `TOOL_ITERATION_LIMIT` (-32007).
- **R010**: el pipe del bridge solo concede acceso al SID del usuario que ejecuta el add-in.

Tests obligatorios: `HighRisk_SinToken_DevuelveHitlDeniedYNoEjecuta`, `HighRisk_ConTokenValido_EjecutaUnaSolaVez`, `TokenDeUnSoloUso_SegundoFrameQuedaDenegado`, `KillSwitchDisparado_BloqueaAntesDelHandler`, `LimiteIteracionesPorTurno_SeRefuerzaEnElDispatcher`, `BuildPipeSecurity_SoloConcedeAlSidDelUsuarioActual`.