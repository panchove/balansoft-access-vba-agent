---
version: 1.0.0
date: 2026-11-19
status: implementado
iso_standard: [29148, 25010, 42001]
aligns_to: [PRD.md, AGENTS.md, docs/governance/AI-POLICY.md]
tags: [spec, ai-disclosure, eu-ai-act, i18n]
---

# Spec 015 — Divulgación de identidad de IA

## Cabecera

| Campo | Valor |
|-------|-------|
| Estado | `implementado` |
| PRD | Requisito no funcional de gobernanza (EU AI Act Art. 50) |
| ADRs | — |
| Tests | 1 verde (`AiDisclosure_PresentInAllLanguages`) + gate de CI |

## Contexto

El add-in envía código del usuario a un modelo externo y ejecuta tool calls en el VBE.
El usuario debe saber, de forma visible y permanente, que interactúa con un sistema de IA,
qué organización lo provee y que debe revisar el código generado.

## Alcance

- Incluye: etiqueta persistente en la terminal, claves localizadas en 4 idiomas, trailer de commit.
- Excluye: diálogo modal (la divulgación no debe bloquear el flujo), cambios en el prompt del modelo.

## Requisitos

- **R001** — La terminal debe mostrar el texto de `AiDisclosure` en un control visible sin necesidad de desplazarse.
- **R002** — La clave `AiDisclosure` debe existir en `es`, `en`, `pt` y `fr`.
- **R003** — El texto debe identificar a Balansoft, a NVIDIA NIM como IA y la obligación de revisar el código.
- **R004** — El cambio de idioma debe actualizar la etiqueta sin reiniciar la terminal.
- **R005** — Los commits generados con asistencia de IA deben incluir `AI-Assistant: OpenCode Big Pickle`.

## Criterios de aceptación

1. **Given** la terminal abierta, **when** se inspeccionan sus controles, **then** existe `_lblAiDisclosure` con `Dock=Top`.
2. **Given** cualquier idioma soportado, **when** se consulta `AiDisclosure`, **then** se devuelve texto no vacío distinto de la clave.
3. **Given** un cambio de idioma en caliente, **when** se refresca la UI, **then** la etiqueta muestra el nuevo texto.
4. **Given** el repositorio, **when** corre el gate de gobernanza, **then** verifica arquitectura de agente único y trailer de divulgación.

## Tests obligatorios

- `BlsStringsTests.AiDisclosure_PresentInAllLanguages`
- Gate de CI: `Gobernanza IA - documentos, divulgacion y agente unico`

## Desviaciones y riesgos

- La divulgación es visible pero no requiere aceptación explícita; se considera suficiente para Art. 50
  al ser persistente y estar junto al punto de interacción.
- `docs/governance/AI-BOM.md` registra el hash del prompt a la fecha de este spec; debe actualizarse si el prompt cambia.
