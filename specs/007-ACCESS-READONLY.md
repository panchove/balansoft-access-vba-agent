# 007 — Integración Access de solo lectura

**Estado:** `borrador`
**PRD:** FR-11
**ADRs:** ADR-004 (contrato de `object_detail`)
**Spec de diseño:** `docs/design/ADR-006-VBE-BRIDGE.md` (sección Access)
**Tests:** 3 en `JsonRpcDispatcherTests` (solo contra `FakeAccess`)

---

## Contexto

Tres operaciones de **solo lectura** sobre el modelo de objetos de Access:
resumen del proyecto, listado de objetos y detalle de un objeto. Existen para
que el modelo entienda el esquema de la base sin depender de adivinar.

`IBlsAccess` está declarada con sus tres métodos y sus tipos de retorno
(`ProjectSummaryResult`, `ListObjectsResult`, `ObjectDetailResult`,
`ObjectCounts`, `ObjectListItem`, `ControlInfo`, `FieldInfo`). Solo existe
`FakeAccess` para tests. **No hay implementación real.**

La interfaz del dispatcher ya despacha estos métodos, así que el trabajo es
implementar `IBlsAccess` contra `Application` de Access.

---

## Alcance

**Entra:**

- `project_summary` — nombre del proyecto y recuentos por tipo.
- `list_objects` — tablas, consultas, formularios, informes, macros.
- `object_detail` — detalle de un objeto, con campos y SQL opcional.

**No entra:**

- Cualquier escritura. Estos tres métodos son de solo lectura por diseño y
  esa restricción es deliberada: reducir la superficie de escritura que el
  modelo puede tocar.
- Ejecución de SQL.

---

## Requisitos

### R001 — Solo lectura

Ningún método escribe en la base. Ningún método ejecuta SQL. Es una decisión
de seguridad, no una limitación pendiente.

### R002 — Filtro de objetos internos

Se excluyen los objetos del sistema:

- `MSys*` (tablas del sistema).
- Objetos temporalizados `~*` (copias de diseño).

Aparecen en el explorador de Access pero no son parte del modelo de negocio y
confunden al modelo.

### R003 — `project_summary`

Devuelve el nombre del proyecto y los recuentos por categoría. Es la primera
llamada de cada turno, para que el modelo sepa con qué trabaja.

### R004 — `list_objects`

Devuelve los objetos agrupados por categoría, con nombre y tipo.

### R005 — `object_detail` (ADR-004)

- **Campos siempre.** Es el dato que el modelo necesita para escribir código
  VBA correcto, así que se devuelve sin condiciones.
- **SQL solo bajo demanda** (`includeSql`). El SQL de una consulta puede ser
  largo y casi siempre es irrelevante para el modelo.
- **Truncado de SQL a 8 KB**, marcando el truncamiento en la respuesta para
  que el modelo sepa que no vio el SQL completo.

### R006 — Límite de campos

`maxFields` acota el número de campos devueltos. Sin límite, una tabla con
300 columnas produce un frame que puede acercarse al máximo de 1 MB del pipe
(ADR-002) y hacer fallar el request entero.

### R007 — Errores reales

Un objeto inexistente lanza y devuelve error con categoría. Nunca una lista
vacía que el modelo interpreta como "la base no tiene tablas".

---

## Criterios de aceptación

```
Dado un proyecto Access con tablas, consultas, formularios, informes y macros
Cuando se llama a project_summary
Entonces los recuentos por categoría son correctos

Dado un proyecto con tablas MSys y objetos ~*
Cuando se llama a list_objects
Entonces ninguno de ellos aparece

Dado una consulta con SQL de 20 KB
Cuando se llama a object_detail con includeSql
Entonces el SQL viene truncado a 8 KB
Y la respuesta indica que está truncado

Dado una tabla con 300 campos
Cuando se llama a object_detail sin maxFields
Entonces el número de campos devueltos está acotado
Y la respuesta no excede el máximo de frame del pipe

Dado un nombre de objeto que no existe
Cuando se llama a object_detail
Entonces devuelve error con categoría
Y no devuelve un detalle vacío
```

---

## Tests obligatorios

Ya implementados (contra `FakeAccess`):

| Test | Cubre |
|---|---|
| `Bridge_ProjectSummary_DevuelveCounts` | R003 |
| `Bridge_ListObjects_DevuelveCategorias` | R004 |
| `Bridge_ObjectDetail_DevuelveCampos` | R005 |

**Pendientes** (requieren la implementación real o un doble más fiel):

| Test | Cubre |
|---|---|
| `Access_ExcluyeObjetosMSysYTemporalizados` | R002 |
| `Access_SqlTruncadoAMax8Kb_MarcaTruncamiento` | R005 |
| `Access_RespetivaMaxFields` | R006 |
| `Access_ObjetoInexistente_LanzaErrorTipado` | R007 |
| `ObjectDetail_Tabla_DevuelveCamposSinSql` | R005, ADR-004 |
| `ObjectDetail_SqlExcede8KB_TruncaYMarca` | R005, ADR-004 |

Los dos últimos los busca la CI por nombre y hoy avisan de que faltan.

---

## Desviaciones y riesgos

- **Sin implementación real.** `FakeAccess` devuelve objetos fijos; no prueba
  nada sobre el modelo de objetos de Access.
- **Enlace tardío contra `Application`.** Es la parte delicada:
  `CurrentProject`, `CurrentData`, `AllTables`, `TableDefs` y `Fields` no
  existen como tipos compilados. Toda esta spec depende de que `IBlsAccess`
  se implemente con `dynamic` en `Helpers/BlsComHelper.cs` (P4), que no
  existe todavía.
- **Los límites (8 KB SQL, `maxFields`) están en la interfaz pero sin
  verificar.** Son los que evitan que un objeto patológico rompa el pipe.
- **ADR-004 requiere `object_detail` con campos siempre y SQL opt-in.** El doc
  de diseño y la firma de `IBlsAccess.ObjectDetail` coinciden; lo que falta
  es el cuerpo.