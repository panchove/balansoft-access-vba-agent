---
version: 1.0.0
date: 2026-10-15
status: aceptado
iso_standard: [29148, 25010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-001-PROJECT-IDENTITY.md, docs/design/ADR-013-SESSION-MANAGEMENT.md]
tags: [spec, multi-session, history, slash-commands, ui]
---

# 011 — Gestión de múltiples sesiones y comandos rápidos

> **Norma de referencia:** ISO/IEC 29148:2018 (Requirements Engineering) / ISO/IEC 25010:2023 (Usabilidad e Integridad)  
**Estado:** `aceptado`  
**PRD:** FR-02, FR-10  
**ADRs:** ADR-001, ADR-006, ADR-008, ADR-013  

---

## Contexto

Evolución del historial de chat por proyecto para permitir la creación, consulta y alternancia entre múltiples sesiones históricas (hilos de chat) asociadas a una misma base de datos Access, así como el acceso inmediato a intenciones recurrentes mediante comandos de barra inclinada y botonera rápida.

---

## Requisitos

### R001 — Catálogo de sesiones por proyecto
- Cada proyecto almacena sus sesiones en `%APPDATA%\Balansoft\AIAddin\history\{ProjectKey}\`.
- Existe un catálogo centralizado `sessions.json` por proyecto que lista los metadatos de cada sesión:
  - `Id`: identificador único de sesión (UUID o timestamp compacto).
  - `Title`: título descriptivo (primeros 50 caracteres del primer prompt de usuario).
  - `CreatedAtUtc`: fecha y hora de creación.
  - `UpdatedAtUtc`: fecha y hora del último mensaje.
  - `MessageCount`: cantidad de mensajes en la sesión.

### R002 — Retrocompatibilidad y migración automática
- Si existe un archivo preexistente `{ProjectKey}.json`, se importa y migra automáticamente como la primera sesión en `{ProjectKey}/sessions.json`, preservando todos los mensajes.

### R003 — Selector de sesiones en la Terminal
- La interfaz WinForms dispone de un selector desplegable de sesiones en la parte superior.
- Al seleccionar una sesión, la terminal guarda el estado de la sesión actual y carga los mensajes de la sesión seleccionada.
- Se incluye un botón `+ Nueva Sesión` que inicializa un hilo de chat en blanco.

### R004 — Botonera de comandos rápidos
- Una botonera accesible sobre el control de entrada permite ejecutar con un clic:
  - `/explain` — Explicar código o módulo activo.
  - `/doc` — Documentar y comentar funciones y procedimientos.
  - `/refactor` — Proponer optimizaciones y mejoras VBA.
  - `/test` — Generar pruebas unitarias en VBA.
  - `/context` — Mostrar el contexto activo del VBE.
  - `/clear` — Reiniciar la conversación en una sesión limpia.
- Los comandos también son interpretados al ingresarse como texto directo en la caja de prompt.
