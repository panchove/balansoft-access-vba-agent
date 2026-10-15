---
name: bls-vbe-automation
description: Automatización VBE, contratos IBlsVbe e IBlsAccess, e identidad de proyecto
---

# SKILL 02 — Automatización VBE

> **Referencia ISO/IEC**: 12207 (Life cycle processes) + 25010 (Functional suitability)
> **Área**: vbe | access | automation
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `BlsVbeService`, `BlsCodeWindowManager`, `BlsVbeMenuManager`, o cualquier
> handler del bridge que toque módulos, selección o ventana del VBE.

---

## Contexto

El VBE (Visual Basic Editor de Access) es **STA** (Single-Threaded
Apartment). Toda interacción con el VBE:

1. Debe ocurrir en el **hilo UI del host** (Access main thread).
2. Debe venir de una referencia obtenida en `OnConnection` (skill bls-com-late-binding).
3. Debe asumir que el VBE puede ser `null` (modo runtime / ACCDE).
4. No es reentrante: serializar operaciones con un semáforo.

Este skill define el contrato `IBlsVbe`, las reglas de cada operación y
cómo se obtiene la identidad de proyecto (ADR-001).

---

## Contrato `IBlsVbe`

Vive en `VbeIntegration/IBlsVbe.cs`. Es el único punto que el resto del
código (bridge, terminal) ve del VBE. `BlsVbeService` implementa el
contrato usando `dynamic` por dentro (aislado en `BlsComHelper`).

```csharp
public interface IBlsVbe
{
    bool IsAvailable { get; }                            // VBE != null
    IReadOnlyList<string> ListModules();
    string ReadModule(string name);
    void   WriteModule(string name, string code);
    void   CreateModule(string name, string code, VbeComponentType type);
    void   DeleteModule(string name, bool confirm);
    SelectionInfo GetSelection();
    ModuleInfo    GetActiveModule();
    ProjectKeyInfo GetProjectKey();                      // ADR-001
}

public enum VbeComponentType
{
    Standard,   // vbext_ct_StdModule
    Class,      // vbext_ct_ClassModule
    Form        // vbext_ct_MSForm
}

public readonly struct SelectionInfo
{
    public string ModuleName { get; init; }   // null si no hay CodePane
    public int StartLine { get; init; }
    public int StartColumn { get; init; }
    public int EndLine { get; init; }
    public int EndColumn { get; init; }
    public string Text { get; init; }         // "" si no hay selección
    public bool HasSelection { get; init; }
}

public readonly struct ModuleInfo
{
    public string Name { get; init; }
    public VbeComponentType Type { get; init; }
    public string ProjectName { get; init; }
}
```

---

## Reglas por operación

### `IsAvailable`
Debe devolver `false` si `VBE == null`. **Nunca** lanza. El bridge la
consulta antes de despachar cualquier operación VBE.

### `ListModules`
- Recorre `vbe.ActiveVBProject.VBComponents`.
- Devuelve solo el `Name` de cada uno.
- Orden: el que devuelve COM (no reordenar; el modelo se adapta).
- Si `ActiveVBProject == null` → `BlsVbeUnavailableException`.

### `ReadModule(name)`
- Busca el componente por `Name` (case-insensitive, VBE es case-insensitive
  pero preserva el original).
- Devuelve `component.CodeModule.Lines(1, CountOfLines)`.
- Si no existe → `BlsVbeException` con `"Módulo 'X' no encontrado"`.
- **No** aplica ninguna transformación al código (ni trim, ni normalizar
  saltos). El modelo ve el código tal cual.

### `WriteModule(name, code)`
- Módulo debe existir. Si no → `BlsVbeException` (no crear implícito).
- **Preservar** la línea `Attribute VB_Name = "..."` existente. Si el
  `code` entrante la trae, se ignora la entrante.
- **Preservar** los demás `Attribute VB_*` (`VB_GlobalNameSpace`,
  `VB_Creatable`, `VB_PredeclaredId`, `VB_Exposed`) si el componente los
  tenía. Esto es crítico para formularios y clases con `PredeclaredId`.
- Implementación: `codeModule.DeleteLines(1, CountOfLines)` +
  `codeModule.InsertLines(1, normalizedCode)` **después** de reconstruir
  los atributos.
- **Nunca** sobrescribir un módulo con nombre duplicado en otro proyecto.
- Normalizar saltos a `\r\n` (VBE los espera así).

### `CreateModule(name, code, type)`
- Validar `name` con regex `^[A-Za-z_][A-Za-z0-9_]{0,30}$`.
  - No empezar por dígito.
  - Máx 31 chars (límite VBE).
  - Sin espacios, sin acentos, sin `ñ`.
- Rechazar si ya existe un componente con ese nombre (case-insensitive)
  → `BlsVbeException` con `"Módulo 'X' ya existe"`. **No** sobrescribir.
- Mapeo de tipo:
  - `Standard` → `vbext_ct_StdModule` (1)
  - `Class`    → `vbext_ct_ClassModule` (2)
  - `Form`     → `vbext_ct_MSForm` (3)
- `code` puede ser vacío; luego se puede llamar `WriteModule`.
- Insertar código tras crear:
  - Añadir `Attribute VB_Name = "name"` si `code` no lo trae.
  - Para `Form`, añadir `Attribute VB_Exposed = False` y
    `Attribute VB_Creatable = False` por defecto (son formularios, no
    clases públicas).

### `DeleteModule(name, confirm)`
- Si `confirm != true` → `BlsInvalidParamsException`
  `"delete_module requiere confirm:true"`. **Nunca** borrar por accidente.
- Módulo debe existir.
- Borrar con `VBComponents.Remove(component)`.
- **No** intentar borrar el último módulo del proyecto (VBE lo permite,
  pero no lo hacemos por prudencia): si `VBComponents.Count == 1` →
  error `"No se puede borrar el último módulo del proyecto"`.
- Registrar en log `WARN` el borrado con nombre y proyecto.

### `GetSelection()`
- Usar `vbe.ActiveCodePane`.
- Si `ActiveCodePane == null` → `SelectionInfo` vacío (`HasSelection=false`),
  **no error**.
- Llamar `codePane.GetSelection(out sl, out sc, out el, out ec)`.
- `Text` = `codePane.CodeModule.Lines(sl, el - sl + 1)` recortado a las
  columnas si la selección es de una sola línea.
- **Límite de tamaño**: si la selección > 32 KB, truncar y añadir
  `"\n... [selección truncada a 32 KB]"`. Evita reventar el token budget.
- Las líneas y columnas son 1-based (COM).

### `GetActiveModule()`
- `vbe.ActiveCodePane.CodeModule.Parent` da el `VBComponent`.
- Si no hay `CodePane` activo → `null` (representado por struct con
  `Name = null`).
- Devuelve también el `ProjectName` (`component.Collection.Parent.Name`).

### `GetProjectKey()` — ADR-001
Cascada (ver ADR-001 para detalle):

```
1. Si VBProject.Description empieza por "BALANSOFT-PROJECT-KEY:" →
   usar valor (validado ^[A-Za-z0-9_-]{1,64}$). Source = "override".
2. Si CurrentProject.FullName no vacío →
   SHA256(full.ToLowerInvariant().Replace('/','\\')).Substring(0,32).
   Source = "path".
3. Fallback: SHA256(VBProject.Name + "|" + MachineName + "|" + UserName)
   .Substring(0,32). Ephemeral = true. Source = "ephemeral".
```

---

## Marshaling al hilo UI

`BlsVbeService` recibe un `IUiScheduler` en el constructor:

```csharp
public interface IUiScheduler
{
    T Run<T>(Func<T> op);
    Task<T> RunAsync<T>(Func<T> op);
}
```

Implementación real (`WinFormsUiScheduler`) usa un `Control` creado en
el hilo UI de Access:

```csharp
public T Run<T>(Func<T> op) => (T)_control.Invoke(op);
```

Implementación de test (`TestUiScheduler`) ejecuta en el hilo actual.

**Regla:** ningún método de `BlsVbeService` se llama directamente desde
un hilo NIM. Siempre a través del `IUiScheduler`. Los handlers del
bridge (skill bls-jsonrpc-pipe-bridge) son responsables de invocar `_uiScheduler.Run(...)`.

---

## Serialización de operaciones

El VBE no es reentrante. `BlsVbeService` tiene un `SemaphoreSlim(1,1)`:

```csharp
private readonly SemaphoreSlim _gate = new(1, 1);

public void WriteModule(string name, string code)
{
    _ui.Run(() =>
    {
        _gate.Wait();
        try { DoWriteModule(name, code); }
        finally { _gate.Release(); }
    });
}
```

Esto evita condiciones de carrera si el usuario pulsa varias cosas a la
vez, y garantiza que el VBE nunca ve operaciones entrelazadas.

---

## `BlsVbeMenuManager` — menú Tools

Responsabilidad: añadir "NIM Terminal" al menú Tools del VBE,
**idempotente**.

- Usa `CommandBars` de VBE (late-bound, vía `BlsComHelper`).
- Localiza el popup `"&Tools"` del `"Menu Bar"`.
- Si ya hay un control con `Tag = "BalansoftNimTerminal"` → no duplicar.
- Si no, inserta un botón con:
  - `Caption = "NIM Terminal"`
  - `Tag = "BalansoftNimTerminal"`
  - `OnAction = ""` (no macro; se maneja con evento COM `OnClick`).
- El click abre la terminal (`BlsNimTerminal`) con owner = VBE HWND.
- En `OnDisconnection` se elimina el botón si existe.

**Idempotencia:** llamar `EnsureMenu()` N veces → un solo botón.

---

## `BlsCodeWindowManager` — ventana propietaria

Responsabilidad: crear el `Form` WinForms que alojará la terminal, con
`Owner = HWND del VBE`.

Obtención del HWND del VBE:

```csharp
IntPtr hwnd = BlsComHelper.Invoke(
    () => (IntPtr)vbe.MainWindow.HWnd,
    nameof(GetVbeHwnd));
```

Fallback si `MainWindow` no está disponible (VBE cerrado pero add-in
vivo):

- `FindWindow("wndclass_desked_gsk", null)` (clase del VBE).
- Si tampoco → `IntPtr.Zero` y la terminal se abre sin owner
  (`StartPosition = CenterScreen`). Log `WARN`.

**Prohibido** usar `Application.MainWindow.HWnd` (es la ventana de
Access, no del VBE).

---

## Errores tipados

| Excepción                    | Cuándo                                                 |
|------------------------------|--------------------------------------------------------|
| `BlsVbeUnavailableException` | `VBE == null` o `ActiveVBProject == null`              |
| `BlsVbeException`            | Módulo no encontrado, ya existe, último módulo, etc.   |
| `BlsInvalidParamsException`  | `name` inválido, `confirm=false`, tipo inválido        |
| `BlsComException`            | Error COM no clasificable (skill bls-com-late-binding) |

Todos preservan `Message` original cuando viene de COM.

---

## Checklist antes de commitear cambios en VBE

- [ ] ¿Todos los métodos pasan por `_ui.Run(...)`?
- [ ] ¿El `SemaphoreSlim` envuelve cada operación VBE?
- [ ] ¿`WriteModule` preserva `Attribute VB_*` del módulo?
- [ ] ¿`CreateModule` valida el nombre con la regex y rechaza duplicados?
- [ ] ¿`DeleteModule` exige `confirm:true`?
- [ ] ¿`GetSelection` no falla si no hay `CodePane`?
- [ ] ¿`GetProjectKey` sigue la cascada de ADR-001?
- [ ] ¿El menú Tools es idempotente?
- [ ] ¿Se comprueba `VBE == null` antes de cualquier uso?
- [ ] ¿Ningún `dynamic` fuera de `Helpers/BlsComHelper.cs`?

---

## Referencias

- `AGENTS.md` reglas 1, 2, 3.
- `docs/design/ADR-001-project-identity.md`.
- PRD §7 FR-03, FR-05.
- `.opencode/skills/bls-com-late-binding/SKILL.md` (envoltura COM, `dynamic` aislado).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (handlers que llaman a este servicio).
- `.opencode/skills/bls-winforms-threading/SKILL.md` (owner del VBE, marshaling).
- `.opencode/skills/bls-logging-errors/SKILL.md` (errores tipados).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (`MockVbe`, `TestUiScheduler`).

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
