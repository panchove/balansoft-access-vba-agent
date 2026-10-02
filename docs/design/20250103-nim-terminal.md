# Feature: NIM Terminal (WinForms + integración VBE)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-03

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

## Depende de
- `docs/design/20250101-vbe-bridge.md` (bridge JSON-RPC).
- `docs/design/20250102-nim-client.md` (cliente NIM).
- `docs/design/ADR-001-project-identity.md` (identidad de proyecto).
- `docs/design/ADR-003-abort-model.md` (abort).

---

## 1. Contexto

`BlsNimTerminal` es la única UI del add-in. Es un `Form` WinForms que:

1. Se abre desde Tools → "NIM Terminal" en el VBE.
2. Muestra la conversación con NIM (streaming en vivo).
3. Envía prompts a NIM vía `BlsNimClient`.
4. Recibe tool calls y las reenvía al bridge (`BlsVbeBridgeServer`) para
   que se ejecuten en el VBE/Access.
5. Persiste la conversación con `BlsChatHistory`.
6. Gestiona la API key con `BlsSecureStore` si falta.
7. Permite cancelar el turno actual (ADR-003).

Es el pegamento del MVP: sin ella, el bridge y el cliente NIM no tienen
quien los orqueste.

Cubre:
- PRD §3.4 (terminal WinForms).
- PRD §7 FR-02 (terminal, streaming, tool calls, abort).
- PRD §7 FR-03 (contexto de módulo activo y selección).
- PRD §7 FR-05 (menú Tools → "NIM Terminal").
- PRD §11 criterios 2, 3, 5, 6.

Y materializa:
- ADR-001 (identidad de proyecto para historial).
- ADR-003 (abort por turno).

---

## 2. Alcance

### Incluido
- `Form` WinForms con ListView de conversación + TextBox de input +
  botón Detener + label de estado.
- Apertura desde el menú Tools del VBE (owner = HWND del VBE).
- Streaming en vivo: cada `OnTextChunk` se refleja en la UI.
- Tool calls: `OnToolCall` → bridge → resultado → nuevo turno NIM.
- Contexto automático: módulo activo + selección se inyectan en el
  system prompt.
- Cancelación con botón Detener / tecla Escape (ADR-003).
- Historial: carga al abrir, guarda con debounce (skill 05).
- Prompt de API key si no existe (skill 05).
- Una sola instancia por proceso (invariante).
- Todos los textos en español.

### Excluido (fuera de MVP)
- Múltiples sesiones/tabs.
- Selección de modelo/proveedor en UI.
- Panel de configuración.
- Exportar conversación.
- Búsqueda en historial.
- Autocompletado en el input.
- Adjuntar archivos.
- Imágenes / audio.
- Tema oscuro.
- Atajos configurables.
- Menú contextual en el ListView.

---

## 3. Interfaces nuevas

### 3.1 `BlsNimTerminal` (UI/)
El `Form` principal.

```csharp
public sealed class BlsNimTerminal : Form
{
    public BlsNimTerminal(
        IBlsNimClient nimClient,
        IBlsBridgeClient bridgeClient,
        IBlsChatHistory history,
        IBlsSecureStore secureStore,
        IBlsVbe vbe,
        IBlsLogger logger);

    // Abre la terminal (o foco a la existente)
    public static BlsNimTerminal ShowOrFocus(
        IWin32Window owner,
        IServiceProvider services);

    // Cancelación externa (por ejemplo desde OnDisconnection)
    public void RequestCancelAndClose();
}
```

**Notas:**
- Constructor recibe todas las dependencias por interfaz.
- `ShowOrFocus` gestiona la invariante "una sola terminal".
- `RequestCancelAndClose` usado por el shutdown del add-in.

### 3.2 `IBlsBridgeClient` (Core/ o UI/)
Cliente del bridge (del lado del consumidor). El bridge server lo
implementa `BlsVbeBridgeServer`; el cliente es un wrapper que abre el
Named Pipe y envía frames JSON-RPC.

```csharp
public interface IBlsBridgeClient : IDisposable
{
    Task<TResult> InvokeAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken ct);
}
```

**Notas:**
- Serializa el request, envía frame, espera response, deserializa.
- Respeta el `CancellationToken` (ADR-003).
- Mapea `-32000..-32004` a excepciones tipadas.
- Timeout de tool: 30 s (ADR-003).
- Un `BlsBridgeClient` por terminal. Se conecta al pipe al abrir la
  terminal, se desconecta al cerrar.

### 3.3 `BlsBridgeClient` (Core/)
Implementación.

```csharp
public sealed class BlsBridgeClient : IBlsBridgeClient
{
    public BlsBridgeClient(
        string pipeName,
        IBlsLogger logger,
        TimeSpan? toolTimeout = null);   // default 30s

    public Task<TResult> InvokeAsync<TResult>(
        string method, object? parameters, CancellationToken ct);

    public void Dispose();
}
```

### 3.4 `IVbeContextProvider` (UI/, interno)
Abstrae la obtención de "contexto VBE" que se inyecta en el prompt.

```csharp
internal interface IVbeContextProvider
{
    Task<VbeContext> GetAsync(CancellationToken ct);
}

internal sealed record VbeContext(
    string? ActiveModuleName,
    string? ActiveModuleType,
    string? SelectionText,
    bool SelectionTruncated);
```

**Notas:**
- Vive en `UI/`, no en `VbeIntegration/`, porque es específico de cómo
  la terminal construye el prompt.
- Usa `IBlsVbe` para leer módulo activo y selección.
- `SelectionText` se trunca a 32 KB (skill 02).

### 3.5 `ISystemPromptBuilder` (UI/, interno)
Construye el system prompt con contexto VBE + reglas del skill 08.

```csharp
internal interface ISystemPromptBuilder
{
    string Build(VbeContext context, IReadOnlyList<string> availableToolNames);
}
```

**Notas:**
- Incluye la regla de oro: "Si una tool falla, propaga el error. No
  inventes éxito" (skill 08).
- Incluye la lista de tools disponibles.
- Incluye el contexto VBE activo (módulo + selección).

### 3.6 `IUserInputDialog` (UI/, interno)
Abstrae el diálogo de API key para poder testear sin UI.

```csharp
internal interface IUserInputDialog
{
    string? PromptForApiKey(IWin32Window owner);
}
```

**Notas:**
- En producción, muestra un `Form` modal con `PasswordChar='●'`.
- En tests, devuelve una key fija o `null`.

---

## 4. Invariantes afectados

| Fuente             | Invariante               | Cómo se cumple                                        |
|--------------------|--------------------------|-------------------------------------------------------|
| AGENTS.md regla 3  | Marshaling al hilo UI    | Toda actualización de controles vía `OnUi` (skill 06) |
| AGENTS.md regla 4  | API key DPAPI            | Prompt → `BlsSecureStore.SaveApiKey`                  |
| AGENTS.md regla 5  | Errores reales           | `OnError` muestra mensaje; nunca éxito falso          |
| AGENTS.md regla 10 | Abort ADR-003            | Botón Detener + Escape → `_currentTurnCts.Cancel()`   |
| ADR-001            | Historial por projectKey | Carga/guarda con `vbe.GetProjectKey()`                |
| ADR-003            | Un CTS por turno         | `_currentTurnCts` propiedad del Form                  |
| ADR-003            | Cancelación ≠ error      | "Cancelado por el usuario", no rojo                   |
| PRD FR-02          | Streaming en vivo        | `OnTextChunk` → `AppendChunk`                         |
| PRD FR-02          | Tool calls               | `OnToolCall` → `bridgeClient.InvokeAsync`             |
| PRD FR-03          | Contexto VBE             | `IVbeContextProvider` + `ISystemPromptBuilder`        |
| PRD FR-05          | Tools → "NIM Terminal"   | `BlsVbeMenuManager` (skill 02)                        |
| PRD FR-05          | Textos en español        | Todos los strings de UI en español                    |
| skill 06 §1        | Creación en hilo UI      | Menú se dispara desde el VBE (hilo UI)                |
| skill 06 §2        | Marshaling               | `OnUi` para todos los callbacks del sink              |
| skill 06 §5        | Cierre limpio            | `OnFormClosing` cancela + flush historial             |

---

## 5. Flujo interno

### Apertura
```
1. Usuario: Tools → "NIM Terminal"
2. Handler en BlsVbeMenuManager (hilo UI):
     a. terminal = BlsNimTerminal.ShowOrFocus(owner: VBE HWND, services)
     b. Si ya existe → Activate() y return
     c. Si no → new BlsNimTerminal(services)
     d. Cargar historial (async, pero UI ya visible)
     e. Mostrar como modal? NO → Show(owner)
3. Al mostrar:
     a. Suscribirse a FormClosing
     b. Cargar historial con projectKey = vbe.GetProjectKey()
     c. Si ephemeral → mostrar aviso "Historial no persistente"
     d. Estado inicial: "Listo"
```

### Envío de prompt
```
1. Usuario escribe en TextBox + Enter
2. OnSendClick (event handler, hilo UI):
     a. if (_currentTurnCts != null) → return (turno en curso)
     b. prompt = textBox.Text.Trim(); textBox.Clear()
     c. Si prompt vacío → return
     d. AppendUserMessage(prompt)
     e. _currentTurnCts = new CancellationTokenSource()
     f. _detener.Enabled = true
     g. SetState("Pensando…")
     h. _ = RunTurnAsync(prompt, _currentTurnCts.Token)
```

### `RunTurnAsync`
```
1. contexto = await _vbeContext.GetAsync(ct)
2. messages = BuildMessages(historial + contexto + prompt)
3. tools = BuildToolDefinitions()    // las 10 del bridge
4. try
     await _nimClient.SendAsync(messages, tools, this /*IBlsNimSink*/, ct)
   catch OperationCanceledException when ct.IsCancellationRequested
     // OnCancelled ya se llamó desde el cliente
   catch BlsNimException ex
     AppendError(ex.Message)
   finally
     _currentTurnCts?.Dispose(); _currentTurnCts = null
     _detener.Enabled = false
     SetState("Listo")   // salvo que OnCancelled/OnError hayan puesto otro
```

### `OnTextChunk` (IBlsNimSink, hilo de fondo)
```
OnUi(() => AppendChunkToAssistantMessage(text))
```

### `OnToolCall` (IBlsNimSink, hilo de fondo)
```
OnUi(() => AppendToolCallMessage(call))
_ = ExecuteToolAsync(call, _currentTurnCts.Token)
```

### `ExecuteToolAsync`
```
1. try
     result = await _bridgeClient.InvokeAsync<JsonElement>(
         call.Name, ParseArgs(call.ArgumentsJson), ct)
     OnUi(() => AppendToolResultMessage(call.Name, result))
     // Añadir mensaje role="tool" al historial en memoria
   catch BlsException ex
     OnUi(() => AppendToolErrorMessage(call.Name, ex.Message))
     // Añadir mensaje role="tool" con el error
   // Nota: tras ejecutar todas las tools, si hay resultado,
   //       se hace un nuevo SendAsync con los tool results.
```

### `OnDone` (IBlsNimSink, hilo de fondo)
```
OnUi(() =>
{
    FinalizeAssistantMessage();
    SetState("Listo");
    _detener.Enabled = false;
    _history.SaveAsync(projectKey, _currentDoc, CancellationToken.None);
});
```

### `OnCancelled`
```
OnUi(() =>
{
    FinalizeAssistantMessage(cancelled: true);
    SetState("Cancelado por el usuario");
    _detener.Enabled = false;
    _history.SaveAsync(projectKey, _currentDoc, CancellationToken.None);
});
```

### `OnError`
```
OnUi(() =>
{
    AppendError(ex.Message);
    SetState("Error");
    _detener.Enabled = false;
    _history.SaveAsync(projectKey, _currentDoc, CancellationToken.None);
});
```

### Cierre
```
OnFormClosing:
1. if (_currentTurnCts != null) _currentTurnCts.Cancel()
2. _history.FlushAsync().Wait(2s)
3. _bridgeClient.Dispose()
4. base.OnFormClosing
```

---

## 6. Contexto VBE en el prompt

El `ISystemPromptBuilder` construye algo así (resumen):

```
Eres un asistente integrado en el editor VBA de Microsoft Access.

Reglas:
- Si una tool falla, propaga el error al usuario. NUNCA inventes éxito.
- Trabaja en el proyecto Access actual.
- Puedes leer y escribir módulos VBA usando las herramientas disponibles.
- Responde en español.

Contexto actual:
- Módulo activo: Utils (Standard)
- Selección: (líneas 10-25)
```vba
Public Function Suma(a As Long, b As Long) As Long
    Suma = a + b
End Function
```

Herramientas disponibles:
- list_modules, read_module, write_module, create_module,
  delete_module, get_selection, get_active_module,
  project_summary, list_objects, object_detail

Proyecto:
- Nombre: MiBase.accdb
- Tablas: 12, Queries: 30, Forms: 8, Reports: 5, Macros: 3
```

**Nota:** el builder no ejecuta tools; solo describe el estado. Si el
modelo necesita más detalle, llama a `list_objects` / `object_detail`.

El contexto se **construye en cada turno** (no se cachea): el usuario
puede haber cambiado de módulo/selección entre turnos.

---

## 7. Layout y controles

### Estructura
```
Form: BlsNimTerminal
├── ToolStrip (opcional, MVP: nada)
├── SplitContainer (vertical)
│   ├── Panel1 (Fill)
│   │   └── ListView _conversation
│   │       ├── Column: "Rol" (80 px)
│   │       └── Column: "Mensaje" (Fill)
│   └── Panel2 (Fixed 120 px)
│       ├── TextBox _input (multiline, Fill)
│       └── Panel inferior
│           ├── Button _detener (Right)
│           └── Label _status (Left)
```

### Propiedades del Form
- `Text = "NIM Terminal"`
- `MinimumSize = (600, 400)`
- `StartPosition = CenterParent`
- `ShowInTaskbar = false`
- `MinimizeBox = true`
- `MaximizeBox = true`
- `FormBorderStyle = Sizable`

### ListView
- `View = View.Details`
- `FullRowSelect = true`
- `GridLines = false`
- `HideSelection = false`
- `HeaderStyle = ColumnHeaderStyle.Nonclickable`
- `MultiSelect = false`
- `Scrollable = true`

### TextBox input
- `Multiline = true`
- `AcceptsReturn = true`
- `ScrollBars = Vertical`
- `Font = Consolas 9pt` (monoespaciado para código)
- `AcceptsTab = false` (Tab navega entre controles)

### Botón Detener
- `Text = "Detener"`
- `Enabled = false` inicialmente
- `Anchor = Top|Right`

### Label estado
- `AutoSize = true`
- `Anchor = Top|Left`
- Estados: `Listo`, `Pensando…`, `Ejecutando tool: {name}`,
  `Cancelando…`, `Cancelado por el usuario`, `Error`,
  `Historial no persistente`.

### Colores por rol
| Rol          | ForeColor     |
|--------------|---------------|
| usuario      | Black         |
| asistente    | DarkBlue      |
| tool (ok)    | DarkGreen     |
| tool (error) | DarkRed       |
| estado       | DimGray       |
| cancelado    | Gray (italic) |

Sin emojis. Texto plano.

---

## 8. Criterios de aceptación

### CA-01 — Apertura
- [ ] Tools → "NIM Terminal" abre el Form con owner = VBE.
- [ ] Segunda invocación → foco a la existente, no abre otra.
- [ ] Cerrar la terminal no cierra Access.
- **Tests:** `Terminal_ShowOrFocus_PrimeraApertura_CreaForm`,
  `Terminal_ShowOrFocus_SegundaApertura_DevuelveMisma`,
  `Terminal_CerrarNoCierraAccess` (integración manual).

### CA-02 — Streaming en vivo
- [ ] Cada `OnTextChunk` se refleja en el ListView.
- [ ] Actualizaciones desde hilo de fondo se marshaling al hilo UI.
- **Tests:** `Terminal_OnTextChunk_ActualizaListView`,
  `Terminal_OnTextChunkDesdeHiloFondo_MarshalingAlHiloUI`.

### CA-03 — Tool calls
- [ ] `OnToolCall` reenvía al bridge y muestra el resultado.
- [ ] Error de tool muestra mensaje original, no "éxito".
- **Tests:** `Terminal_OnToolCall_InvocaBridge`,
  `Terminal_ToolFalla_MuestraError`,
  `Terminal_ToolOk_MuestraResultado`.

### CA-04 — Cancelación (ADR-003)
- [ ] Botón Detener cancela el turno.
- [ ] Escape cancela el turno.
- [ ] Cancelación muestra "Cancelado por el usuario" (no rojo).
- [ ] Detener sin turno activo es no-op.
- **Tests:** `Terminal_DetenerClick_CancelaTurno`,
  `Terminal_Escape_CancelaTurno`,
  `Terminal_Cancelacion_MuestraEstadoCancelado`,
  `Terminal_DetenerSinTurno_EsNoOp`.

### CA-05 — Contexto VBE
- [ ] El prompt incluye módulo activo y selección.
- [ ] Sin módulo activo, el prompt funciona igual (contexto vacío).
- [ ] Selección > 32 KB se trunca y se marca.
- **Tests:** `Terminal_ContextoVbeConModulo_IncluyeNombreYSeleccion`,
  `Terminal_SinModuloActivo_PromptSinContexto`,
  `Terminal_SeleccionGrande_Trunca`.

### CA-06 — Historial
- [ ] Al abrir, carga el historial del `projectKey`.
- [ ] Al cerrar, hace flush sin debounce.
- [ ] Si el proyecto es efímero, muestra aviso.
- **Tests:** `Terminal_Abrir_CargaHistorial`,
  `Terminal_Cerrar_FlushHistorial`,
  `Terminal_ProyectoEfimero_MuestraAviso`.

### CA-07 — API key
- [ ] Sin API key → diálogo modal con `PasswordChar='●'`.
- [ ] Cancelar → no envía, muestra error.
- [ ] Guardar → `SaveApiKey` y continúa.
- **Tests:** `Terminal_SinApiKey_MuestraPrompt`,
  `Terminal_PromptCancelado_NoEnvia`,
  `Terminal_PromptGuardado_Continua`.

### CA-08 — Una sola terminal
- [ ] `ShowOrFocus` dos veces → un solo `Form`.
- **Test:** `Terminal_ShowOrFocus_DosVecesUnSoloForm`.

### CA-09 — Errores
- [ ] Error de NIM → mensaje en el ListView, no "éxito".
- [ ] Error de bridge → mensaje en el ListView.
- **Tests:** `Terminal_NimError_MuestraMensaje`,
  `Terminal_BridgeError_MuestraMensaje`.

### CA-10 — Textos en español
- [ ] Todos los `Text` de controles en español.
- [ ] Todos los estados en español.
- **Test:** `Terminal_TodosLosTextos_EnEspanol` (inspección).

---

## 9. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── UI/
│   ├── BlsNimTerminal.cs                   (nuevo)
│   ├── BlsNimTerminal.Designer.cs          (generado por VS)
│   ├── BlsNimTerminal.resx                 (generado por VS)
│   ├── IBlsBridgeClient.cs                 (nuevo)
│   ├── BlsBridgeClient.cs                  (nuevo)
│   ├── IVbeContextProvider.cs              (nuevo, internal)
│   ├── VbeContextProvider.cs               (nuevo, internal)
│   ├── ISystemPromptBuilder.cs             (nuevo, internal)
│   ├── SystemPromptBuilder.cs              (nuevo, internal)
│   ├── IUserInputDialog.cs                 (nuevo, internal)
│   ├── WinFormsUserInputDialog.cs          (nuevo, internal)
│   ├── ApiKeyPromptForm.cs                 (nuevo)
│   ├── ApiKeyPromptForm.Designer.cs        (generado)
│   └── ApiKeyPromptForm.resx               (generado)

tests/Balansoft.AIAddin.Tests/
├── UI/
│   ├── BlsNimTerminalTests.cs              (nuevo)
│   ├── VbeContextProviderTests.cs          (nuevo)
│   ├── SystemPromptBuilderTests.cs         (nuevo)
│   └── BlsBridgeClientTests.cs             (nuevo)
└── Fakes/
    ├── FakeNimClient.cs                    (nuevo)
    ├── FakeBridgeClient.cs                 (nuevo)
    ├── FakeChatHistory.cs                  (nuevo)
    ├── FakeSecureStore.cs                  (ya existe del #2)
    └── FakeUserInputDialog.cs              (nuevo)
```

---

## 10. Decisiones internas (no requieren ADR)

### 10.1 `Show(owner)` no modal
La terminal es no-modal: el usuario puede seguir editando VBA mientras
habla con NIM. Modal bloquearía el IDE. `Show(owner)` permite
interacción con el resto del IDE.

**Excepción:** el diálogo de API key sí es modal (`ShowDialog`), porque
bloquea el turno hasta que el usuario decide.

### 10.2 Owner = HWND del VBE
Si `GetVbeHwnd` falla → `Show()` sin owner + log WARN. La terminal
funciona igual, solo pierde "centrada respecto al VBE".

### 10.3 Una sola instancia vía estático débil
`ShowOrFocus` usa un campo estático `WeakReference<BlsNimTerminal>`. Si
la terminal se recolecta, la siguiente invocación crea una nueva. Evita
referencias fuertes que prevengan el GC del Form.

### 10.4 `IBlsBridgeClient` conecta al abrir la terminal
El cliente del bridge se conecta al pipe al abrir la terminal. Al
cerrarla, se desconecta. Si el bridge no está corriendo (porque el
add-in aún no arrancó el VBE), `InvokeAsync` falla con error tipado →
se muestra en el ListView.

### 10.5 Reenvío de tool results a NIM
Tras ejecutar todas las tool calls de un turno, la terminal hace un
**nuevo** `SendAsync` con los resultados como mensajes `role="tool"`.
Esto cierra el ciclo del function calling.

El `_currentTurnCts` **se mantiene** durante todo el ciclo: no se crea
uno nuevo por cada reenvío. Un turno del usuario puede tener varios
`SendAsync` (uno por cada round de tool calls) con el mismo token
(ADR-003).

### 10.6 El historial se guarda al final del turno, no por chunk
`OnTextChunk` **no** dispara guardado. Solo `OnDone`, `OnCancelled` y
`OnError`. El debounce 500 ms del skill 05 se usa para ediciones
posteriores, pero no es necesario en streaming.

### 10.7 `Escape` cancela
`Form.KeyPreview = true` + handler de `KeyDown` con `Keys.Escape` →
`OnDetenerClick`. El input multiline no captura Escape, así que no hay
conflicto.

### 10.8 `Enter` envía, `Shift+Enter` nueva línea
En `_input.KeyDown`:
```
if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; OnSendClick(...); }
```

### 10.9 Sin `ToolStrip`
MVP no tiene barra de herramientas. El botón Detener y el label estado
van abajo. Simple.

### 10.10 Fuente monoespaciada en input
`Consolas 9pt` en `_input` facilita pegar código. El ListView usa la
fuente por defecto del sistema.

### 10.11 Truncado de selección a 32 KB
`IVbeContextProvider` trunca `SelectionText` a 32 KB (skill 02). El
contexto completo se construye en cada turno, no se cachea.

### 10.12 Reenvío de tool results en el mismo CTS
El turno del usuario es la unidad de cancelación (ADR-003). Los
múltiples `SendAsync` de un mismo turno comparten el `_currentTurnCts`.
Si el usuario cancela en medio de un reenvío, se cancelan todos.

### 10.13 Errores de bridge se muestran como mensaje `tool`
Cuando una tool falla, la terminal añade un mensaje `role="tool"` con
el error. Ese mensaje se envía a NIM en el siguiente `SendAsync`. El
modelo ve el error y puede reaccionar. Nunca se oculta.

---

## 11. Fuera de alcance (recordatorio)

- Múltiples sesiones/tabs.
- Selección de modelo/proveedor.
- Panel de configuración.
- Exportar conversación.
- Búsqueda en historial.
- Autocompletado.
- Adjuntar archivos.
- Imágenes / audio.
- Tema oscuro.
- Atajos configurables.
- Menú contextual.
- Persistencia de posición/tamaño del Form entre sesiones.

---

## 12. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md`.
2. `.opencode/skills/00-project-context.md`.
3. `.opencode/skills/02-vbe-automation.md` (`IBlsVbe`, menú Tools).
4. `.opencode/skills/03-nim-client-streaming.md` (`IBlsNimSink`).
5. `.opencode/skills/04-jsonrpc-pipe-bridge.md` (contrato del bridge).
6. `.opencode/skills/05-secure-store-dpapi.md` (prompt API key, historial).
7. `.opencode/skills/06-winforms-threading.md` (marshaling, ciclo de vida).
8. `.opencode/skills/08-logging-errors.md` (errores tipados, system prompt).
9. `.opencode/skills/09-testing-strategy.md` (fakes).
10. `docs/design/ADR-001-project-identity.md`.
11. `docs/design/ADR-003-abort-model.md`.
12. `docs/design/20250101-vbe-bridge.md` (interfaz `IBlsBridgeServer`).
13. `docs/design/20250102-nim-client.md` (interfaz `IBlsNimClient`).

---

## 13. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests sin Office ni red
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Terminal"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~VbeContext"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~SystemPrompt"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~BridgeClient"
```

Criterio de éxito: build verde + tests de CA-01..CA-10 verdes.

---

## 14. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §7 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §12 están listados.
- [ ] Los diseños de #1 y #2 están aprobados (dependencias).

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-10.
- [ ] No modificar interfaces del §3 ni de #1 ni de #2.
- [ ] Referenciar este doc en el commit:
      `feat: terminal NIM WinForms (refs design/20250103-nim-terminal.md)`.

---

## 15. Referencias

- `AGENTS.md` reglas 3, 4, 5, 10.
- `docs/design/ADR-001-project-identity.md`.
- `docs/design/ADR-003-abort-model.md`.
- `docs/design/20250101-vbe-bridge.md`.
- `docs/design/20250102-nim-client.md`.
- `PRD.md` §3.4, §7 FR-02, FR-03, FR-05, §11.
- `.opencode/skills/02-vbe-automation.md`.
- `.opencode/skills/03-nim-client-streaming.md`.
- `.opencode/skills/04-jsonrpc-pipe-bridge.md`.
- `.opencode/skills/05-secure-store-dpapi.md`.
- `.opencode/skills/06-winforms-threading.md`.
- `.opencode/skills/08-logging-errors.md`.
- `.opencode/skills/09-testing-strategy.md`.
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa la terminal (bloqueado hasta que #1 y #2 estén mergeados)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/00-project-context.md
3. docs/design/20250103-nim-terminal.md (el diseño)
4. Los skills citados en §12 del diseño
5. Los ADRs y diseños referenciados en §15

Implementa la feature 'NIM Terminal' exactamente como está especificado.
No cambies interfaces del §3, ni de #1 (bridge), ni de #2 (client).
No añadas features del §11.
Entrega diff completo + tests + comando de verificación del §13."
```

### Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/10-workflow-big-pickle-nemotron.md (checklist de rechazo)
3. docs/design/20250103-nim-terminal.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill 10.
Presta especial atención a:
- Marshaling al hilo UI (skill 06 §2).
- Cancelación ≠ error (ADR-003).
- Historial guardado al final del turno, no por chunk (§10.6).
- Reenvío de tool results con el mismo CTS (§10.12).
- System prompt incluye la regla 'no inventes éxito' (skill 08).

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño

| # | Doc                                | Estado      | Depende de |
|---|------------------------------------|-------------|------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado | —          |
| 2 | `20250102-nim-client.md`           | ✅ Entregado | —          |
| 3 | `20250103-nim-terminal.md`         | ✅ Entregado | #1, #2     |
| 4 | `20250104-secure-store.md`         | Pendiente   | —          |
| 5 | `20250105-chat-history.md`         | Pendiente   | ADR-001    |
| 6 | `20250106-registration-scripts.md` | Pendiente   | —          |
| 7 | `20250107-logging-version.md`      | Pendiente   | —          |
| 8 | `20250108-addin-lifecycle.md`      | Pendiente   | Todos      |

**Ruta crítica hacia el MVP:**
```
#1 bridge ─┐
           ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client ─┘       ↑
                   │
#4 store ──────────┤ (alimenta #2 y #3)
#5 history ────────┤ (alimenta #3)
#6 reg ────────────┘ (necesario para #8)

#7 log ── transversal (alimenta todo)
```

**Paralelización inmediata (4 frentes posibles):**
- Frente A: #1 bridge
- Frente B: #2 client
- Frente C: #4 store + #5 history
- Frente D: #6 reg + #7 log

Luego #3 terminal (necesita #1+#2) y por último #8 lifecycle (integra todo).
