---
name: bls-winforms-threading
description: Terminal WinForms embebida en el VBE y marshaling estricto al hilo UI
---

# SKILL 06 — WinForms UI y threading

> **Referencia ISO/IEC**: 25010 (Reliability) + 12207 (Life cycle processes)
> **Área**: ui | winforms | threading | marshaling
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `UI/BlsNimTerminal.cs`, `VbeIntegration/BlsCodeWindowManager.cs`, o
> cualquier código que cree controles WinForms, actualice la UI desde
> hilos de fondo, o gestione el ciclo de vida de la ventana de la
> terminal.

---

## Contexto

`BlsNimTerminal` es un `Form` WinForms alojado como ventana propiedad del
VBE. Convive con:

- El **hilo UI de Access** (STA) donde vive el VBE.
- El **hilo del pipe** (skill bls-jsonrpc-pipe-bridge) que despacha tool calls.
- Los **hilos de `HttpClient`** que devuelven chunks SSE (skill bls-nim-client-streaming).
- El **hilo del `Task` de streaming NIM**.

WinForms solo admite acceso a controles desde el hilo que creó el
control. Cualquier acceso desde otro hilo lanza
`InvalidOperationException` o corrompe el estado del control.

---

## Reglas duras

### 1. Creación en el hilo UI
`BlsNimTerminal` se instancia y se muestra **exclusivamente** desde el
hilo UI de Access. Esto se garantiza porque:

- El evento de menú "NIM Terminal" lo dispara el VBE (hilo UI).
- `BlsCodeWindowManager` se invoca desde ese handler.

**Prohibido** crear `BlsNimTerminal` desde el hilo del pipe o desde
`Task.Run`.

### 2. Marshaling de actualizaciones
Todo método que actualice un control desde un hilo no-UI pasa por:

```csharp
void OnUi(Action action)
{
    if (_form.IsHandleCreated && _form.InvokeRequired)
        _form.BeginInvoke(action);
    else
        action();
}
```

Reglas:
- `BeginInvoke` (no `Invoke`) para evitar bloqueos circulares.
- Comprobar `IsHandleCreated` antes: si el handle no existe, el control
  no está listo y `BeginInvoke` lanza.
- Capturar excepciones dentro del `action`; `BeginInvoke` no las propaga
  al caller.

### 3. `Application.DoEvents` prohibido
Reentra el bucle de mensajes y causa bugs sutiles (eventos duplicados,
orden de operaciones). **Nunca** usar.

### 4. `async void` solo en event handlers
Métodos `async` que no son event handlers devuelven `Task`:

```csharp
private async void OnSendClick(object sender, EventArgs e)  // OK
{
    await SendPromptAsync();
}

private async Task SendPromptAsync() { ... }                 // OK
```

**Prohibido** `async void` en cualquier otro sitio (no se puede
`await` ni capturar excepciones).

### 5. Cierre limpio
Al cerrar la terminal:

```
1. Cancelar _currentTurnCts (ADR-003) si existe.
2. Esperar hasta 2 s a que el cliente NIM confirme OnCancelled().
3. Flush del historial (sin debounce) — skill bls-secure-store-dpapi.
4. Dispose del CTS.
5. base.OnFormClosing / base.OnFormClosed.
```

Cerrar la terminal **no** debe cerrar Access. Cerrar Access **debe**
cerrar la terminal vía `OnDisconnection`.

---

## Ciclo de vida

### Creación
```csharp
public sealed class BlsNimTerminal : Form
{
    public BlsNimTerminal(
        IBlsNimClient nimClient,
        IBlsChatHistory history,
        IBlsSecureStore secureStore,
        IBlsLogger logger)
    {
        // ...
        Text = "NIM Terminal";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 400);
        ShowInTaskbar = false;
    }
}
```

### Apertura
```csharp
// En BlsVbeMenuManager, handler de click del menú Tools
public void OnNimTerminalClick()
{
    if (_terminal != null && !_terminal.IsDisposed)
    {
        _terminal.Activate();
        return;                        // una sola terminal (invariante)
    }

    _terminal = new BlsNimTerminal(...);
    _terminal.FormClosed += (_, __) => _terminal = null;

    IntPtr owner = _codeWindowManager.GetVbeHwnd();   // skill bls-vbe-automation
    if (owner != IntPtr.Zero)
        _terminal.Show(new WindowWrapper(owner));
    else
        _terminal.Show();               // sin owner, CenterScreen
}
```

`WindowWrapper` es un `IWin32Window` trivial que envuelve el `IntPtr`.

### Cierre desde Access
`OnDisconnection`:

```csharp
public void OnDisconnection(...)
{
    if (_terminal != null && !_terminal.IsDisposed)
    {
        _terminal.Close();              // dispara OnFormClosing
    }
    _terminal = null;
}
```

---

## Layout de la terminal (MVP)

```
+----------------------------------------------------------+
| NIM Terminal                                    [_][□][X]|
+----------------------------------------------------------+
| ListView (conversación)                                  |
|   [usuario]    prompt ...                                |
|   [asistente]  respuesta ...                             |
|   [tool]       write_module → ok                         |
|   [estado]     Pensando…                                 |
+----------------------------------------------------------+
| TextBox (input, multilínea)                 [Detener] |
|-------------------------------------------------------|
|                                                       |
+----------------------------------------------------------+
| Estado: Listo                                            |
+----------------------------------------------------------+
```

### Controles
- `_listView`: `View.Details`, columnas `[Rol][Texto]`. Sin bordes de
  selección visible al añadir (evita parpadeo).
- `_input`: `TextBox.Multiline = true`, `AcceptsReturn = true`,
  `ScrollBars = Vertical`.
- `_detener`: `Button`, deshabilitado si no hay turno activo.
- `_status`: `Label` en la parte inferior.

### Atajos
- **Enter** en `_input` → enviar.
- **Shift+Enter** → nueva línea.
- **Escape** → cancelar turno (equivalente a `_detener`).

### Colores por rol
| Rol       | Color        |
|-----------|--------------|
| usuario   | Negro        |
| asistente | Azul oscuro  |
| tool      | Verde oscuro |
| error     | Rojo oscuro  |
| estado    | Gris         |
| cancelado | Gris itálico |

Sin emojis; texto en español.

---

## Recepción de eventos del cliente NIM

`BlsNimTerminal` implementa `IBlsNimSink` (skill bls-nim-client-streaming). Cada callback
llega desde un hilo de fondo y **debe** marshaling al hilo UI:

```csharp
public void OnTextChunk(string text)
{
    OnUi(() =>
    {
        AppendChunk(text);            // actualiza el ListView
    });
}

public void OnToolCall(ToolCallComplete call)
{
    OnUi(() => AppendStatus($"Ejecutando tool: {call.Name}"));
    _ = ExecuteToolAsync(call);       // se reenvía al bridge
}

public void OnError(BlsNimException ex)
{
    OnUi(() => AppendError(ex.Message));
    SetState("Error");
}

public void OnCancelled()
{
    OnUi(() =>
    {
        AppendStatus("Cancelado por el usuario");
        SetState("Cancelado");
        _detener.Enabled = false;
    });
}

public void OnDone()
{
    OnUi(() =>
    {
        SetState("Listo");
        _detener.Enabled = false;
    });
}
```

### Regla de `BeginInvoke` vs `Invoke`
- **`BeginInvoke`** (asíncrono): siempre que sea posible. Evita
  deadlocks entre hilos.
- **`Invoke`** (bloqueante): solo en el cierre de la ventana, cuando
  necesitas garantizar que la UI ya procesó algo antes de seguir.

---

## Cancelación en UI (ADR-003)

### Botón "Detener"
```csharp
private void OnDetenerClick(object sender, EventArgs e)
{
    if (_currentTurnCts == null || _currentTurnCts.IsCancellationRequested)
        return;                        // no-op
    _currentTurnCts.Cancel();
    _detener.Enabled = false;
    SetState("Cancelando…");
}
```

### Habilitación del botón
- Se habilita al enviar prompt (`SendPromptAsync`).
- Se deshabilita en `OnDone`, `OnError`, `OnCancelled`, o al cancelar.
- Estado inicial: deshabilitado.

### Cierre durante turno activo
`OnFormClosing`:
```csharp
protected override void OnFormClosing(FormClosingEventArgs e)
{
    if (_currentTurnCts != null && !_currentTurnCts.IsCancellationRequested)
    {
        _currentTurnCts.Cancel();
        // NO bloquear el cierre: OnCancelled se disparará y limpiará
    }
    _history.FlushAsync().Wait(TimeSpan.FromSeconds(2));
    base.OnFormClosing(e);
}
```

Nunca mostrar un diálogo modal de "¿seguro que quieres cancelar?" en el
cierre: el usuario ya cerró la ventana.

---

## Estado y feedback

Un único `SetState(string)` centraliza el texto del `_status`:

| Estado                             | Cuándo                                      |
|------------------------------------|---------------------------------------------|
| `Listo`                            | Idle, sin turno                             |
| `Pensando…`                        | Tras enviar prompt, antes del primer chunk  |
| `Ejecutando tool: {name}`          | Tras `OnToolCall`                           |
| `Cancelando…`                      | Tras pulsar Detener, antes de `OnCancelled` |
| `Cancelado por el usuario`         | Tras `OnCancelled`                          |
| `Error`                            | Tras `OnError`                              |
| `Historial no persistente` (aviso) | Si `projectKey.Ephemeral == true`           |

---

## Prohibiciones absolutas

| Anti-patrón                                   | Por qué                                    |
|-----------------------------------------------|--------------------------------------------|
| `Application.DoEvents()`                      | Reentra el bucle de mensajes, bugs sutiles |
| Tocar controles desde hilo NIM sin marshaling | `InvalidOperationException` o corrupción   |
| Crear el `Form` fuera del hilo UI             | Rompe el apartamento STA                   |
| `async void` fuera de event handlers          | No se puede `await` ni capturar            |
| `Control.Invoke` en bucle de eventos          | Deadlocks                                  |
| `Thread.Sleep` en el hilo UI                  | Congela la UI                              |
| Cerrar la terminal cierra Access              | UX incorrecta                              |
| Abrir múltiples terminales                    | Viola invariante (una terminal)            |
| `MessageBox.Show` desde hilo de fondo         | Igual que tocar controles                  |
| Bloquear el cierre con confirmación modal     | Mala UX                                    |

---

## Checklist antes de commitear cambios en UI

- [ ] ¿Todos los accesos a controles desde otros hilos usan `OnUi`?
- [ ] ¿`OnUi` usa `BeginInvoke` (no `Invoke`) salvo en cierre?
- [ ] ¿No hay `Application.DoEvents`?
- [ ] ¿`async void` solo en event handlers?
- [ ] ¿El `Form` se crea en el hilo UI?
- [ ] ¿El owner del `Form` es el HWND del VBE (o `IntPtr.Zero` con log)?
- [ ] ¿El botón Detener respeta ADR-003?
- [ ] ¿El cierre cancela el turno y hace flush del historial?
- [ ] ¿Cerrar la terminal NO cierra Access?
- [ ] ¿Una sola terminal por proceso (foco a la existente)?
- [ ] ¿La UI está en español?

---

## Referencias

- `AGENTS.md` regla 3 (marshaling).
- `docs/design/ADR-003-abort-model.md`.
- PRD §7 FR-02, FR-05.
- `.opencode/skills/bls-vbe-automation/SKILL.md` (HWND del VBE).
- `.opencode/skills/bls-nim-client-streaming/SKILL.md` (`IBlsNimSink`).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (ejecución de tool calls).
- `.opencode/skills/bls-secure-store-dpapi/SKILL.md` (flush del historial al cerrar).
- `.opencode/skills/bls-logging-errors/SKILL.md` (cancelación ≠ error).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (`TestUiScheduler`).

## Referencias Cruzadas

Las siguientes skills están directamente relacionadas y deben consultarse según el área de trabajo:

- [.opencode/skills/bls-project-context/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-project-context/SKILL.md) — Skill 00: Contexto y visión del proyecto
- [.opencode/skills/bls-com-late-binding/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-com-late-binding/SKILL.md) — Skill 01: COM late-binding sin PIAs
- [.opencode/skills/bls-vbe-automation/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-vbe-automation/SKILL.md) — Skill 02: Automatización VBE
- [.opencode/skills/bls-nim-client-streaming/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-nim-client-streaming/SKILL.md) — Skill 03: Cliente NIM streaming
- [.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md) — Skill 04: Bridge JSON-RPC sobre Named Pipe
- [.opencode/skills/bls-secure-store-dpapi/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-secure-store-dpapi/SKILL.md) — Skill 05: DPAPI y almacenamiento seguro
- [.opencode/skills/bls-winforms-threading/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-winforms-threading/SKILL.md) — Skill 06: WinForms y threading
- [.opencode/skills/bls-registration-scripts/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-registration-scripts/SKILL.md) — Skill 07: Registro HKCU + scripts
- [.opencode/skills/bls-logging-errors/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-logging-errors/SKILL.md) — Skill 08: Logging y propagación de errores
- [.opencode/skills/bls-testing-strategy/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-testing-strategy/SKILL.md) — Skill 09: Estrategia de tests
- [.opencode/skills/bls-workflow-nemotron/SKILL.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/skills/bls-workflow-nemotron/SKILL.md) — Skill 10: Workflow Nemotron 3 Ultra
- [.opencode/agents/bls-iso.md](file:///c:/desarrollos/balansoft-access-vba-agent/.opencode/agents/bls-iso.md) — Agente de cumplimiento y gobernanza ISO/IEC (42001, 27001, 25010, 29148, 12207)
