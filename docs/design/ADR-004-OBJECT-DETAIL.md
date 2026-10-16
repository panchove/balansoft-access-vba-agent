---
version: 1.0.0
date: 2025-01-01
status: approved
iso_standard: [42010]
aligns_to: [PRD.md, AGENTS.md, CONSTITUTION.md]
tags: [architecture, adr, object-detail, access-schema, readonly]
---

# ADR-004 — Contrato de `object_detail` (Access read-only)

## Estado
Aceptado — resuelve gap del PRD §3.2 y §7 FR-11.

## Fecha
2025-01-01

## Contexto
El bridge expone 3 operaciones de solo lectura sobre objetos Access:
`project_summary`, `list_objects`, `object_detail`.

El PRD no define:

- Qué campos devuelve `object_detail`.
- Si incluye SQL (potencialmente kilométrico).
- Cómo se limita el tamaño para no reventar el token budget de
  Nemotron 3 Ultra (contexto finito y caro).
- Qué hacer con objetos del sistema (`MSys*`, `~*`).
- Si cuenta registros (potencialmente costoso en tablas grandes).

Necesitamos un contrato **estable, acotado y predecible** que dé al modelo
suficiente contexto para razonar sin inundarlo.

## Decisión

### Parámetros de `object_detail`
```json
{
  "name": "Clientes",
  "type": "Table" | "Query" | "Form" | "Report" | "Macro",
  "includeSql": false,
  "maxFields": 200
}
```

- `name` (obligatorio): nombre exacto del objeto.
- `type` (obligatorio): uno de los 5 tipos soportados. Case-sensitive
  contra el nombre del contenedor de Access.
- `includeSql` (opcional, default `false`): solo aplica a `Query` y a
  `TableDef` con origen SQL.
- `maxFields` (opcional, default 200, máx 1000): límite de campos
  devueltos.

### Respuesta común (siempre presente)
```json
{
  "name": "Clientes",
  "type": "Table",
  "fieldCount": 14,
  "fields": [
    { "name": "Id",       "type": "Long",   "size": 4,  "required": true,  "description": "" },
    { "name": "Nombre",   "type": "Text",   "size": 50, "required": true,  "description": "" },
    { "name": "Email",    "type": "Text",   "size": 120,"required": false, "description": "" }
  ],
  "lastModified": "2025-01-01T00:00:00Z"
}
```

### Extensiones por tipo

#### `Table`
```json
{
  ...campos comunes...,
  "recordCount": 12043,           // opcional, best-effort, timeout 2s
  "sql": "SELECT ... FROM ...",   // solo si includeSql:true y es tabla con origen SQL
  "sqlTruncated": false
}
```

#### `Query`
```json
{
  ...campos comunes...,
  "recordCount": 87,
  "sql": "SELECT ...",            // solo si includeSql:true
  "sqlTruncated": false
}
```

#### `Form` / `Report`
```json
{
  ...campos comunes...,
  "controls": [
    { "name": "txtNombre", "type": "TextBox" },
    { "name": "btnGuardar","type": "CommandButton" }
  ],
  "controlsTruncated": false
}
```
Sin propiedades individuales de cada control (evita explosión de tokens).

#### `Macro`
```json
{
  ...campos comunes...,
  "macroType": "Standalone"       // "Standalone" | "Embedded"
}
```

### Reglas de acotación

| Regla                   | Valor                               | Comportamiento si se excede                                                       |
|-------------------------|-------------------------------------|-----------------------------------------------------------------------------------|
| `fields`                | `maxFields` (default 200, máx 1000) | Devuelve los primeros `maxFields` + `"truncatedFields": true` + `fieldCount` real |
| SQL                     | 8 KB (8192 chars)                   | Trunca + `"sqlTruncated": true`                                                   |
| `controls`              | 200                                 | Trunca + `"controlsTruncated": true`                                              |
| `recordCount`           | timeout 2 s                         | Si falla o tarda, se **omite la propiedad** (no es error)                         |
| `description` por campo | 256 chars                           | Trunca + `"..."`                                                                  |

### Reglas de seguridad y filtrado
1. **Nunca** se abren objetos en vista diseño ni se ejecuta nada.
2. **Nunca** se devuelve contenido de datos de la tabla (solo metadata).
3. Se **omiten** objetos del sistema:
   - `MSys*`
   - `~*` (temporales de Access)
   - `MSysNavPane*`, `MSysObjects`, `MSysQueries`, etc.
4. Lectura exclusivamente vía late-bound:
   - `CurrentDb.TableDefs` / `QueryDefs`
   - `CurrentProject.AllForms` / `AllReports` / `AllMacros`
5. Cualquier error de permiso → `-32000` con mensaje original preservado.
6. Objeto inexistente → `-32000` con `"Objeto 'X' no encontrado"`.
7. `type` inválido → `-32602` con lista de tipos válidos.

### Contrato de `list_objects` (hermano)
Para que el modelo elija sin cargar todo:

```json
{
  "tables":  [ { "name": "Clientes", "fieldCount": 14 }, ... ],
  "queries": [ { "name": "qClientesActivos", "fieldCount": 8 }, ... ],
  "forms":   [ { "name": "frmClientes" }, ... ],
  "reports": [ { "name": "rptClientes" }, ... ],
  "macros":  [ { "name": "mcrInicio" }, ... ]
}
```

- Máximo 500 objetos por categoría. Si se excede → `"truncated": true` en
  la categoría correspondiente.
- Sin SQL, sin campos, sin controles.

### Contrato de `project_summary`
```json
{
  "name": "MiBase.accdb",
  "projectKey": "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
  "ephemeral": false,
  "counts": { "tables": 12, "queries": 30, "forms": 8, "reports": 5, "macros": 3 },
  "moduleCount": 22,
  "accessVersion": "16.0",
  "bitness": "x86"
}
```

## Alternativas descartadas

| Alternativa                                        | Motivo de descarte                                      |
|----------------------------------------------------|---------------------------------------------------------|
| Devolver siempre el SQL completo                   | Revienta el token budget con queries grandes.           |
| Devolver sample de filas                           | Viola "read-only de metadata"; riesgo de fuga de datos. |
| Contar registros siempre                           | `COUNT(*)` en tablas grandes bloquea el hilo UI.        |
| Sin límite de campos                               | Tablas con 300+ campos inundan el contexto.             |
| Devolver propiedades de cada control de formulario | Explosión combinatoria (100+ props por control).        |
| Objeto sin `type` explícito                        | Ambigüedad entre `Table` y `Query` con mismo nombre.    |

## Consecuencias

### Positivas
- Token budget predecible: máx ~10 KB por `object_detail` típico.
- El modelo pide SQL explícitamente cuando lo necesita (`includeSql:true`).
- Coste de `COUNT(*)` acotado (2 s máx, best-effort).
- Filtrado de objetos de sistema evita ruido y errores.

### Negativas / mitigaciones
- **`recordCount` puede faltar** en tablas grandes.
  - Mitigación: documentado como opcional; el modelo no debe asumirlo.
- **`maxFields` default 200** puede truncar tablas muy anchas.
  - Mitigación: el modelo puede reintentar con `maxFields:1000`.
- **`sqlTruncated` puede cortar la query a medias** (sintaxis inválida
  para el modelo).
  - Mitigación: se incluye `sqlLength` real para que el modelo sepa.
- **Formularios con subformularios**: solo se listan controles de primer
  nivel. Subformularios no se recorren.
  - Mitigación: el modelo puede pedir `object_detail` del subformulario
    por separado.

## Implementación

`BlsAccessObjectService` (late-bound, hilo UI):

```csharp
public ObjectDetailResult GetObjectDetail(
    string name, string type, bool includeSql, int maxFields)
{
    if (maxFields <= 0 || maxFields > 1000)
        throw new BlsInvalidParamsException("maxFields fuera de rango [1,1000]");

    if (IsSystemObject(name))
        throw new BlsInvalidParamsException($"Objeto de sistema no accesible: {name}");

    return type switch
    {
        "Table"   => GetTableDetail(name, includeSql, maxFields),
        "Query"   => GetQueryDetail(name, includeSql, maxFields),
        "Form"    => GetFormDetail(name, maxFields),
        "Report"  => GetReportDetail(name, maxFields),
        "Macro"   => GetMacroDetail(name),
        _         => throw new BlsInvalidParamsException(
                        $"type inválido: {type}. Válidos: Table,Query,Form,Report,Macro")
    };
}
```

`IsSystemObject(name)` devuelve `true` si empieza por `MSys` o `~`.

## Tests obligatorios
- `ObjectDetail_Tabla_DevuelveCamposSinSql`
- `ObjectDetail_QueryConIncludeSql_DevuelveSql`
- `ObjectDetail_SqlExcede8KB_TruncaYMarca`
- `ObjectDetail_MasDeMaxFields_MarcaTruncated`
- `ObjectDetail_ObjetoMSys_EsOmitido`
- `ObjectDetail_ObjetoTemporal_EsOmitido`
- `ObjectDetail_TipoInvalido_LanzaInvalidParams`
- `ObjectDetail_ObjetoInexistente_ErrorConMensaje`
- `ObjectDetail_RecordCountTimeout_OmitePropiedad`
- `ListObjects_MasDe500Objetos_MarcaTruncated`
- `ProjectSummary_DevuelveCountsYProjectKey`

## Referencias
- PRD §3.2 (bridge), §7 FR-11 (read-only Access).
- `.opencode/skills/bls-vbe-automation/SKILL.md` (identidad de proyecto).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (códigos de error).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (tests).
- ADR-001 (projectKey en `project_summary`).
