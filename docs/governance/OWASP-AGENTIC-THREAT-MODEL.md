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

| ID | Amenaza | Aplicable | Control actual | Mitigación | Fase |
|----|---------|-----------|----------------|------------|------|
| ASI01 | Secuestro de objetivo (*goal hijacking*) | Sí | System prompt con reglas de error | Validación de intención + confirmación UI para escrituras | F2 |
| ASI02 | Mal uso de herramientas | Sí | Whitelist `JsonRpcMethods.All`, `MaxToolIterations=8` | Esquema runtime + gate HITL por nivel de riesgo | F2, F4 |
| ASI03 | Abuso de identidad / permisos | Sí | DPAPI CurrentUser, sin ROT | Archivo de riesgo bajo LP + registro de aprobación | F2 |
| ASI04 | Inyección de memoria/contexto | Sí | Historial por proyecto | Sanitización de historial + límite de contexto + TTL | F5 |
| ASI05 | Ejecución de código no aislada | Sí | Sin sandbox; el modelo edita el VBE real | Confirmación explícita + preview del payload + backup | F2 |
| ASI06 | Envenenamiento de RAG | No | Sin RAG | — | N/A |
| ASI07 | Comunicación insegura entre agentes | Parcial | Sin multi-agente desde F1 | Un único agente; gate de `agents_index` en CI | F1 ✅ |
| ASI08 | Cascada de fallos | Sí | Reintentos 429/5xx con backoff | Circuit breaker + jitter + timeout global | F4 |
| ASI09 | Confianza humana engañosa (falso éxito) | Sí | `DeleteModule` devuelve sin hacer nada | Excepción obligatoria + postcondición + verificación | F2 |
| ASI10 | Agente rogue / supply chain | Sí | AI-BOM con hash del prompt | Verificación en CI + prohibición de carga dinámica | F1 🟡 |

## 2. Controles de detección

| Control | Estado |
|---------|--------|
| Gate de CI: presencia de políticas, BOM y threat model | ✅ F1 |
| Gate de CI: divulgación IA y agente único | ✅ F1 |
| Logs JSON con hash encadenado | ❌ F3 |
| Verificación de integridad de la cadena de hash | ❌ F3 |
| Red teaming adversario (prompt injection en selección VBE) | ❌ F5 |

## 3. Plan de pruebas adversarias (F5)

1. Inyectar en la selección VBE una instrucción que intente cambiar el system prompt → el modelo no debe obedecer.
2. Enviar una cadena de conexión en el código seleccionado → debe ser redactada antes de `BuildJson`.
3. Forzar un `tool_call` con `confirm: true` sin aprobación UI → debe ser rechazado.
4. Reenviar el mismo `id` JSON-RPC dos veces → una sola ejecución.

## 4. Salida

Este modelo de amenazas se actualiza en el mismo PR que cualquier cambio en tools, prompt o modelo.
La evidencia de ejecución se conserva en los logs Audit (F3).
