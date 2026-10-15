# Specs — Balansoft Access VBA Agent

Cada spec es un contrato verificable de una funcionalidad. Un PR de feature
debe referenciar el spec que implementa y tener sus criterios de aceptación
cubiertos por tests verdes.

Un spec describe **el comportamiento real**, no el deseado. Cuando el código y
el doc de diseño discrepan, gana el código y la discrepancia se anota en
"Desviaciones". Cuando lo que falta es la implementación, el spec sigue
existiendo con estado `aceptado` o `borrador`.

## Formato

Un spec por archivo, nombrado `NNN-slug.md`, con esta estructura:

1. **Cabecera** — estado, PRD, ADRs, spec de diseño, número de tests
2. **Contexto** — qué resuelve y qué falta hoy
3. **Alcance** — qué entra y qué no
4. **Requisitos** — `R001`, `R002`… numerados y verificables
5. **Criterios de aceptación** — Given/When/Then
6. **Tests obligatorios** — nombres exactos; la CI los busca por nombre
7. **Desviaciones y riesgos** — lo que el código hace distinto o no hace

## Estados

| Estado | Significado |
|---|---|
| `borrador` | El spec se está escribiendo o no está aprobado. |
| `aceptado` | El spec es el contrato. La implementación está parcial. |
| `implementado` | El código cumple todos los criterios y hay tests verdes. |
| `obsoleto` | Reemplazado por otro spec. Se conserva por historial. |

## Índice

| ID | Spec | PRD | ADRs | Estado | Tests |
|---|---|---|---|---|---|
| 001 | [addin-lifecycle](001-addin-lifecycle.md) | FR-01, FR-05 | — | `borrador` | 0/5 |
| 002 | [vbe-bridge](002-vbe-bridge.md) | FR-03, FR-04 | ADR-001, ADR-002 | `aceptado` | 46 verdes |
| 003 | [nim-client](003-nim-client.md) | FR-09 | ADR-003 | `implementado` | 53 verdes |
| 004 | [nim-terminal](004-nim-terminal.md) | FR-02, FR-03 | ADR-003 | `aceptado` | 0/10 |
| 005 | [secure-store](005-secure-store.md) | FR-08 | — | `aceptado` | 0/6 |
| 006 | [chat-history](006-chat-history.md) | FR-10 | ADR-001 | `aceptado` | 0/7 |
| 007 | [access-readonly](007-access-readonly.md) | FR-11 | ADR-004 | `borrador` | 3 verdes, 6 pendientes |
| 008 | [logging-errors](008-logging-errors.md) | FR-07, FR-12 | — | `borrador` | 2 verdes, 7 pendientes |
| 009 | [registration](009-registration.md) | FR-06 | — | `borrador` | 0/8 |
| 010 | [design-by-contract](010-design-by-contract.md) | Calidad / DbC | ADR-005 | `implementado` | 17 verdes |

`implementado` solo si hay tests verdes. Los specs 005 y 006 tienen el código
completo pero ninguna aserción que lo valide, así que no pueden declararse
implementados.

Los 99 tests de la suite se reparten en siete ficheros:
`BlsNimClientTests` 28 · `JsonRpcDispatcherTests` 17 · `NimStreamParserTests` 14
· `BlsVbeBridgeServerTests` 12 · `NimHttpRequestBuilderTests` 11 ·
`BlsPipeFramingTests` 10 · `JsonRpcDeserializationTests` 7.

## Orden de implementación

El spec 001 es bloqueante: sin punto de entrada COM el add-in no carga, y todo
lo demás queda sin forma de probarse en Access real.

```
001 (lifecycle)  ──►  habilita probar en Access
  ├─ 008 logging         necesario para diagnosticar 001
  ├─ 009 registration    necesario para desplegar 001
  ├─ 002 vbe-bridge      falta la implementación real de IBlsVbe
  └─ 007 access-readonly  falta la implementación real de IBlsAccess
```

Los specs 003 a 006 tienen código. La terminal (004) es la única que puede
arrastrar a los otros dos, y por eso es el siguiente gap de calidad después
de 001: ~600 líneas de WinForms sin un solo test.

## Gaps que bloquean la CI

- **La CI está operativa** y ejecuta todos los gates:
  - Build Debug x86 y Release x86 con `dotnet build` (0 warnings, 0 errors).
  - Tests 99/99 con `dotnet vstest` + `.runsettings` + `/Platform:x86`.
  - 39 documentos obligatorios verificados (incluye `docs/PRD.md`,
    `docs/constitution.md`, `specs/` y los 4 ADRs).
  - Sin PIAs de Office en `src/`.
  - `dynamic` solo en `Helpers/BlsComHelper.cs`.
  - Sin `Thread.Sleep` en `tests/`.
  - Tests de ADR implementados presentes (5 blocking + 5 warning).
  - Referencias cruzadas en `docs/`, `.opencode/skills/`, `specs/`,
    `AGENTS.md`, `MEMORY.md` — **cero rotas**.
- **`Directory.Build.props` existe** con versión `0.1.0`,
  `TreatWarningsAsErrors=true`, `PlatformTarget=x86`, `Prefer32Bit=true`,
  `LangVersion=10`, `Deterministic=true`.
- **Scripts arreglados**: `Pack.ps1`, `RegisterAddin.ps1`,
  `UnregisterAddin.ps1`, `dev.ps1` usan `dotnet build` / `dotnet vstest` y
  rutas `bin\x86\<Config>\net48\`. `Pack.ps1` probado end-to-end produce ZIP
  válido.
- **`msbuild` de VS no sirve** sin workload de .NET (MSB4236). El comando
  canónico es `dotnet build`.
- **`Scaffold.ps1` tiene 4 errores de parser** (líneas 762-775) y genera
  comandos obsoletos. No se usa en build ni CI; es generador de repo nuevo.

## Trazabilidad con el PRD

| PRD | Cubierto por |
|---|---|
| FR-01, FR-05 | 001 |
| FR-02, FR-03 | 004 (y 002 para el transporte) |
| FR-04 | 002 |
| FR-06 | 009 |
| FR-07, FR-12 | 008 |
| FR-08 | 005 |
| FR-09 | 003 |
| FR-10 | 006 |
| FR-11 | 007 |

FR-01 a FR-12 están cubiertos por al menos un spec. Ningún spec queda fuera
del PRD.

## Trazabilidad con los ADRs

| ADR | Decisión | Reflejado en |
|---|---|---|
| ADR-001 | Identidad de proyecto: SHA256 de la ruta normalizada | 002 (R007), 006 (R001) |
| ADR-002 | Framing: 4 bytes LE + JSON UTF-8, máx. 1 MB | 002 (R001), 007 (R006) |
| ADR-003 | Un `CancellationTokenSource` por turno | 003 (R005), 004 (R004) |
| ADR-004 | `object_detail`: campos siempre, SQL opt-in | 007 (R005) |

Los cuatro ADRs aceptados están reflejados. Ningún spec contradice un ADR.