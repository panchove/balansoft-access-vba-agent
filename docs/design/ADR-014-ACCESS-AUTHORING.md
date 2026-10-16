---
version: 1.0.0
date: 2026-10-15
status: approved
iso_standard: [42010, 25010]
aligns_to: [PRD.md, AGENTS.md, docs/design/ADR-004-OBJECT-DETAIL.md, docs/design/ADR-006-VBE-BRIDGE.md]
tags: [architecture, adr, access-authoring, forms, tables, queries, ribbons, sql]
---

# ADR-014 — Herramientas de Creación y Autoría de Objetos Access (Formularios, Tablas, Vistas y Ribbons)

> **Norma de referencia:** ISO/IEC 42010:2022 (Architecture Description) / ISO/IEC 25010:2023 (Adecuación Funcional, Fiabilidad e Interoperabilidad)

## Estado
Aprobado — evoluciona las capacidades de integración con Microsoft Access desde la inspección de solo lectura (ADR-004) hacia la autoría directa y modificación de objetos nativos bajo enlace tardío seguro.

## Fecha
2026-10-15

## Contexto
Originalmente, el agente contaba únicamente con herramientas de manipulación del editor VBE (`write_module`, `create_module`, etc.) e inspección de solo lectura de objetos Access (`project_summary`, `list_objects`, `object_detail`).
Cuando el usuario solicitaba crear elementos nativos de la aplicación Access (como formularios de login, tablas relacionales, consultas filtradas o menús Ribbon), el LLM carecía de herramientas dedicadas en el bridge JSON-RPC y fallaba o intentaba simular la acción sin modificar el archivo `.accdb`.

## Decisión de Arquitectura

Se expande el protocolo JSON-RPC, los contratos de `IBlsAccess` y la fachada COM de enlace tardío `BlsComHelper` para soportar 5 herramientas nativas de autoría:

### 1. Creación de Formularios (`create_form`)
- **Firma:** `create_form(name: string, caption?: string, controls?: Array<CreateFormControl>)`
- **Mapeo COM:** Invoca `Application.CreateForm()` en el hilo STA de la UI de Access mediante `_ui.RunAsync`.
- **Controles soportados:** `TextBox` (con soporte para `label` asociada e `is_password: true` asignando `InputMask = "Password"`), `CommandButton` y `Label`.
- **Cálculo de Maquetación:** Distribución geométrica automática en twips (1440 twips = 1 pulgada) para apilar filas de campos con etiquetas y botonera inferior.
- **Persistencia:** Guarda y renombra atómicamente el formulario usando `DoCmd.Save(acForm, tempName)` y `DoCmd.Rename(name, acForm, tempName)`. Habilita `HasModule = true` para permitir posterior inyección de código VBA vía `write_module`.

### 2. Creación de Tablas (`create_table`)
- **Firma:** `create_table(name: string, columns: Array<CreateColumnSpec>)`
- **Mapeo DDL:** Traduce tipos canónicos (`AutoNumber`, `Text`, `Memo`, `Integer`, `Long`, `Byte`, `Date`, `Currency`, `Boolean`, `Double`, `Single`) a la sintaxis DDL de Microsoft Jet/ACE (`AUTOINCREMENT`, `VARCHAR(size)`, `MEMO`, `INTEGER`, etc.).
- **Restricciones:** Configura claves primarias (`CONSTRAINT [PK_tbl] PRIMARY KEY ([col])`) y obligatoriedad `NOT NULL`.
- **Ejecución:** Invoca `CurrentDb.Execute(ddl, dbFailOnError)`.

### 3. Creación de Consultas/Vistas (`create_query`)
- **Firma:** `create_query(name: string, sql: string)`
- **Mapeo DAO:** Reemplaza o crea objetos `QueryDef` en `CurrentDb.QueryDefs`.
- **Idempotencia:** Si ya existe una consulta con el nombre proporcionado, la elimina previamente para garantizar la sobrescritura limpia.

### 4. Ejecución Arbitraria DDL/DML (`execute_sql`)
- **Firma:** `execute_sql(sql: string)`
- **Mapeo DAO:** Ejecuta sentencias de acción vía `CurrentDb.Execute(sql, 128)` y devuelve el total de filas impactadas leyendo `db.RecordsAffected`.

### 5. Creación y Gestión de Ribbons (`create_ribbon`)
- **Firma:** `create_ribbon(name: string, ribbon_xml: string)`
- **Esquema de Sistema:** Crea automáticamente la tabla del sistema `USysRibbons` en la base de datos actual si aún no existe (`ID AUTOINCREMENT PRIMARY KEY, RibbonName VARCHAR(255) NOT NULL, RibbonXML MEMO NOT NULL`).
- **Persistencia:** Inserta o actualiza el XML de CustomUI en `USysRibbons` escapando comillas para garantizar integridad.

### 6. Inspección de Menús Clásicos CommandBars (`list_commandbars`)
- **Firma:** `list_commandbars(custom_only: bool = true)`
- **Inspección COM:** Recorre `Application.CommandBars`, filtrando opcionalmente las barras nativas del sistema (`BuiltIn == false`), extrayendo nombres, tipos y colecciones de controles (`Caption`, `OnAction`, `TooltipText`).

### 7. Migración Automática CommandBar a Ribbon XML (`migrate_commandbar_to_ribbon`)
- **Firma:** `migrate_commandbar_to_ribbon(commandbar_name: string, target_ribbon_name?: string)`
- **Transformación Declarativa:** Convierte automáticamente la estructura jerárquica de botones de la barra clásica en un XML estándar CustomUI Fluent UI (`<tab>`, `<group>`, `<button onAction="...">`), limpiando los atajos de teclado clásicos `&` e inyectándolo directamente en la tabla `USysRibbons`.

## Consecuencias
- **Positivas:** El asistente es capaz de construir aplicaciones Access completas (tablas, consultas, formularios UI, ribbons y módulos VBA) en lenguaje natural y modernizar proyectos legados de Access 2003 automáticamente hacia Access 2010+.
- **Garantías de Diseño:** Todo el acceso COM cumple con las reglas no negociables de `AGENTS.md`: sin PIAs, enlace tardío dinámico encapsulado exclusivamente en `BlsComHelper.cs`, marshaling obligatorio a UI thread y validación por contratos `BlsContract`.
