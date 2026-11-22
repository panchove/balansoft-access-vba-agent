---
version: 1.0.0
date: 2026-11-22
status: approved
iso_standard: [42001, 23894, 27701, 23894]
aligns_to: [AI-POLICY.md, AI-RISK-REGISTER.md, OWASP-AGENTIC-THREAT-MODEL.md]
tags: [governance, aia, impact-assessment, eu-ai-act]
---

# Evaluación de Impacto de IA (AIA) — Balansoft Access VBA Agent

> Pilar 1 / ISO/IEC 42001 §6.1 e ISO/IEC 23894. Aprobación humana registrada: propietario del producto, 2026-11-22.

## 1. Propósito y alcance

El sistema es un asistente de edición de VBA integrado en el VBE de Microsoft Access. Sugiere y escribe código, crea objetos de base de datos y ejecuta SQL sobre el proyecto Access abierto. **No toma decisiones sobre personas**: no hay scoring, selección de personal, crédito, diagnóstico ni vigilancia.

| Aspecto | Valoración |
|---------|-----------|
| Rol del modelo | Asistente (EU AI Act Art. 50), no autoridad |
| Riesgo del sistema | Limitado, con salvaguardas reforzadas para herramientas de escritura |
| Personas afectadas | Desarrolladores y usuarios de bases de Access que usan el add-in |
| Datos tratados | Código VBA, metadatos del proyecto, texto seleccionado en el editor |
| Datos externos | Prompt y contexto se envían a NVIDIA NIM previo saneamiento PII |

## 2. Impactos identificados

| ID | Impacto | Probabilidad | Severidad | Control |
|----|---------|--------------|-----------|---------|
| I-01 | Código destructivo ejecutado sin revisión | Media | Alta | HITL en terminal **y** en `JsonRpcDispatcher`; kill switch compartido |
| I-02 | Fuga de credenciales/PII al proveedor | Media | Alta | `BlsPiiScrubber` + `BlsPiiConsentGate` ("No" por defecto) |
| I-03 | Pérdida de trazabilidad de una acción | Baja | Alta | JSONL con cadena SHA-256 verificada al arranque; kill switch si está rota |
| I-04 | Respuesta falsa de éxito | Baja | Alta | DbC y propagación tipada de errores; tests de regresión R-02 |
| I-05 | Dependencia excesiva del asistente | Alta | Media | Divulgación persistente `AiDisclosure`: "revisa el código generado" |
| I-06 | Saturación del proveedor por reintentos | Baja | Media | Full jitter, `Retry-After`, breaker y timeout global de turno |

## 3. Medidas y residualidad

Todos los impactos con severidad Alta disponen de control ejecutable verificado en CI (gates 8c, 8d, 8e y 8f). El riesgo residual se considera **aceptable** siempre que se mantengan: aprobación humana para tools `Medium`/`High`, ACL del pipe por SID, verificación de la cadena de auditoría al arranque y revisión humana del código generado.

## 4. Seguimiento

Revisión en cada release y tras cualquier incidente (Pilar 14). Esta AIA se entrega en el mismo PR que cualquier cambio de modelo, prompt, tools o política (Pilar 17).
