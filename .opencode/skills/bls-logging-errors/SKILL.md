---
name: bls-logging-errors
description: Jerarquía de excepciones tipadas, no-tragar errores y logging seguro
---

# SKILL 08 — Logging y propagación de errores

> **Referencia ISO/IEC**: 25010 (Fault tolerance) + 27001 (Confidentiality in logs)
> **Área**: logging | error-handling | exceptions
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `Helpers/BlsLogger.cs`, `Helpers/BlsVersion.cs`, cualquier captura de
> excepciones (`try/catch`), o al decidir cómo un error se propaga entre
> capas y hacia el modelo NIM.

---

## Parte A — `BlsLogger`

### Objetivo
Log diario con rotación de 7 días, sin dependencias externas (sin
log4net, sin Serilog, sin NLog). Escribe a archivo plano con un lock
interno para ser thread-safe.

### Ubicación y nombre
- Directorio: `%APPDATA%\Balansoft\AIAddin\logs\`
- Archivo: `balansoft-yyyyMMdd.log` (ej. `balansoft-20250101.log`)
- Un archivo por día. Sin subdirectorios.

### Rotación
Al iniciar el add-in (una vez por proceso):

```
1. Listar balansoft-*.log en el directorio.
2. Parsear la fecha del nombre.
3. Eliminar los que tengan > 7 días de antigüedad.
4. No borrar el del día actual aunque el reloj esté raro.
```

Si el parseo de fecha falla → log `WARN` y no borrar (evita borrados
accidentales por nombres corruptos).

### Primera línea del día
La primera vez que se escribe en el log de hoy, se antepone una línea
de contexto:

```
2025-01-01T00:00:00Z [INFO ] [BlsVersion] Balansoft.AIAddin.Access
  versión=0.1.0 commit=a3f1c8e os=Windows 10.0.19045
  access=16.0.14332.20556 bitness=x86
```

`BlsVersion` obtiene:

- `Version`: de `AssemblyInformationalVersion` (o `AssemblyVersion` si no
  existe).
- `Commit`: de `AssemblyMetadata("Commit")` (inyectado por el build) o
  `"unknown"`.
- `OS`: `Environment.OSVersion.VersionString`.
- `Access`: `Application.Version` (late-bound).
- `Bitness`: fijo `"x86"` (el proyecto solo soporta x86).

**Nunca** incluir API key ni PII en esta línea.

### Formato de línea
```
{ISO8601-UTC} [{NIVEL}] [{Componente}] {Mensaje}
```

Ejemplo:
```
2025-01-01T12:34:56Z [INFO ] [BlsNimClient] Streaming iniciado (turnId=42)
2025-01-01T12:34:57Z [WARN ] [BlsChatHistory] Historial no cargable, se empieza vacío
2025-01-01T12:34:58Z [ERROR] [BlsVbeService] write_module falló: Módulo 'X' no encontrado
```

- Nivel alineado a 5 chars (`DEBUG`, `INFO `, `WARN `, `ERROR`).
- Componente = nombre de clase sin namespace.
- Mensaje en **español**, una línea, sin saltos internos.
- Si el mensaje contiene saltos, reemplazar por `\n` literal (escapes
  visibles) para no romper el parseo.

### Niveles
| Nivel   | Uso                                                                                                 |
|---------|-----------------------------------------------------------------------------------------------------|
| `DEBUG` | Trazas de flujo (solo en build Debug). En Release: ignoradas.                                       |
| `INFO`  | Eventos normales: arranque, prompt, chunk, done, cancel.                                            |
| `WARN`  | Algo no ideal pero recuperable: historial corrupto, override inválido, owner del VBE no encontrado. |
| `ERROR` | Fallo real: excepción COM, timeout NIM, fallo al escribir VBE.                                      |

### API pública
```csharp
public interface IBlsLogger
{
    void Debug(string component, string message);
    void Info (string component, string message);
    void Warn (string component, string message);
    void Error(string component, string message, Exception? ex = null);
}
```

`Error` con `ex` añade una línea adicional:
```
  └─ COMException (0x800A9C68): Módulo 'X' no encontrado
       at BlsVbeService.WriteModule(...)
```

El stack se incluye completo solo en build **Debug**. En Release se
incluye solo el `GetType().Name`, `HResult` (si es `COMException`) y el
`Message`. **Nunca** el stack completo (fuga de información).

### Thread safety
Un único `StreamWriter` con `AutoFlush = true` y un `lock` interno:

```csharp
private readonly object _lock = new();
private StreamWriter? _writer;
private DateTime _currentDay;

public void Info(string component, string message)
    => Write("INFO", component, message);

private void Write(string level, string component, string message)
{
    lock (_lock)
    {
        EnsureWriter();               // abre si cambia el día
        _writer!.WriteLine($"{Now():O} [{level,-5}] [{component}] {message}");
    }
}
```

`EnsureWriter` cierra el `StreamWriter` anterior si cambió el día y abre
uno nuevo, escribiendo la primera línea de contexto.

### Prohibiciones absolutas
- **Nunca** loguear la API key (ni completa, ni parcial, ni hash).
- **Nunca** loguear el contenido completo de mensajes de usuario; solo
  longitud y primeras **80 chars** (si aplica).
- **Nunca** loguear el payload completo de código VBA; solo longitud y
  primeras 80 chars.
- **Nunca** loguear el body de respuesta NIM completo; solo `Content-Length`
  y código HTTP.
- **Nunca** loguear credenciales, tokens, cookies, headers de auth.
- **Nunca** usar `Console.WriteLine` (add-in no tiene consola).

---

## Parte B — Captura global de excepciones

En `BalansoftConnect.OnConnection` (o `BalansoftAddin.Start`) se
registran 3 handlers:

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    _logger.Error("AppDomain", "Excepción no controlada", e.ExceptionObject as Exception);
    // NO relanzar. El hilo muere, Access sigue.
};

Application.ThreadException += (_, e) =>
{
    _logger.Error("WinForms", "Excepción en hilo UI", e.Exception);
    MessageBox.Show(
        "Se produjo un error inesperado. Revisa el log para más detalles.",
        "Balansoft Access VBA Agent",
        MessageBoxButtons.OK, MessageBoxIcon.Warning);
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    _logger.Error("TaskScheduler", "Excepción no observada en Task", e.Exception);
    e.SetObserved();     // evita que el proceso termine
};
```

Reglas:

1. **Nunca** relanzar desde `UnhandledException` (mataría Access).
2. **Nunca** mostrar un `MessageBox` en `UnhandledException` (puede ser
   un hilo no-UI).
3. **Siempre** `SetObserved()` en `UnobservedTaskException`.
4. `Application.ThreadException` se activa con
   `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)`
   en `OnConnection`, antes de crear la terminal.

---

## Parte C — Propagación de errores hacia NIM

El modelo **nunca** debe recibir un "hecho" si la operación falló. Esta
es la regla de oro del proyecto (AGENTS.md regla 5, PRD FR-12).

### Contrato JSON-RPC de error (skill bls-jsonrpc-pipe-bridge)

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": {
    "code": -32000,
    "message": "Módulo 'X' no encontrado",
    "data": {
      "category": "VbeException",
      "op": "write_module",
      "hresult": null
    }
  }
}
```

### Mapeo categoría → código
| Categoría                              | Code   | Cuándo                              |
|----------------------------------------|--------|-------------------------------------|
| `InvalidParamsException`               | -32602 | `params` inválidos                  |
| `MethodNotFound`                       | -32601 | `method` fuera de whitelist         |
| `VbeException`                         | -32000 | Error funcional del VBE             |
| `AccessException`                      | -32000 | Error funcional de Access           |
| `ComException`                         | -32000 | Error COM con HRESULT               |
| `TimeoutException` (tool)              | -32002 | Tool sin respuesta 30 s             |
| `OperationCanceledException` (usuario) | —      | No se envía error; ver §Cancelación |
| Cualquier otra                         | -32603 | Excepción no controlada             |

### Mensajes
- `message` = **categoría + mensaje original** del error subyacente.
  - Correcto: `"VbeException: Módulo 'Utils' no encontrado"`
  - Incorrecto: `"Algo salió mal"` (pierde información)
- `data.op` = nombre del método (para que el modelo sepa qué falló).
- `data.hresult` = HRESULT original si aplica (`0x800A9C68`).
- `data.stack` **solo en Debug**.

### Prompt del sistema (regla explícita)
El system prompt que se envía a NIM incluye:

```
Cuando una herramienta (tool) falle, propaga el error al usuario con
la información recibida. NUNCA inventes que la operación tuvo éxito.
Si no estás seguro de si una operación se completó, verifica con otra
tool antes de afirmar nada.
```

### Cancelación ≠ error (ADR-003)
- `OperationCanceledException` cuando el token del turno está cancelado
  **no** es error: es cancelación de usuario.
- Se loguea como `INFO`, no como `ERROR`.
- Se emite `OnCancelled()` en el sink, no `OnError(...)`.
- El bridge, si recibe cancelación antes del VBE, responde con un
  `result` marcado (`{"cancelled": true}`) o no responde (según flujo);
  nunca con un `error`.

---

## Parte D — Errores tipados del proyecto

Jerarquía:

```
BlsException                       (base)
├── BlsComException                (skill bls-com-late-binding)
├── BlsVbeException                (skill bls-vbe-automation)
├── BlsVbeUnavailableException     (skill bls-vbe-automation)
├── BlsInvalidParamsException      (transversal)
├── BlsAccessException             (AccessIntegration)
├── BlsNimException                (skill bls-nim-client-streaming)
│   ├── BlsNimAuthException
│   ├── BlsNimRateLimitException
│   ├── BlsNimServerException
│   ├── BlsNimTimeoutException
│   └── BlsNimProtocolException
├── BlsSecureStoreException        (skill bls-secure-store-dpapi)
├── BlsHistoryIOException          (skill bls-secure-store-dpapi)
└── BlsHistoryCorruptException     (skill bls-secure-store-dpapi)
```

Cada excepción:

- Hereda de `BlsException`.
- Tiene `Message` en español.
- Preserva `InnerException` cuando aplica.
- **Nunca** incluye API key ni PII en `Message`.

---

## Prohibiciones absolutas

| Anti-patrón                                  | Por qué                                       |
|----------------------------------------------|-----------------------------------------------|
| `catch (Exception) { }`                      | Traga errores; el modelo cree que funcionó    |
| `catch { return true; }`                     | Idem                                          |
| Loguear la API key o el body completo de NIM | Fuga de secretos                              |
| Loguear el código VBA completo               | Ruido + PII del cliente                       |
| `Console.WriteLine` en producción            | No hay consola en add-in                      |
| Stack completo en Release                    | Fuga de información                           |
| Relanzar en `UnhandledException`             | Mata Access                                   |
| Olvidar `SetObserved()`                      | Proceso termina por `UnobservedTaskException` |
| Devolver `-32603` cuando aplica `-32000`     | El modelo pierde contexto                     |
| Tratar cancelación como error                | Mala UX, doble reporte                        |
| `MessageBox` desde hilo no-UI                | Crash                                         |

---

## Checklist antes de commitear cambios en logging/errores

- [ ] ¿El log se escribe en `%APPDATA%\Balansoft\AIAddin\logs\`?
- [ ] ¿La rotación borra > 7 días al iniciar?
- [ ] ¿La primera línea del día incluye versión + commit + OS + Access?
- [ ] ¿El nivel está alineado a 5 chars y el formato es el canónico?
- [ ] ¿Nunca se loguea la API key, el código VBA completo, ni el body NIM?
- [ ] ¿Los 3 handlers globales están registrados?
- [ ] ¿`UnobservedTaskException` llama `SetObserved()`?
- [ ] ¿`UnhandledException` no relanza?
- [ ] ¿Los handlers del bridge devuelven errores con categoría + mensaje original?
- [ ] ¿La cancelación de usuario NO se reporta como error?
- [ ] ¿El system prompt prohíbe inventar éxito?

---

## Referencias

- `AGENTS.md` reglas 4, 5.
- `docs/design/ADR-002-pipe-framing.md` (códigos -320xx).
- `docs/design/ADR-003-abort-model.md` (cancelación ≠ error).
- PRD §7 FR-07, FR-12.
- `.opencode/skills/bls-com-late-binding/SKILL.md` (`BlsComException`).
- `.opencode/skills/bls-vbe-automation/SKILL.md` (`BlsVbeException`).
- `.opencode/skills/bls-nim-client-streaming/SKILL.md` (`BlsNimException` y variantes).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (mapeo a códigos JSON-RPC).
- `.opencode/skills/bls-secure-store-dpapi/SKILL.md` (nunca loguear la key).
- `.opencode/skills/bls-registration-scripts/SKILL.md` (línea de versión+commit).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (tests de no-tragar errores).

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
