---
version: 1.0.0
date: 2026-10-15
status: approved
iso_standard: [42010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-001-PROJECT-IDENTITY.md, docs/design/ADR-008-NIM-TERMINAL.md]
tags: [architecture, adr, session-management, multi-session, slash-commands]
---

# ADR-013 — Gestión de múltiples sesiones por proyecto y botonera de comandos rápidos

> **Norma de referencia:** ISO/IEC 42010:2022 (Architecture Description) / ISO/IEC 25010:2023 (Usabilidad e Integridad de Datos)

## Estado
Aprobado — evoluciona el modelo de sesión única continua hacia soporte multi-sesión con selector temporal y botonera de comandos.

## Fecha
2026-10-15

## Contexto
El diseño original de persistencia ([ADR-001](file:///c:/desarrollos/balansoft-access-vba-agent/docs/design/ADR-001-PROJECT-IDENTITY.md) y [ADR-006](file:///c:/desarrollos/balansoft-access-vba-agent/docs/design/ADR-006-CHAT-HISTORY.md)) asociaba un único archivo JSON continuo por proyecto (`{ProjectKey}.json`).
Con el uso continuo, esto presenta dos limitaciones principales:
1. Imposibilidad de consultar o retomar sesiones de trabajo realizadas días atrás de forma separada o aislada (por ejemplo, para revisar una tarea de cálculo de hace dos días sin mezclarla con refactorizaciones actuales).
2. Dificultad para invocar intenciones repetitivas sobre el código VBA activo (explicar código, documentar, refactorizar, generar tests, consultar contexto o reiniciar conversación), requiriendo tipeo manual extenso en el prompt.

## Decisión de Arquitectura

### 1. Estructura de Persistencia Multi-Sesión (ISO/IEC 25010 Fiabilidad e Integridad)
Cada proyecto identificado por su `ProjectKey` (SHA-256 de la ruta canónica) dispondrá de un esquema estructurado:
- Carpeta de proyecto: `%APPDATA%\Balansoft\AIAddin\history\{ProjectKey}\`
- Catálogo de sesiones: `%APPDATA%\Balansoft\AIAddin\history\{ProjectKey}\sessions.json`
- Archivo individual por sesión: `%APPDATA%\Balansoft\AIAddin\history\{ProjectKey}\{SessionId}.json`
- **Retrocompatibilidad:** Si existe un archivo histórico legado `{ProjectKey}.json`, se importa y migra automáticamente como la primera sesión del proyecto sin pérdida de datos.

### 2. Estructura de Metadatos de Sesión (`SessionMetadata`)
```csharp
public sealed class SessionMetadata
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Nueva sesión";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public int MessageCount { get; set; }
}
```

El título de la sesión se inicializa con la marca de fecha/hora y se actualiza de forma automática con los primeros caracteres del primer mensaje emitido por el usuario (ej: *"Resta de dos enteros..."*).

### 3. Selector de Sesiones en WinForms (ISO/IEC 25010 Usabilidad)
En la parte superior de la terminal se incorpora una barra de navegación de sesiones:
- `ComboBox` estilizado con lista de sesiones ordenadas descendentemente por `UpdatedAtUtc`.
- Botón **`[+ Nueva Sesión]`** para crear y conmutar inmediatamente a un nuevo hilo de chat limpio.
- Cambio de sesión con autoguardado previo de cualquier mensaje pendiente en la sesión actual.

### 4. Botonera de Comandos Rápidos (Slash Commands)
Encima de la caja de entrada de texto se implementa una botonera compacta y accesible:
- `[/explain]` — Genera y envía el prompt para explicar el módulo o selección activa.
- `[/doc]` — Solicita documentar y agregar comentarios a las funciones y procedimientos del módulo.
- `[/refactor]` — Solicita optimizaciones y mejoras de calidad de código VBA.
- `[/test]` — Solicita la generación de procedimientos de prueba unitaria en VBA.
- `[/context]` — Muestra el contexto actual detectado en el VBE (módulo activo, líneas seleccionadas).
- `[/clear]` — Limpia los mensajes en pantalla y crea una nueva sesión de trabajo.

Los comandos también pueden escribirse directamente como texto en la caja de prompt (`/explain`, `/doc`, etc.) ejecutando la misma acción.

## Consecuencias
- **Positivas:** El usuario puede navegar en el tiempo entre sesiones de diferentes días, conservar un histórico ordenado y ejecutar acciones rápidas sobre VBA con un solo clic.
- **Riesgos mitigados:** La migración transparente evita que se pierda el historial existente de proyectos previos.
