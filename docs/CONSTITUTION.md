---
version: 1.1.0
date: 2026-10-15
status: approved
iso_standard: [12207, 25010, 29148, 42010]
aligns_to: [AGENTS.md, PRD.md, ADR-001..005]
tags: [governance, constitution, sdd, iso-standards, quality-gates]
---

# Constitution — Balansoft Access VBA Agent (NIM Direct)

> Documento rector del Spec-Driven Development (SDD) de este repositorio.
> Deriva de `AGENTS.md`, de los ADR vigentes en `docs/design/` y de las puertas
> de CI en `.github/workflows/ci.yml`.
> Los contratos por feature están en `specs/`, indexados en `specs/README.md`.
>
> **Estado:** v1.1 · Aceptado · 2026-10-15

---

## Preámbulo

Este repositorio desarrolla un COM Add-in para Microsoft Access 2010+ (x86)
que integra Nvidia NIM dentro del VBE. El código se apoya en .NET Framework
4.8 y conversa con los objetos COM de Office mediante enlace tardío, dentro
del proceso de Access y sin dependencias de runtime externas.

El SDD de este proyecto **no es decorativo**. El add-in se ejecuta dentro de un
proceso de usuario que no se puede reiniciar sin perder su trabajo, y varias de
sus reglas (x86, sin PIAs, DPAPI, un CTS por turno) existen porque
incumplirlas produce fallos en producción que los tests no reproducen: un
Access x64 cargado donde se esperaba x86, claves que se filtran a disco, o
turnos de modelo que se cuelgan y dejan la terminal colgada.

Por eso esta constitución es corta, verificable y tiene penalidades
concretas. Todo principio de aquí es comprobable por máquina o por revisión
humana, y ninguno depende de la buena voluntad del autor.

---

## I. Principios

### P1 — El ADRs ganan

Los ADR en `docs/design/ADR-*.md` son la fuente de verdad cuando un spec, un
skill o el código discrepan. Si un cambio de comportamiento invalida un ADR, el
ADR se actualiza **en el mismo PR** que el cambio.

> today: ADR-001..004 todos Aceptados.

### P2 — Contrato primero, implementación después

Ninguna feature entra sin spec en `specs/` que declare alcance, criterios de
aceptación verificables y tests obligatorios. El spec se revisa antes de
escribir código. El índice y el formato están en `specs/README.md`.

Un spec describe **el comportamiento real**, no el deseado: cuando el código y
el doc de diseño discrepan, el spec documenta lo que hace el código y anota la
discrepancia. `implementado` solo es válido con tests verdes.

### P3 — x86 es una restricción dura

`PlatformTarget=x86` y `Prefer32Bit=true` en los dos `.csproj`. Nada de AnyCPU
ni x64. `Pack.ps1` empaqueta la variante x86. Access x86 no puede cargar un
assembly x64 y el fallo en carga es fatal.

El `AssemblyName` es `Balansoft.AIAddin.Access`, sin sufijo de plataforma: la
garantía de x86 la da `PlatformTarget`, no el nombre.

### P4 — Sin PIAs, sin ROT

Prohibido `using Microsoft.Office.Interop.*`, `Marshal.GetActiveObject`,
`Marshal.ReleaseComObject` y cualquier acceso al Running Object Table. El
enlace es tardío vía `dynamic`, y `dynamic` vive **únicamente** en
`Helpers/BlsComHelper.cs`.

*Justificación:* las PIAs atan el binario a una versión concreta de Office y
los RCW no reutilizables filtran handles COM en sesiones largas de Access.

### P5 — Todo acceso a Office pasa por `IUiScheduler`

El VBE y Access no son thread-safe. Ninguna operación que toque `VBE`,
`VBProject`, `VBComponent` o el modelo de objetos de Access se ejecuta desde
un hilo de fondo. Los handlers del bridge se despachan al hilo de UI.

### P6 — Abort por turno (ADR-003)

Un `CancellationTokenSource` por turno de usuario, propiedad del Form. La
cancelación de usuario **nunca** se reporta como error: emite `OnCancelled`,
no `OnError`. Ningún reintento, espera ni lectura de stream ignora el token.

### P7 — La API key solo existe cifrada

DPAPI `DataProtectionScope.CurrentUser`. Prohibido en logs, excepciones,
JSON de configuración, registry y cualquier archivo plano. El `.env` local
existe solo para tests de integración y está en `.gitignore`.

### P8 — Errores reales, nunca éxito falso

Un fallo de COM o de NIM se propaga como excepción con **categoría + mensaje
original**. Prohibido `catch (Exception) { }`, `catch { return true; }` y
cualquier handler del bridge que responda éxito cuando la operación falló.
La cancelación de usuario es el único caso que no es error.

### P9 — El protocolo está escrito, no improvisado

Framing `[uint32 LE][UTF-8 JSON]` (ADR-002), máximo 1 MB por frame. Los 10
métodos JSON-RPC viven en la whitelist de `JsonRpcMethods.All`. Cualquier
método fuera de la lista responde `-32601`.

### P10 — Los tests prueban comportamiento, no implementación

Tests sin Office por defecto. Nada de `Thread.Sleep`: se usan `Task.Delay`
con timeout explícito. Sin `.Result` ni `.Wait()` sobre tasks. Cada ADR
tiene tests obligatorios con nombre fijo, verificados por nombre.

### P11 — Cambios acotados

Sin refactor oportunista en un PR de feature. Si algo está mal pero no es lo
que el PR toca, se documenta como deuda y se deja para otro cambio.

### P12 — Español en UI y logs

Textos visibles al usuario y mensajes de log en español. Los identificadores
siguen convención de C# en inglés. El inglés está pendiente (PRD §4).

### P13 — Diseño por Contrato (Design by Contract - DbC)

Todo servicio, componente y método público debe validar formalmente su contrato:
- **Precondiciones (`Requires`):** Validación estricta a la entrada de métodos y constructores mediante `BlsContract.Requires*`. Si una precondición falla, la responsabilidad es del llamador (`BlsPreconditionException`, `ArgumentNullException`, `ArgumentException`).
- **Postcondiciones (`Ensures`):** Garantía del valor de retorno o del estado terminal tras la ejecución mediante `BlsContract.Ensures*`. Si una postcondición falla, la responsabilidad es de la implementación (`BlsPostconditionException`).
- **Invariantes (`Invariant`):** Condiciones permanentes del estado del componente que se mantienen verdaderas en todo momento público mediante `BlsContract.Invariant`.

---

## II. Restricciones técnicas

| Ámbito        | Valor                     | Motivo                                           |
|---------------|---------------------------|--------------------------------------------------|
| Target        | `net48`                   | Access 2010+ sobre .NET Framework                |
| Lenguaje      | C# 10 (`LangVersion`)     | Sin raw strings ni features de C# 11             |
| Nullable      | `disable`                 | El código usa `dynamic`; nullable(ref) no aporta |
| Platform      | x86 únicamente            | P3                                               |
| Serialización | `System.Text.Json` 8.0.5  | Sin Newtonsoft; más apto para AOT                |
| HTTP          | `System.Net.Http`         | Sin `System.Net.Http.Json` en net48              |
| Testing       | MSTest + `VSTest.Console` | Sin infrastructure externa                       |

### APIs prohibidas por no existir en `net48`

`Task.WaitAsync` · raw strings de C# 11 · `System.Net.Http.Json` ·
`File.ReadAllTextAsync` · `Environment.ProcessPath` · claves `required`.

Para waits con timeout existe el helper `tests/.../Infrastructure/TaskTestExtensions.cs`.

---

## III. Gates

### Gate automático (CI)

`.github/workflows/ci.yml` **falla el build** si:

1. El build `Debug x86` o `Release x86` de los `.csproj` no sale limpio con
   `TreatWarningsAsErrors`.
2. Los tests (sin integración NIM) no están verdes: 99/99.
3. Falta cualquiera de los 39 documentos obligatorios.
4. Aparece `Microsoft.Office.Interop` en `src/`.
5. Aparece `dynamic` fuera de `Helpers/BlsComHelper.cs`.
6. Aparece `Thread.Sleep` en `tests/`.
7. Desaparece un test de ADR ya implementado (los de ADR-001 y ADR-004
   pendientes solo avisan).
8. Hay una referencia cruzada rota en `docs/`, `.opencode/skills/`, `specs/`,
   `AGENTS.md` o `MEMORY.md`.

En `push` a `main` además ejecuta `scripts/Pack.ps1` y sube el ZIP como
artefacto.

> **Estado: operativo.** La CI restauraba un `Balansoft.AIAddin.sln` que no
> existe, así que el job moría en el restore y ninguno de los gates llegaba a
> comprobarse. Ahora compila los `.csproj` (la solución `.slnx` no propaga
> `Platform` a proyectos SDK-style) con `dotnet build`, y los tests con
> `dotnet vstest` sobre `.runsettings`. Los ocho gates se ejecutan; el comando
> de test es el mismo que se ha verificado en local.

### Gate de spec

- El PR referencia un spec en `specs/`.
- Los criterios de aceptación del spec tienen tests asociados **verdes**.
- Si el spec cambia, se actualiza en el mismo PR.
- `specs/` entra en el gate de referencias cruzadas, así que un enlace roto en
  un spec lo detecta la CI. El resto del gate de spec es **manual**: que los
  criterios tengan test verde depende de la revisión, no de un paso automático.

### Gate humano (PR checklist)

`.github/PULL_REQUEST_TEMPLATE.md` contiene el checklist de rechazo. Cualquier
ítem en negativo es rechazo automático salvo justificación explícita en
"Notas para el revisor".

Puntos que **solo** puede verificar una persona y por eso son bloqueantes:

- El marshaling al hilo de UI es correcto en cada camino.
- La API key no aparece en ningún log ni excepción.
- Los errores se propagan, no se silencian.
- El cambio está dentro del alcance del spec.

---

## IV. Flujo de trabajo

```
1. Spec (specs/NNN-*.md) con alcance + criterios de aceptación + tests
2. Revisión del spec            ← bls-architect
3. ADR si la decisión es estructural y no está cubierta
4. Implementación + tests       ← bls-implementer
5. Verificación: build Debug + Release x86 + suite completa
6. PR con checklist de rechazo y referencias cruzadas válidas
7. Revisión y auditoría         ← bls-reviewer + bls-iso
8. Merge
```

Un cambio sin spec pasa si es `fix` de un defecto ya cubierto por un spec
existente, o si es `docs`/`chore` sin impacto funcional. En cualquier otro
caso, el spec va primero.

---

## V. Penalidades

| Incumplimiento                        | Consecuencia                     |
|---------------------------------------|----------------------------------|
| PIA de Office o ROT                   | CI falla; rechazo                |
| `dynamic` fuera de `BlsComHelper`     | CI avisa; rechazo en revisión    |
| x86 → AnyCPU/x64                      | CI falla; rechazo                |
| API key en claro                      | Rechazo inmediato, sin discusión |
| `catch (Exception) { }` o éxito falso | Rechazo inmediato                |
| `Thread.Sleep` en tests               | CI falla                         |
| Método JSON-RPC fuera de la whitelist | Rechazo                          |
| Sin spec para una feature             | Rechazo                          |
| `TreatWarningsAsErrors` desactivado   | Rechazo                          |
| Nueva dependencia NuGet sin ADR       | Rechazo                          |

---

## VI. Diagnóstico del estado del repositorio

Snapshot a 2026-10-02, verificado contra el árbol de ficheros y la suite de
tests. Registrado aquí porque la constitución describe el destino y este es el
punto de partida real.

### Lo que existe y funciona

- Bridge JSON-RPC completo (10 métodos, framing ADR-002, `busy`, cancelación,
  timeout de inactividad) con **46 tests verdes**.
- Cliente NIM con streaming SSE, reintentos 1s/2s/4s, `Retry-After` capado
  a 30 s, timeout de chunk y tool calls por `index`: **53 tests verdes**.
- Terminal WinForms con loop de herramientas funcional, sin tests.
- `BlsSecureStore` (DPAPI) e `IBlsChatHistory`, sin tests.
- **99 tests verdes en total**, repartidos en 7 ficheros. Build limpio en
  Debug x86.

### Lo que falta

- **Punto de entrada COM.** No existe la clase que implemente
  `IDTExtensibility2`, ni `OnStartupComplete`, `OnDisconnection` ni
  `OnBeginShutdown`. `ComVisible=true` está en el csproj pero no hay nada que
  registrar. **El add-in no carga hoy.** Esto es lo prioritario, y es el spec
  `001-addin-lifecycle`.
- **Implementaciones reales de `IBlsVbe` e `IBlsAccess`.** Solo existen las
  interfaces y los dobles de test (`MockVbe`, `FakeAccess`). El bridge no
  tiene a quién hablar.
- **`BlsComHelper`.** La regla P4 concentra el `dynamic` en un archivo que no
  existe todavía.
- **`BlsLogger` y `BlsVersion`.** `IBlsLogger` solo tiene implementación de
  test. No hay rotación de logs ni versión+commit, así que ningún fallo en
  producción es diagnosticable. `BlsPaths.LogsDir` está definido y sin usar.
- **Menú Tools → "NIM Terminal".** Sin gestor de menú del VBE no hay forma de
  abrir la terminal.
- **`BlsConstants` no existe**, pero `.env.example` lo referencia dos veces.

### Cerrado en v1.1

Estos puntos del diagnóstico inicial ya no son deuda; se resuelven con
`Directory.Build.props`, los scripts y la CI:

- **`Directory.Build.props`** creado (versión `0.1.0`, `TreatWarningsAsErrors`,
  `PlatformTarget=x86`, `Prefer32Bit`, `LangVersion=10`, `Deterministic`).
  `version.txt` sale con `0.1.0` y no con `0.0.0`.
- **`TreatWarningsAsErrors=true`** en `Directory.Build.props`: build limpio con
  0 warnings en Debug y Release x86. La regla 7 ya bloquea de verdad.
- **`Nullable=annotations`** en ambos `.csproj`. Con `disable` el compilador
  emitía 88 avisos CS8632 (`?` en tipos de referencia sin anotación): el código
  fuente **sí** usa anotaciones nullable.
- **Scripts de registro y empaquetado**: `Pack.ps1` compila el proyecto de tests
  con `dotnet build -p:Platform=x86` y lee de `bin\x86\<Config>\net48\`;
  `RegisterAddin.ps1` / `UnregisterAddin.ps1` apuntan a la misma ruta.
  `Pack.ps1` ejecutado de principio a fin produce el ZIP con las 4 entradas
  esperadas.
- **`dev.ps1`** reescrito sobre `dotnet build` / `dotnet vstest` (antes usaba
  un `.sln` inexistente y `dotnet test` sobre la solución).
- **CI arreglada**: compila los `.csproj` en Debug y Release x86, corre
  `dotnet vstest` con `.runsettings` sobre net48 x86 (99/99 verificado en local
  con el mismo comando), publica el `.trx` de `TestResults/` y ejecuta los
  gates de documentos, PIAs, `dynamic`, `Thread.Sleep`, tests de ADR y
  referencias cruzadas. Ya no restaura un `.sln` inexistente.
- **`Balansoft.AIAddin.slnx`** es la solución real y **no se compila**:
  no propaga `Platform` a los proyectos SDK-style y escribe en
  `bin\<Config>\net48\`, ruta que no usan los scripts. Compilando los `.csproj`
  la salida es `bin\x86\<Config>\net48\`, que es lo que verifican los scripts.
- **`msbuild` de Visual Studio no sirve** en un equipo con VS/BuildTools sin el
  workload de .NET: falla con MSB4236 al no resolver `Microsoft.NET.Sdk`. El
  comando canónico es `dotnet build`.
- **Referencias cruzadas**: los ficheros apuntaban al skill bls-workflow-nemotron con otro nombre
  y `AGENTS.md` a un nombre de skills que no existe. Corregidos; el gate de
  referencias pasa.

### Deuda conocida

- `docs/design/20250103-nim-terminal.md:285-334` describe el `OnToolCall` /
  `OnDone` antiguos, que provocaban una carrera entre el loop de herramientas
  y el cierre del turno. El código ya no los sigue.
- `HistoryMessage` soporta `ToolCalls` y `NimMessageSanitizer` sanea las secuencias
  de mensajes hacia NIM garantizando correspondencia estricta de `tool_call_id` y
  `tool_calls` para evitar HTTP 400.
- La CI espera 7 tests de ADR por nombre que no existen, y dos de los que sí
  existen tienen nombres distintos a los que busca (`Turno_*`).
- `Frame_Vacio_RespondeInvalidRequest` y `Frame_JsonInvalido_RespondeInvalidRequest`
  están duplicados en dos clases de test.
- `scripts/Scaffold.ps1` no parsea (4 errores, líneas 762-775). Es un generador
  de repositorio nuevo, no forma parte del build ni de la CI.
- `scripts/CopyDocs.ps1` exige aún los nombres antiguos de los skills en su
  tabla de copia.
- `src/Balansoft.AIAddin.Access/Class1.cs` es residuo del scaffold.
- `balansoft-aiaddin-access/` en la raíz es un repositorio git vacío (un único
  commit del árbol vacío), no una carpeta de trabajo.

### Estado de los specs

`specs/README.md` es el índice. Resumen: 003 `implementado`; 002, 004, 005 y
006 `aceptado` con implementación parcial o sin tests; 001, 007, 008 y 009
`borrador`. Ningún spec está `obsoleto`.

---

## VII. Enmiendas

Esta constitución cambia solo por decisión explícita, registrada aquí con
fecha, motivo y responsable.

| Versión | Fecha      | Cambio                                                                                                                                                                                                                 | Motivo                                                                                                                 |
|---------|------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------|
| v1.0    | 2026-10-02 | Versión inicial derivada de `AGENTS.md`, ADR-001..004 y `ci.yml`                                                                                                                                                       | Punto de partida del SDD                                                                                               |
| v1.1    | 2026-10-02 | Índice de `specs/` como parte del documento; P2 y P3 alineados con el código real; el gate de CI marcado como no ejecutado; estado del árbol verificado (`Directory.Build.props` y `TreatWarningsAsErrors` no existen) | Los nueve specs obligaron a contrastar la constitución con el árbol y encontraron afirmaciones que el código desmiente |

Los hallazgos que surjan del propio SDD (este documento y los specs) se resuelven
corrigiendo el spec o la constitución, no marcándolos como "agendados".