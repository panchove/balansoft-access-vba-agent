---
version: 1.0.0
date: 2026-10-15
status: aceptado
iso_standard: [29148, 25010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-001-PROJECT-IDENTITY.md, docs/design/ADR-009-SECURE-STORE.md]
tags: [spec, chat-history, persistence, debounce]
---

# 006 — Historial de chat

**Norma de referencia:** ISO/IEC 29148:2018 (Requirements Engineering) / ISO/IEC 25010:2023  
**Estado:** `aceptado`
**PRD:** FR-10
**ADRs:** ADR-001 (identidad de proyecto), ADR-009 (persistencia)
**Spec de diseño:** (cubierto por `docs/design/ADR-006-VBE-BRIDGE.md` y
`docs/design/ADR-008-NIM-TERMINAL.md`)
**Tests:** 0 propios

---

## Contexto

Historial persistente por proyecto Access, en JSON bajo
`%APPDATA%\Balansoft\AIAddin\history\{projectKey}.json`. La clave del archivo
sale de `IBlsVbe.GetProjectKey()` (ADR-001).

Implementado en `BlsChatHistory` con `HistoryDocument` y `HistoryMessage`.
Sin cobertura de test.

---

## Alcance

**Entra:**

- Guardar y cargar el historial de un proyecto.
- Escritura con debounce.
- Rotación por tamaño.
- Renderizado del historial en la Terminal.

**No entra:**

- Búsqueda en historial (fuera del MVP).
- Sincronización entre máquinas.
- Migración entre versiones de esquema salvo la de `Version = 1`.

---

## Requisitos

### R001 — Aislamiento por proyecto

El archivo se nombra por `projectKey` (SHA256 de la ruta normalizada,
ADR-001). Dos proyectos distintos nunca comparten historial.

### R002 — Proyectos efímeros

Si el proyecto no está guardado, `GetProjectKey()` marca `Ephemeral`. En ese
caso el historial **no se persiste** y la Terminal lo avisa en la barra de
estado. Un proyecto sin guardar no debe dejar rastro en disco.

### R003 — Formato

- `HistoryDocument`: `Version`, `ProjectKey`, `Ephemeral`, `UpdatedUtc`, `Messages`.
- `HistoryMessage`: `Role` (`user` | `assistant` | `tool`), `Content`, `Ts`,
  `Cancelled`, `ToolName`, `ToolCallId`.
- JSON UTF-8 legible. Un archivo corrupto lanza `BlsHistoryCorruptException`,
  no se traga en silencio ni se sobrescribe.

### R004 — Escritura con debounce

Guardar en cada chunk de texto generaría una escritura por token. La
escritura se agrupa con debounce, y hay un flush explícito al cerrar la
terminal (spec 004, R004).

### R005 — Rotación

Si el archivo supera un tamaño máximo, se trunca o se rota. Un historial que
crece sin límite acaba fallando la escritura por disco lleno en mitad de un
turno, que es el peor momento posible.

### R006 — Errores tipados

- `BlsHistoryIOException` — problema de E/S.
- `BlsHistorySchemaException` — JSON válido pero con forma inesperada.
- `BlsHistoryCorruptException` — ilegible.

### R007 — Renderizado

Al abrir, la Terminal reconstruye la conversación. Un mensaje de tool se
distingue por su icono según tenga o no error.

---

## Criterios de aceptación

```
Dado un historial guardado para el proyecto A
Cuando se abre el proyecto B
Entonces no aparecen los mensajes de A

Dado un proyecto sin guardar
Cuando se cierra la terminal
Entonces no se escribe ningún archivo de historial

Dado un archivo de historial con JSON inválido
Cuando se carga
Entonces lanza BlsHistoryCorruptException
Y el archivo original no se sobrescribe

Dado una respuesta cancelada por el usuario
Cuando se guarda
Entonces el mensaje queda con Cancelled igual a true

Dado un historial que crece por encima del máximo
Cuando se guarda
Entonces el archivo se rota y no falla la escritura

Dado varios chunks de texto en un turno
Cuando se guarda
Entonces no hay una escritura por chunk
```

---

## Tests obligatorios

Ninguno implementado.

| Test | Cubre |
|---|---|
| `History_RoundTrip_DevuelveMismosMensajes` | R003 |
| `History_DosProyectos_NoCompartenArchivo` | R001 |
| `History_ProyectoEphemeral_NoPersiste` | R002 |
| `History_JsonInvalido_LanzaBlsHistoryCorruptException` | R003, R006 |
| `History_EsquemaInvalido_LanzaBlsHistorySchemaException` | R006 |
| `History_ArchivoGrande_RotaSinFallar` | R005 |
| `History_MensajeCancelado_PersisteCancelled` | R003 |

Todos son testeables con un directorio temporal, sin Office.

---

## Desviaciones y riesgos

- **Sin tests.** Al ser serialización JSON a disco, es la capa con más
  superficie para corrupción silenciosa y la segunda sin cubrir.
- **Falta `ToolCalls` en `HistoryMessage`.** Es el bug latente descrito en
  spec 004: al recargar, un `role="tool"` puede viajar sin el `assistant` con
  `tool_calls` que lo precede, y NIM responde 400. Persiste en cada
  conversación que usó tools.
  - Opciones: añadir `ToolCalls` a `HistoryMessage`; o no persistir mensajes
    `tool` y reconstruirlos; o filtrarlos al construir el request y aceptar
    la pérdida de contexto.
- **Rotación (R005) sin verificar.** No está claro qué límite se usa ni qué
  ocurre con el archivo anterior.
- **`OnFormClosing` usa `.Wait(TimeSpan.FromSeconds(2))`.** Si el flush no
  termina en 2 s, el historial se pierde silenciosamente al cerrar Access.