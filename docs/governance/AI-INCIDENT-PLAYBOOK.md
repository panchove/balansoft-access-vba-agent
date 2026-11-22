---
version: 1.0.0
date: 2026-11-22
status: approved
iso_standard: [42001, 27001, 23894]
aligns_to: [AI-RISK-REGISTER.md, AI-IMPACT-ASSESSMENT.md]
tags: [governance, incident-response, playbook]
---

# Playbook de Incidentes de IA — Balansoft Access VBA Agent

> Pilar 14 / ISO/IEC 42001 e NIST AI RMF. Objetivo: contener en segundos sin tumbar el host Access.

## 1. Clasificación

| Nivel | Ejemplo | Acción inicial |
|-------|---------|----------------|
| SEV-1 | Tool destructiva ejecutada sin aprobación, cadena de auditoría rota, fuga de API key | Kill switch + aislamiento inmediato |
| SEV-2 | Rechazo HITL ignorado, PII enviada sin consentimiento, breaker en Open persistente | Kill switch + revisión del log |
| SEV-3 | Respuesta incorrecta no destructiva, latencia fuera de SLO | Registro y seguimiento |

## 2. Contención (minutos 0-5)

1. **Activar el kill switch** del botón de la terminal, o dispararlo por código: `BlsSafetyGate.Trip("incidente SEV-n")`. El dispatcher lo comprueba antes de cada tool call, aunque el incidente viniera del pipe directo.
2. **Si la cadena de auditoría está rota**, el arranque ya dispara el kill switch; no reanudar hasta verificar `BlsAuditChain.VerifyDirectory(BlsPaths.LogsDir)`.
3. **Si hay sospecha de API key expuesta**: `BlsSecureStore.DeleteApiKey()` y rotar la clave en NVIDIA NIM antes de volver a guardarla. Nunca pegar la clave en logs, issues ni chats.
4. **Si el proceso Access está fuera de control**: cerrar MSACCESS.EXE. El log es append-only y sobrevive al proceso.

## 3. Erradicación y recuperación

- Identificar el evento en el JSONL por `correlation_id`/`trace_id` y verificar el hash con `BlsAuditChain.Verify`.
- Corregir la causa con un spec/ADR nuevo; no parchear sin test de regresión.
- Ejecutar build Debug/Release x86/x64, los 13 gates y la suite completa antes de rehabilitar.
- Restablecer con `BlsSafetyGate.Reset()` solo tras aprobación humana registrada.

## 4. Comunicación y post-mortem

- Divulgar la intervención (Pilar 5): el informe debe declarar asistencia de IA y registrar quién aprobó cada acción.
- En un plazo de 5 días hábiles: causa raíz, impacto, controles que fallaron, nueva evidencia en CI y actualización del registro de riesgos.
- Los incidentes SEV-1 se notifican al propietario del producto de forma inmediata.

## 5. Prueba del procedimiento

Simulacro trimestral: disparar el kill switch durante un turno con una tool `High`, comprobar que el dispatcher devuelve `SAFETY_TRIPPED` (-32006), que no hay ejecución en VBE y que el evento queda registrado. Evidencia: `JsonRpcSafetyTests.KillSwitchDisparado_BloqueaAntesDelHandler`.
