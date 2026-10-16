---
name: bls-com-late-binding
description: COM late-binding sin PIAs y aislamiento de dynamic en BlsComHelper
---

# SKILL 01 — COM late binding (Access/VBE x86, sin PIAs)

> **Referencia ISO/IEC**: 12207 (Life cycle processes) + 25010 (Maintainability)
> **Área**: com | vbe | late-binding
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** SIEMPRE que escribas código que toque
> `Access.Application`, `VBE`, `VBProject`, `VBComponent`, o cualquier
> objeto COM de Office.

---

## Principios

Access 2010+ solo x86 sin PIAs obliga a late binding. Todo acceso a COM
se hace vía `dynamic`, pero el `dynamic` **no puede estar disperso** por
el código: se aísla en `Helpers/BlsComHelper.cs` y el resto del código
trabaja con tipos propios.

Razones:

1. **Sin PIAs**: no hay referencias a `Microsoft.Office.Interop.*`.
2. **Sin registro de tipos**: no podemos usar interfaces `[ComImport]`
   para VBE/Access porque la versión de Office varía (2010, 2016, 365).
3. **x86 only**: la DLL se compila `PlatformTarget=x86` y se registra con
   el regasm x86. Cargar en Access x64 **no** es soportado.
4. **Sin admin**: el registro vive en HKCU (`regasm /codebase`).

---

## Reglas duras

### 1. Obtención de referencias COM
Únicas fuentes permitidas:

- `Application.VBE` inyectado por el host COM Add-in en `OnConnection`.
- `Application.CurrentProject`, `Application.CurrentDb`, etc., obtenidos
  del mismo `Application` inyectado.
- `CreateObject` / `GetObject` **solo** en tests (fakes).

**Prohibido:**
- `Marshal.GetActiveObject("Access.Application")`
- Running Object Table (ROT)
- `Type.GetTypeFromProgID("Access.Application")` fuera de tests
- `Activator.CreateInstance(Type.GetTypeFromProgID(...))`

### 2. Aislamiento de `dynamic`
El `dynamic` vive **solo** en `Helpers/BlsComHelper.cs`. Fuera de ahí,
prohibido. El resto del código usa tipos propios:

```csharp
public interface IBlsVbe { /* ... */ }
public interface IBlsAccess { /* ... */ }
```

`BlsComHelper` envuelve `dynamic` y expone `IBlsVbe`/`IBlsAccess`.

### 3. Envoltura de toda llamada COM
Toda llamada puede lanzar `COMException` con HRESULT variable
(`0x800Axxxx`, `0x80040154`, `0x8001010A`, ...). Envolver siempre:

```csharp
public static T Invoke<T>(Func<T> op, string opName)
{
    try
    {
        return op();
    }
    catch (COMException ex)
    {
        throw new BlsComException(
            opName:   opName,
            category: MapHresult(ex.HResult),
            message:  ex.Message,
            inner:    ex);
    }
}
```

### 4. `VBE == null` es un caso válido
Access puede arrancar en **modo runtime** (`.accde`, `/runtime`) donde
`Application.VBE` es `null`. El add-in debe:

- Detectar `VBE == null` en `AttachVbe`.
- No lanzar excepción no controlada.
- Degradar: no registra menú, no arranca bridge VBE, log `WARN`.
- No impedir la carga de Access.

**Prohibido** asumir `VBE != null` en cualquier punto del código.

### 5. Threading
`BlsComHelper` **no** hace marshaling por ti. El marshaling al hilo UI
es responsabilidad del caller (ver skill bls-winforms-threading). Regla simple:

- Si no estás en el hilo UI, no toques COM.
- Si no sabes si estás en el hilo UI, asume que no.

### 6. `ReleaseComObject` prohibido
Dejar que el GC + RCW gestionen la vida de los objetos COM. Llamar
`Marshal.ReleaseComObject` manualmente suele causar más problemas
(doble release, objetos en uso, cierres inesperados de Access) que los
que resuelve.

### 7. Propiedades que devuelven `dynamic`
Muchas propiedades COM devuelven `object` que hay que castear. Siempre
castear con comprobación:

```csharp
var name = BlsComHelper.Invoke(() => (string)vbe.ActiveVBProject.Name, "GetProjectName");
```

Si el cast falla → `InvalidCastException`, que `BlsComHelper` también
captura y re-lanza como `BlsComException` categoría `UnexpectedType`.

---

## Categorías de error (`BlsComCategory`)

| Categoría        | HRESULT típico | Significado                                        |
|------------------|----------------|----------------------------------------------------|
| `NotRegistered`  | `0x80040154`   | Clase COM no registrada                            |
| `Busy`           | `0x8001010A`   | Servidor COM ocupado (RPC_E_SERVERCALL_RETRYLATER) |
| `Rejected`       | `0x80010001`   | Llamada rechazada por el servidor                  |
| `Permission`     | `0x80070005`   | E_ACCESSDENIED                                     |
| `UnexpectedType` | —              | Cast fallido en un `dynamic`                       |
| `Other`          | resto          | Otro COMException no clasificado                   |

`BlsComException` expone `Category`, `Message` (original), `HResult`,
`Operation` (nombre del método que falló) e `InnerException`.

---

## Patrón canónico de envoltura

### `BlsComHelper`
```csharp
internal static class BlsComHelper
{
    public static T Invoke<T>(Func<T> op, string opName)
    {
        try
        {
            return op();
        }
        catch (COMException ex)
        {
            throw new BlsComException(opName, MapHresult(ex.HResult), ex.Message, ex);
        }
        catch (InvalidCastException ex)
        {
            throw new BlsComException(opName, BlsComCategory.UnexpectedType, ex.Message, ex);
        }
    }

    public static void Invoke(Action op, string opName)
        => Invoke<object>(() => { op(); return null; }, opName);

    private static BlsComCategory MapHresult(int hr) => hr switch
    {
        unchecked((int)0x80040154) => BlsComCategory.NotRegistered,
        unchecked((int)0x8001010A) => BlsComCategory.Busy,
        unchecked((int)0x80010001) => BlsComCategory.Rejected,
        unchecked((int)0x80070005) => BlsComCategory.Permission,
        _                          => BlsComCategory.Other
    };
}
```

### Uso típico
```csharp
public string GetProjectName(dynamic vbe)
{
    return BlsComHelper.Invoke(
        () => (string)vbe.ActiveVBProject.Name,
        nameof(GetProjectName));
}
```

### `IBlsVbe` (contrato propio)
```csharp
public interface IBlsVbe
{
    IReadOnlyList<string> ListModules();
    string ReadModule(string name);
    void   WriteModule(string name, string code);
    void   CreateModule(string name, string code, VbeComponentType type);
    void   DeleteModule(string name, bool confirm);
    SelectionInfo GetSelection();
    ModuleInfo    GetActiveModule();
}
```

`BlsVbeService : IBlsVbe` implementa el contrato y usa `dynamic` por
dentro, pero los consumidores (bridge, terminal) solo ven `IBlsVbe`.

---

## Anti-patrones prohibidos (rechazo automático en PR)

| Anti-patrón                                                        | Por qué                            |
|--------------------------------------------------------------------|------------------------------------|
| `dynamic vbe = Marshal.GetActiveObject("Access.Application").VBE;` | ROT prohibido                      |
| `Type.GetTypeFromProgID("Access.Application")` fuera de tests      | Prohibido                          |
| `[ComImport]` manual para interfaces de Office                     | Varía por versión                  |
| `Marshal.ReleaseComObject(vbe)`                                    | Doble release, cierres inesperados |
| `dynamic` fuera de `Helpers/BlsComHelper.cs`                       | Dispersión incontrolable           |
| `catch (Exception) { }` en llamadas COM                            | Traga errores; usar `BlsComHelper` |
| `Application.VBE` sin comprobar `null`                             | Rompe en runtime/ACCDE             |
| `Thread.Sleep` esperando a COM                                     | Bloquea; usar `await`/`Task`       |
| Llamar COM desde hilo NIM sin marshaling                           | Crash del host                     |

---

## Checklist antes de commitear código COM

- [ ] ¿Toda referencia COM viene de `Application` inyectado?
- [ ] ¿`dynamic` está solo en `Helpers/BlsComHelper.cs`?
- [ ] ¿Cada llamada COM está envuelta en `BlsComHelper.Invoke`?
- [ ] ¿Se comprueba `VBE == null` en `AttachVbe` y antes de usarlo?
- [ ] ¿El marshaling al hilo UI está explícito (skill bls-winforms-threading)?
- [ ] ¿No hay `ReleaseComObject` ni `GetActiveObject`?
- [ ] ¿Los errores se propagan con `BlsComException` (no se tragan)?

---

## Referencias

- `AGENTS.md` reglas 1, 2, 3, 5.
- PRD §8 (compatibilidad x86, sin PIAs).
- `.opencode/skills/bls-vbe-automation/SKILL.md` (uso del contrato `IBlsVbe`).
- `.opencode/skills/bls-winforms-threading/SKILL.md` (marshaling al hilo UI).
- `.opencode/skills/bls-logging-errors/SKILL.md` (propagación de errores).

## Referencias Cruzadas

Las siguientes skills están directamente relacionadas y deben consultarse según el área de trabajo:

- [.opencode/skills/bls-project-context/SKILL.md](.opencode/skills/bls-project-context/SKILL.md) — Skill 00: Contexto y visión del proyecto
- [.opencode/skills/bls-com-late-binding/SKILL.md](.opencode/skills/bls-com-late-binding/SKILL.md) — Skill 01: COM late-binding sin PIAs
- [.opencode/skills/bls-vbe-automation/SKILL.md](.opencode/skills/bls-vbe-automation/SKILL.md) — Skill 02: Automatización VBE
- [.opencode/skills/bls-nim-client-streaming/SKILL.md](.opencode/skills/bls-nim-client-streaming/SKILL.md) — Skill 03: Cliente NIM streaming
- [.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md](.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md) — Skill 04: Bridge JSON-RPC sobre Named Pipe
- [.opencode/skills/bls-secure-store-dpapi/SKILL.md](.opencode/skills/bls-secure-store-dpapi/SKILL.md) — Skill 05: DPAPI y almacenamiento seguro
- [.opencode/skills/bls-winforms-threading/SKILL.md](.opencode/skills/bls-winforms-threading/SKILL.md) — Skill 06: WinForms y threading
- [.opencode/skills/bls-registration-scripts/SKILL.md](.opencode/skills/bls-registration-scripts/SKILL.md) — Skill 07: Registro HKCU + scripts
- [.opencode/skills/bls-logging-errors/SKILL.md](.opencode/skills/bls-logging-errors/SKILL.md) — Skill 08: Logging y propagación de errores
- [.opencode/skills/bls-testing-strategy/SKILL.md](.opencode/skills/bls-testing-strategy/SKILL.md) — Skill 09: Estrategia de tests
- [.opencode/skills/bls-workflow-nemotron/SKILL.md](.opencode/skills/bls-workflow-nemotron/SKILL.md) — Skill 10: Workflow Nemotron 3 Ultra
- [.opencode/agents/bls-iso.md](.opencode/agents/bls-iso.md) — Agente de cumplimiento y gobernanza ISO/IEC (42001, 27001, 25010, 29148, 12207)
