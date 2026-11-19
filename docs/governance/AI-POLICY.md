---
version: 1.0.0
date: 2026-11-19
status: approved
iso_standard: [42001, 27001, 25010, 29148, 12207, 27701]
aligns_to: [AGENTS.md, CONSTITUTION.md, docs/PRD.md]
tags: [governance, ai-policy, iso-42001, eu-ai-act, disclosure]
---

# Política de Uso de Inteligencia Artificial — Balansoft Access VBA Agent

> Aprobación humana registrada: propietario del producto, 2026-11-19 (Fase F1 del plan de remedición).
> Alcance: COM Add-in para Access/Excel con terminal NVIDIA NIM embebida en el VBE.
> Marco: 25 Pilares de `AI_GOVERNANCE.md` v2.1, ISO/IEC 42001:2023, EU AI Act Art. 50, OWASP Agentic 2025.

## 1. Propósito

Definir los límites de uso del modelo de IA dentro del add-in, las responsabilidades humanas
y los controles mínimos exigibles antes de cada entrega. Esta política es operativa: sus
puntos de control se verifican en CI (gate *Gobernanza IA*) y no dependen de la voluntad del autor.

## 2. Principios

| ID     | Principio                                                                                              | Control verificable                                                |
|--------|--------------------------------------------------------------------------------------------------------|--------------------------------------------------------------------|
| P-AI-1 | El modelo es un asistente, no una autoridad. Toda acción sobre VBA/Access requiere supervisión humana. | Gate HITL (F2)                                                     |
| P-AI-2 | La identidad IA se divulga de forma visible en la terminal.                                            | Clave `AiDisclosure` + `_lblAiDisclosure`                          |
| P-AI-3 | Las operaciones del modelo pasan por una whitelist de tools con esquema validado.                      | `JsonRpcMethods.All` + JSON Schema                                 |
| P-AI-4 | Ningún secreto, PII ni fragmento de código sensible se envía al proveedor sin revisión.                | `BlsPiiScrubber` + `BlsPiiConsentGate` + gate 8f (F5)              |
| P-AI-5 | Todo comportamiento del modelo es trazable y auditable.                                                | `BlsLogger` JSONL + `BlsAuditChain.Verify` + `correlation_id` (F3) |
| P-AI-6 | El sistema degrada con seguridad: parada determinista independiente del modelo.                        | `BlsSafetyGate` + botón de kill switch (F2)                        |
| P-AI-7 | La configuración del modelo está versionada e inventariada.                                            | `docs/governance/AI-BOM.md`                                        |
| P-AI-8 | Un solo agente de desarrollo; la gobernanza se aplica por gates de CI, no por agentes que "revisan".   | `agents_index` = 1                                                 |

## 3. Uso permitido

- Generación, explicación, documentación y refactorización de código VBA a petición explícita del usuario.
- Lectura de contexto del proyecto VBE/Access a través de los métodos `read-only` de la whitelist.
- Tool calls de escritura **siempre** precedidas de aprobación humana (F2).

## 4. Uso prohibido

- Ejecutar DDL/DML, borrar módulos o migrar objetos sin confirmación humana.
- Enviar credenciales, cadenas de conexión, correos o datos personales al endpoint NIM.
- Atribuir las respuestas a una persona o a otro modelo distinto del declarado en `AI-BOM.md`.
- Desactivar los gates de CI ni el registro de auditoría para acelerar una entrega.
- Cargar modelos, prompts o servidores MCP externos en tiempo de ejecución (Pilar 10).

## 5. Divulgación (EU AI Act Art. 50 / Pilar 5)

La terminal muestra de forma persistente, en los cuatro idiomas soportados, el texto definido
por la clave `AiDisclosure`: identidad del sistema (Balansoft), presencia de IA (NVIDIA NIM)
y obligación de revisar el código generado. Los mensajes de commit incluyen el trailer
`AI-Assistant: OpenCode Big Pickle`, que sustituye la atribución incorrecta a Claude Code.
El agente que produzca este u otros informes debe declarar su naturaleza de IA.

## 6. Supervisión humana (Pilar 4)

| Nivel | Operaciones                                                                                     | Gate                                                                          |
|-------|-------------------------------------------------------------------------------------------------|-------------------------------------------------------------------------------|
| Bajo  | Lectura, explicación, resumen                                                                   | Ninguno (registro en log)                                                     |
| Medio | `write_module`, `create_module`, `create_form`, `create_table`, `create_query`, `create_ribbon` | Confirmación UI + log de aprobación con `payload_sha256`                      |
| Alto  | `delete_module`, `execute_sql`, `migrate_*`                                                     | Confirmación UI con detalle del payload + kill switch accesible y rechequeado |

Implementado en F2 (`BlsSafetyGate`, `BlsToolRiskPolicy`, `BlsHumanApprovalGate` y botón de
kill switch en la terminal). El rechazo devuelve un JSON de error y **no invoca el bridge**.
Las operaciones de nivel bajo no requieren aprobación, pero quedan bloqueadas si el kill
switch está disparado. F3 cerró R-04/R-05 con registro append-only y
trazabilidad por turno; F4 cerró R-06/R-09 y F5 cerró R-03/R-07/R-10/R-11.
Los doce riesgos del registro están cerrados.

## 7. Gestión de cambios

Toda modificación del modelo, endpoint, prompt, tools o de esta política se registra en
`docs/governance/AI-BOM.md` y se entrega en el mismo PR que el cambio (Pilar 17).
La revisión de riesgos se realiza en cada release y tras cualquier incidente (Pilar 14).

## 8. Referencias

- `docs/governance/AI-RISK-REGISTER.md`
- `docs/governance/AI-BOM.md`
- `docs/governance/OWASP-AGENTIC-THREAT-MODEL.md`
- `specs/015-AI-DISCLOSURE.md`
- `AGENTS.md` · `docs/CONSTITUTION.md`
