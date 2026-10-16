---
version: 1.0.0
date: 2026-10-15
status: implementado
iso_standard: [29148, 25010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-001-PROJECT-IDENTITY.md, docs/design/ADR-013-SESSION-MANAGEMENT.md]
tags: [spec, session, delete, history, ui]
---

# 013 — Eliminación de sesiones de historial de chat

> **Norma de referencia:** ISO/IEC 29148:2018 (Requirements Engineering) / ISO/IEC 25010:2023 (Usabilidad e Integridad)  
**Estado:** `implementado`  
**PRD:** FR-10  
**ADRs:** ADR-001, ADR-013  

---

## Contexto

El spec 011 (SESSION-MANAGEMENT) cubre la creación, listado y alternancia entre múltiples sesiones de chat por proyecto, pero no contempla la eliminación de sesiones individuales ni el vaciado completo del historial. Sin capacidad de eliminar, el historial crece sin límite y el usuario no puede desechar sesiones equivocadas o sensibles.

---

## Alcance

**Entra:**

- Eliminar una sesión específica del catálogo y del disco.
- Eliminar todo el historial de un proyecto (vaciar).
- Confirmación de borrado en la UI antes de eliminar.
- Actualización del catálogo `sessions.json` tras la eliminación.
- Recarga de la sesión activa si la eliminada era la actual.

**No entra:**

- Eliminación de mensajes individuales dentro de una sesión.
- Sincronización de eliminación entre máquinas.
- Papelera o recuperación de sesiones eliminadas.

---

## Requisitos

### R001 — Eliminar sesión específica

Dado un proyecto con al menos una sesión en el catálogo  
Cuando el usuario selecciona una sesión y confirma su eliminación  
Entonces la sesión se elimina del catálogo `sessions.json` y su archivo `{sessionId}.json` se borra del disco.

### R002 — Eliminar todo el historial del proyecto

Dado un proyecto con sesiones en el catálogo  
Cuando el usuario elige eliminar todo el historial  
Entonces el directorio del proyecto y el archivo legacy `{ProjectKey}.json` se eliminan del disco.

### R003 — Confirmación de borrado

Dado que el usuario solicita eliminar una sesión  
Cuando se muestra el diálogo de confirmación  
Entonces la eliminación no se ejecuta si el usuario cancela.

### R004 — Recarga tras eliminación de sesión activa

Dado que la sesión activa se elimina  
Cuando la eliminación se completa  
Entonces la Terminal recarga la lista de sesiones y crea una nueva sesión vacía si el catálogo queda vacío.

### R005 — Sin sesión seleccionada

Dado que no hay ninguna sesión seleccionada en el catálogo  
Cuando el usuario pulsa eliminar  
Entonces la Terminal muestra un aviso y no ejecuta ninguna eliminación.

---

## Criterios de aceptación

```
Dado un proyecto con dos sesiones (S1 y S2)
Cuando se elimina S1
Entonces el catálogo contiene solo S2
Y el archivo S1.json ya no existe en disco

Dado un proyecto con una sesión
Cuando se elimina todo el historial
Entonces el catálogo está vacío
Y el directorio del proyecto ya no existe

Dado que no hay sesión seleccionada
Cuando el usuario pulsa eliminar
Entonces no se elimina ninguna sesión
Y se muestra un aviso al usuario

Dado que la sesión activa es S1 y se elimina S1
Cuando la eliminación se completa
Entonces la Terminal recarga la lista y crea una nueva sesión vacía
```

---

## Tests obligatorios

| Test                                                      | Cubre |
|-----------------------------------------------------------|-------|
| `DeleteSession_SpecificSession_RemovedFromCatalogAndDisk` | R001  |
| `DeleteSession_NullSessionId_DeletesAllProjectHistory`    | R002  |
| `DeleteSession_NoSessionSelected_ShowsWarning`            | R005  |

---

## Desviaciones y riesgos

- La UI de confirmación usa `MessageBox` de WinForms; no hay prueba unitaria de la interacción del usuario con el cuadro de diálogo.
- No hay prueba de integración contra Access real para verificar la eliminación desde el menú de la Terminal.
- Si la sesión eliminada era la activa, se crea una nueva sesión automáticamente; esto es un cambio de comportamiento implícito no documentado en 011.
