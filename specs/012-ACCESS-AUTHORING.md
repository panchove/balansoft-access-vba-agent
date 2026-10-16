---
version: 1.0.0
date: 2026-10-15
status: implementado
iso_standard: [29148, 25010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-014-ACCESS-AUTHORING.md]
tags: [spec, access, authoring, forms, tables, queries, ribbons, sql]
---

# 012 — Herramientas de Autoría en Microsoft Access (Formularios, Tablas, Vistas y Ribbons)

> **Norma de referencia:** ISO/IEC 29148:2018 (Requirements Engineering) / ISO/IEC 25010:2023 (Calidad de Software)  
> **Estado:** `implementado`  
> **PRD:** FR-11 (Extensión de capacidades Access)  
> **ADRs:** ADR-014 (Creación y autoría de objetos Access)  
> **Tests:** 7 verdes en `JsonRpcDispatcherTests`

---

## Contexto

El asistente NIM requiere construir y estructurar bases de datos Access completas bajo peticiones directas del usuario en lenguaje natural: generar esquemas relacionales, consultas SQL, interfaces de usuario de formularios y cintas de comandos Ribbon de Office Fluent UI.

---

## Alcance

**Entra:**
- `create_form`: Creación de formularios con controles `TextBox`, `Label` y `CommandButton`, guardado y habilitación de `HasModule`.
- `create_table`: Creación de tablas mediante DDL SQL con columnas tipadas, `NOT NULL` y clave primaria.
- `create_query`: Creación y actualización idempotente de objetos `QueryDef`.
- `execute_sql`: Ejecución de cualquier sentencia SQL DDL/DML contra `CurrentDb`.
- `create_ribbon`: Registro de esquemas XML de Ribbon en la tabla del sistema `USysRibbons` (creando la tabla si no existe).

**No entra:**
- Modificación destructiva masiva de tablas sin confirmación previa del usuario.

---

## Requisitos

### R001 — Hilo Seguro (STA UI Thread)
Las llamadas COM a `Application.CreateForm` y manipulación de controles se despachan forzosamente en el hilo principal de Access mediante `_ui.RunAsync()`.

### R002 — Enlace Tardío Seguro (Sin PIAs)
Toda interacción COM con `Access.Application` o `DAO.Database` se implementa mediante reflexión y `dynamic` exclusivamente dentro de `BlsComHelper.cs`.

### R003 — Tolerancia a Fallos e Idempotencia
- `create_query`: Si el `QueryDef` ya existe, se elimina antes de crearlo de nuevo.
- `create_ribbon`: Si `USysRibbons` no existe en la base, se crea automáticamente; si ya existe una entrada con el mismo `RibbonName`, se actualiza.

---

## Criterios de Aceptación (Gherkin)

### Escenario: Creación de formulario con controles
```gherkin
Given un request JSON-RPC con método "create_form"
And name = "frmLogin" y una lista de controles (txtUser, txtPassword, btnLogin)
When el dispatcher procesa la solicitud
Then Access crea el formulario, ubica los controles y devuelve Success = true
```

### Escenario: Creación de tabla con clave primaria
```gherkin
Given un request JSON-RPC con método "create_table"
And name = "Usuarios" y columnas con "Id" AutoNumber y primary_key = true
When el dispatcher ejecuta el comando
Then CurrentDb ejecuta el DDL y se crea la tabla correctamente
```

### Escenario: Creación de consulta guardada
```gherkin
Given un request JSON-RPC con método "create_query"
And name = "qryActivos" y sentencia SQL válida
When el dispatcher ejecuta la creación
Then CurrentDb registra el QueryDef correspondiente
```

### Escenario: Creación de cinta de opciones (Ribbon)
```gherkin
Given un request JSON-RPC con método "create_ribbon"
And name = "rbnApp" y XML válido de customUI
When el dispatcher ejecuta el registro
Then se almacena el XML en la tabla USysRibbons
```

### Escenario: Listado de menús clásicos CommandBars
```gherkin
Given un request JSON-RPC con método "list_commandbars"
And custom_only = true
When el dispatcher consulta las CommandBars
Then devuelve la lista de barras de herramientas y menús personalizados con sus controles
```

### Escenario: Migración automática de CommandBar a Ribbon XML
```gherkin
Given un request JSON-RPC con método "migrate_commandbar_to_ribbon"
And commandbar_name = "MenuBar2003"
When el dispatcher ejecuta la migración
Then se genera el Ribbon XML a partir de los controles de la CommandBar y se almacena en USysRibbons
```

### Escenario: Inspección de proyecto ADP
```gherkin
Given un request JSON-RPC con método "inspect_adp"
When el dispatcher inspecciona el proyecto activo
Then detecta el tipo de proyecto, servidor SQL Server, base de datos y conteo de objetos
```

### Escenario: Migración de proyecto ADP a contenedor ACCDB
```gherkin
Given un request JSON-RPC con método "migrate_adp_to_accdb"
When el dispatcher ejecuta la migración
Then crea el archivo .accdb y exporta todos los formularios, informes, macros y módulos
```

---

## Tests Obligatorios

| Nombre del Test | Archivo | Estado |
|---|---|---|
| `Bridge_CreateForm_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_CreateTable_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_CreateQuery_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_ExecuteSql_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_CreateRibbon_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_ListCommandBars_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_MigrateCommandBarToRibbon_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_InspectAdp_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
| `Bridge_MigrateAdpToAccdb_EjecutaYDevuelveResult` | `JsonRpcDispatcherTests.cs` | Superado |
