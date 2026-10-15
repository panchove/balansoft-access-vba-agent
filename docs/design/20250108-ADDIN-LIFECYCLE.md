# Feature: Add-in Lifecycle (OnConnection → OnDisconnection)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-08

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

## Depende de
- `docs/design/20250101-vbe-bridge.md` (`IBlsBridgeServer`).
- `docs/design/20250102-nim-client.md` (`IBlsNimClient`).
- `docs/design/20250103-nim-terminal.md` (`BlsNimTerminal`).
- `docs/design/20250104-secure-store.md` (`IBlsSecureStore`, `IBlsChatHistory`).
- `docs/design/20250106-registration-scripts.md` (`BlsAddinRegistrar`).
- `docs/design/20250107-logging-version.md` (`BlsLogger`, `BlsVersion`).
- `docs/design/ADR-003-abort-model.md` (shutdown).

---

## 1. Contexto

`BalansoftConnect` es el punto de entrada COM del add-in. Implementa
`IDTExtensibility2` y es el único objeto que Access instancia
directamente. Su ciclo de vida determina:

- **Cuándo** se crean los servicios (logger, store, history, vbe,
  access, bridge, nim client).
- **Cuándo** se arrancan (VBE disponible, bridge listo, menú Tools
  instalado).
- **Cuándo** se apagan, en qué orden, y con qué timeouts.

Un ciclo de vida mal gestionado provoca:
- Access que no arranca (add-in que lanza en `OnConnection`).
- `LoadBehavior` degradado a 2 (fallo silencioso).
- Recursos que no se liberan (pipes abiertos, CTS vivos).
- Errores de shutdown (Access que se cierra pero deja procesos
  huérfanos).
- `OnConnection` llamado dos veces (recargas del add-in) que duplican
  handlers o menús.

Este doc cierra el MVP: integra todos los componentes en el ciclo de
vida COM.

Cubre:
- PRD §3.1 (carga en `OnStartupComplete`, limpieza en
  `OnDisconnection`/`OnBeginShutdown`).
- PRD §7 FR-01 (tolerante a `COMException` y `VBE == null`).
- PRD §11 criterios 1, 7, 10.

Y materializa:
- AGENTS.md reglas 1, 2, 3, 5.
- ADR-002, ADR-003 (aplicados al shutdown).
- skill bls-vbe-automation (`AttachVbe`, `GetVbeHwnd`, menú Tools).
- skill bls-winforms-threading (una sola terminal, cierre limpio).
- skill bls-logging-errors (log de arranque, captura global).

---

## 2. Alcance

### Incluido
- `BalansoftConnect` (`IDTExtensibility2`) con los 5 callbacks COM.
- `BalansoftAddin` (orquestador interno, testeable).
- Contenedor de servicios minimalista (sin DI framework).
- Orden de arranque: logger → version → handlers → store → history →
  registrar → vbe → access → bridge → menú → terminal (on-demand).
- Orden de parada: terminal → menú → bridge → vbe → handlers.
- Idempotencia de `OnConnection` (recargas del add-in).
- Tolerancia a `VBE == null` (modo runtime/ACCDE).
- Tolerancia a `COMException` en cualquier punto (no bloquea Access).
- Shutdown con timeouts (bridge 5s, terminal 2s).
- Log de arranque con versión + commit + registro + VBE + Access.
- `OnBeginShutdown` como señal temprana.

### Excluido (fuera de MVP)
- Recarga del add-in sin reiniciar Access (aunque se soporta, no se
  optimiza).
- Múltiples conexiones COM (Access solo hace una).
- Soporte para Access x64.
- Soporte para Word/Excel.
- Instalación por ClickOnce/MSI.
- Actualización en caliente.
- Modo "sin VBE" con funcionalidad degradada más allá del log.
- Panel de configuración post-arranque.
- Auto-actualización de la API key.

---

## 3. Interfaces nuevas

### 3.1 `BalansoftConnect` (Core/)
Punto de entrada COM. Implementa `IDTExtensibility2`.

```csharp
namespace Balansoft.AIAddin.Core;

[ComVisible(true)]
[Guid("d31bf73e-dbe1-4739-84bc-2c974fcd3171")]
[ProgId("Balansoft.AIAddin.Access")]
public sealed class BalansoftConnect : IDTExtensibility2
{
    public BalansoftConnect();          // sin lógica

    // IDTExtensibility2
    public void OnConnection(
        object Application,
        ext_ConnectMode ConnectMode,
        object AddInInst,
        ref Array custom);

    public void OnDisconnection(
        ext_DisconnectMode RemoveMode,
        ref Array custom);

    public void OnAddInsUpdate(ref Array custom);
    public void OnStartupComplete(ref Array custom);
    public void OnBeginShutdown(ref Array custom);
}
```

**Notas:**
- **Sin lógica** en el constructor. Toda la lógica en `OnConnection`.
- Todos los callbacks envueltos en `try/catch`. Un fallo **nunca**
  propaga (degrada `LoadBehavior` a 2).
- `Application` llega como `object` (late-bound).
- Estado interno gestionado por `BalansoftAddin`.

### 3.2 `BalansoftAddin` (Core/)
Orquestador interno. **Sin dependencia de `IDTExtensibility2`**:
recibe `object accessApplication` como parámetro. Esto lo hace
testeable sin COM.

```csharp
namespace Balansoft.AIAddin.Core;

internal sealed class BalansoftAddin : IDisposable
{
    public BalansoftAddin();

    // Ciclo de vida
    public void Start(object accessApplication);
    public void Shutdown(ShutdownReason reason);

    // Acceso a servicios (para tests y diagnóstico)
    public IBlsLogger? Logger { get; }
    public bool IsRunning { get; }
}

internal enum ShutdownReason
{
    OnDisconnection,
    OnBeginShutdown,
    Exception
}
```

**Notas:**
- `Start` es idempotente: si ya arrancó, no hace nada y loguea `WARN`.
- `Shutdown` es idempotente: si ya paró, no hace nada.
- `Dispose` llama `Shutdown(Exception)` si no se llamó antes.

### 3.3 `AddinServiceContainer` (Core/, interno)
Contenedor minimalista. Solo factory methods, sin reflexión, sin DI
framework.

```csharp
internal sealed class AddinServiceContainer : IDisposable
{
    // Servicios construidos en orden
    public IBlsLogger Logger { get; }
    public BlsVersion Version { get; }
    public IBlsSecureStore SecureStore { get; }
    public IBlsChatHistory ChatHistory { get; }
    public IBlsVbe Vbe { get; }
    public IBlsAccess Access { get; }
    public IBlsBridgeServer Bridge { get; }
    public IBlsNimClient NimClient { get; }
    public HttpClient HttpClient { get; }

    // Se construye con el Application de Access
    public static AddinServiceContainer Create(object accessApplication);

    public void Dispose();
}
```

**Notas:**
- Construcción explícita en `Create` (orden importa).
- Sin lazy loading excepto terminal (on-demand).
- `Dispose` libera en orden inverso al de construcción.
- `HttpClient` se construye una vez y se pasa a `NimClient`.

### 3.4 `VbeAttachResult` (Core/, interno)
Resultado de intentar adjuntar al VBE. Permite distinguir "VBE
disponible" de "VBE nulo (modo runtime)".

```csharp
internal readonly struct VbeAttachResult
{
    public bool Available { get; init; }
    public IBlsVbe? Vbe { get; init; }
    public string? Reason { get; init; }   // por qué no está disponible
}
```

**Notas:**
- `Available = false` no es error: es el caso "Access en runtime".
- `Reason` se loguea como `WARN` para diagnóstico.

### 3.5 `IAddinLifecycleObserver` (Core/, interno, opcional)
Hook para tests: permite verificar el orden de arranque sin COM.

```csharp
internal interface IAddinLifecycleObserver
{
    void OnServiceCreated(string serviceName);
    void OnServiceDisposed(string serviceName);
    void OnShutdownStarted(ShutdownReason reason);
    void OnShutdownCompleted();
}
```

**Notas:**
- En producción: no se usa (o se usa un observer que loguea `DEBUG`).
- En tests: se inyecta un observer que graba el orden.
- Permite verificar CA-03 (orden de arranque) sin inspeccionar campos.

---

## 4. Invariantes afectados

| Fuente                   | Invariante                    | Cómo se cumple                                     |
|--------------------------|-------------------------------|----------------------------------------------------|
| AGENTS.md regla 1        | Sin PIAs                      | `object` + `dynamic` solo en `BlsComHelper`        |
| AGENTS.md regla 2        | Sin ROT                       | `Application` viene por parámetro                  |
| AGENTS.md regla 3        | Marshaling UI                 | `IUiScheduler` se crea en `OnConnection` (hilo UI) |
| AGENTS.md regla 5        | Errores reales                | `OnConnection` nunca traga; loguea y degrada       |
| ADR-002                  | Bridge arranca en `AttachVbe` | `Start()` llamado tras `VbeAttachResult.Available` |
| ADR-003                  | Abort al shutdown             | `Shutdown` cancela turno activo vía terminal       |
| PRD FR-01                | Carga en `OnStartupComplete`  | Todo el arranque se hace ahí, no en `OnConnection` |
| PRD FR-01                | Tolerante a `COMException`    | `try/catch` en cada callback                       |
| PRD FR-01                | Tolerante a `VBE == null`     | `VbeAttachResult`                                  |
| PRD FR-05                | Menú Tools                    | `BlsVbeMenuManager.EnsureMenu` tras VBE            |
| skill bls-winforms-threading §1              | Una sola terminal             | Gestionada por `BlsNimTerminal.ShowOrFocus`        |
| skill bls-logging-errors §Captura global | Handlers al arranque          | `GlobalExceptionHandlers.Register`                 |
| skill bls-vbe-automation                 | `AttachVbe` antes de bridge   | Orden explícito en `AddinServiceContainer`         |

---

## 5. Flujo interno

### `OnConnection`
```
1. try
     // Nada de lógica pesada aquí. Access llama esto antes de que la
     // UI esté lista. Solo guardamos la referencia.
     _application = Application;
     _connectMode = ConnectMode;
2. catch (Exception ex)
     // No hay logger aún. No podemos loguear. Silencio.
     // No relanzar: Access interpretaría fallo y degradaría LoadBehavior.
```

### `OnStartupComplete`
Aquí ocurre **todo** el arranque. La UI de Access ya está lista.

```
1. try
     a. _addin = new BalansoftAddin()
     b. _addin.Start(_application)
     c. Log INFO "Add-in arrancado"
2. catch (COMException ex)
     // Logger puede no existir. Intentar loguear; si falla, silencio.
     TryLog("ERROR", "OnStartupComplete", "Fallo COM", ex)
     // No relanzar: Access seguiría con el add-in cargado a medias.
3. catch (Exception ex)
     TryLog("ERROR", "OnStartupComplete", "Fallo inesperado", ex)
```

### `BalansoftAddin.Start(application)`
```
1. if (_started) { Logger?.Warn("Addin", "Start llamado dos veces"); return; }
2. _started = true;

3. // 1. Logger (primero: todo lo demás lo usa)
   _logger = new BlsLogger(BlsPaths.LogsDir, BlsVersion.Current, debugEnabled);
   _logger.Info("Addin", $"Arranque v{BlsVersion.Current.Version} " +
                          $"commit={BlsVersion.Current.Commit}");

4. // 2. Handlers globales
   GlobalExceptionHandlers.Register(_logger);

5. // 3. Registrar (verificar, no escribir)
   var info = BlsAddinRegistrar.GetRegistrationInfo();
   _logger.Info("Registration",
       $"IsRegistered={info.IsRegistered} LoadBehavior={info.LoadBehavior}");
   if (!info.IsRegistered)
       _logger.Warn("Registration",
           "Add-in no registrado correctamente. Ejecuta RegisterAddin.ps1.");

6. // 4. UI Scheduler (necesario antes de tocar VBE)
   _uiScheduler = new WinFormsUiScheduler(CreateHiddenControl());

7. // 5. Servicios sin VBE
   _protector    = new DpapiDataProtector();
   _secureStore  = new BlsSecureStore(_protector, BlsPaths.ApiKeyFile, _logger);
   _chatHistory  = new BlsChatHistory(BlsPaths.HistoryDir, _logger);

8. // 6. VBE (tolerante a null)
   var attach = TryAttachVbe(application);
   if (attach.Available)
   {
       _vbe = attach.Vbe!;
       _access = new BlsAccessObjectService(application, _uiScheduler, _logger);
       _logger.Info("Addin", "VBE disponible");
   }
   else
   {
       _logger.Warn("Addin", $"VBE no disponible: {attach.Reason}");
       // Seguimos sin VBE. La terminal se abrirá degradada.
   }

9. // 7. HttpClient (único)
   _httpClient = new HttpClient(new HttpClientHandler {
       AutomaticDecompression = DecompressionMethods.All
   }) { Timeout = Timeout.InfiniteTimeSpan };

10. // 8. NIM Client
    _nimClient = new BlsNimClient(_httpClient, _secureStore, _logger);

11. // 9. Bridge (solo si VBE disponible)
    if (_vbe != null)
    {
        _bridge = new BlsVbeBridgeServer(_vbe, _access!, _uiScheduler, _logger);
        _bridge.Start();
        _logger.Info("Addin", "Bridge iniciado en \\\\.\\pipe\\balansoft-opencode");
    }

12. // 10. Menú Tools (solo si VBE disponible)
    if (_vbe != null)
    {
        _menuManager = new BlsVbeMenuManager(_vbe, _logger);
        _menuManager.EnsureMenu(() => OnNimTerminalRequested());
    }

13. _logger.Info("Addin", "Arranque completado");
```

**Puntos críticos:**
- El orden importa. Logger antes que todo. UI Scheduler antes que VBE.
  Bridge después de VBE.
- El menú Tools se instala **al final** para que el handler esté listo.
- Sin VBE, el bridge no se arranca y no hay menú. La terminal se puede
  abrir igual (útil para consultar sin VBE), pero el bridge fallará al
  invocar tools.

### `OnNimTerminalRequested` (handler del menú Tools)
```
1. try
     a. if (_terminal != null && !_terminal.IsDisposed)
        {
            _terminal.Activate();
            return;
        }
     b. owner = _vbe != null ? BlsCodeWindowManager.GetVbeHwnd(_vbe) : IntPtr.Zero
     c. _terminal = new BlsNimTerminal(
            nimClient:   _nimClient,
            bridgeClient: _bridge != null ? new BlsBridgeClient(...) : null,
            history:     _chatHistory,
            secureStore: _secureStore,
            vbe:         _vbe,       // puede ser null
            logger:      _logger);
     d. _terminal.FormClosed += (_, __) => _terminal = null;
     e. if (owner != IntPtr.Zero)
            _terminal.Show(new WindowWrapper(owner));
        else
            _terminal.Show();
2. catch (Exception ex)
     _logger.Error("Addin", "Fallo al abrir la terminal", ex);
     MessageBox.Show("No se pudo abrir la terminal NIM. Revisa el log.",
         "Balansoft Access VBA Agent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
```

### `OnDisconnection` / `OnBeginShutdown`
```
1. try
     a. _addin?.Shutdown(reason)
2. catch (Exception ex)
     // Logger puede haber sido dispuesto. Silencio o intento best-effort.
3. finally
     _addin = null;
     _application = null;
```

### `BalansoftAddin.Shutdown(reason)`
```
1. if (_shutdown) return;      // idempotente
2. _shutdown = true;
3. _logger?.Info("Addin", $"Shutdown ({reason})");

4. // 1. Terminal (cancela turno, flush historial)
   if (_terminal != null && !_terminal.IsDisposed)
   {
       _terminal.RequestCancelAndClose();     // ADR-003
       // Espera máx 2s dentro de RequestCancelAndClose
       _terminal = null;
   }

5. // 2. Menú Tools (quitar botón)
   _menuManager?.RemoveMenu();

6. // 3. Bridge (cancela loop, cierra pipe)
   if (_bridge != null)
   {
       try { _bridge.StopAsync(TimeSpan.FromSeconds(5)).Wait(TimeSpan.FromSeconds(5)); }
       catch (Exception ex) { _logger?.Warn("Addin", $"Bridge no paró limpio: {ex.Message}"); }
       _bridge.Dispose();
       _bridge = null;
   }

7. // 4. Historrial (flush final)
   try { _chatHistory?.FlushAsync(CancellationToken.None).Wait(TimeSpan.FromSeconds(2)); }
   catch { /* best-effort */ }

8. // 5. Handlers globales
   GlobalExceptionHandlers.Unregister();

9. // 6. Servicios
   _nimClient?.Dispose();
   _httpClient?.Dispose();
   _vbe = null;               // no dispose: es COM, lo libera el GC
   _access = null;
   _uiScheduler?.Dispose();

10. _logger?.Info("Addin", "Shutdown completado");
11. _logger?.Dispose();
12. _logger = null;
```

**Orden de parada (inverso al de arranque, con matices):**
- Terminal primero: puede estar usando el bridge.
- Menú: quitar botón antes de que VBE desaparezca.
- Bridge: ya no lo usa nadie.
- Historial: flush final.
- Handlers: quitar antes de disponer el logger.
- Servicios: cliente HTTP y NIM.
- Logger: último (todos lo usan).

### `TryAttachVbe`
```
try
{
    dynamic app = application;
    object vbe = app.VBE;                 // puede lanzar COMException
    if (vbe == null)
        return new VbeAttachResult {
            Available = false,
            Reason = "Application.VBE es null (modo runtime o ACCDE)" };

    var service = new BlsVbeService(vbe, _uiScheduler, _logger);
    return new VbeAttachResult { Available = true, Vbe = service };
}
catch (COMException ex)
{
    return new VbeAttachResult {
        Available = false,
        Reason = $"COMException 0x{ex.HResult:X8}: {ex.Message}" };
}
catch (Exception ex)
{
    return new VbeAttachResult {
        Available = false,
        Reason = $"{ex.GetType().Name}: {ex.Message}" };
}
```

---

## 6. Máquina de estados del add-in

```
        ┌──────────────┐
        │  New (ctor)  │
        └──────┬───────┘
               │ OnConnection
               ▼
        ┌──────────────┐
        │  Connected   │  ← Application guardado, sin servicios
        └──────┬───────┘
               │ OnStartupComplete
               ▼
        ┌──────────────┐
        │  Starting    │  ← Construcción de servicios en curso
        └──────┬───────┘
               │ éxito
               ▼
        ┌──────────────┐
        │   Running    │  ← Bridge up, menú instalado
        └──────┬───────┘
               │ OnDisconnection / OnBeginShutdown
               ▼
        ┌──────────────┐
        │  Stopping    │  ← Parada ordenada
        └──────┬───────┘
               │
               ▼
        ┌──────────────┐
        │   Stopped    │
        └──────────────┘

Fallo en Starting → se salta a Stopped con log ERROR.
```

Transiciones idempotentes:
- `Start` en `Running` → no-op + `WARN`.
- `Shutdown` en `Stopped` → no-op.
- `Shutdown` en `New` → permite cerrar limpio (libera nada).

---

## 7. Gestión de errores en el arranque

### Regla de oro
**`OnConnection` y `OnStartupComplete` nunca relanzan**. Un fallo se
loguea (si el logger ya existe) y el add-in queda en estado degradado.
Access sigue funcionando.

### Degradaciones aceptables
| Fallo                          | Comportamiento                                                            |
|--------------------------------|---------------------------------------------------------------------------|
| `VBE == null`                  | Add-in carga sin bridge ni menú. Terminal puede abrirse en modo limitado. |
| `GetRegistrationInfo` falla    | Se loguea `WARN`. Add-in sigue.                                           |
| `BlsLogger` no se puede crear  | Fallback a `NullLogger`. Add-in sigue sin logs.                           |
| `BlsSecureStore` falla         | Se loguea. La terminal pedirá API key al abrir.                           |
| `BlsChatHistory` falla         | Se loguea. La terminal arranca sin historial.                             |
| `Bridge.Start()` falla         | Se loguea. La terminal muestra error al invocar tools.                    |
| `HttpClient` no se puede crear | Raro. Se loguea. La terminal muestra error al enviar.                     |

### Degradaciones **no** aceptables (relanzar y dejar que Access degrade)
Ninguna. Todo se captura.

---

## 8. `WindowWrapper` (helper)

```csharp
internal sealed class WindowWrapper : IWin32Window
{
    public IntPtr Handle { get; }
    public WindowWrapper(IntPtr handle) { Handle = handle; }
}
```

Vive en `UI/` o `Helpers/`. Se usa para pasar el HWND del VBE como
owner de la terminal.

---

## 9. Criterios de aceptación

### CA-01 — Arranque básico
- [ ] `OnConnection` guarda `Application` sin lógica pesada.
- [ ] `OnStartupComplete` construye servicios en el orden del §5.
- [ ] El log de arranque incluye versión + commit + registro + VBE.
- **Tests:** `Connect_OnConnection_SoloGuardaApplication`,
  `Addin_Start_ConstruyeServiciosEnOrden`,
  `Addin_Start_LogIncluyeContexto`.

### CA-02 — Idempotencia
- [ ] `Start` dos veces → no duplica servicios.
- [ ] `Shutdown` dos veces → no relanza.
- [ ] `OnConnection` dos veces → no rompe.
- **Tests:** `Addin_StartDosVeces_NoDuplica`,
  `Addin_ShutdownDosVeces_NoRelanza`.

### CA-03 — Orden de arranque
- [ ] El observer registra el orden: logger → handlers → registrar →
      ui → store → history → vbe → access → http → nim → bridge → menú.
- **Test:** `Addin_Start_OrdenDeServiciosCorrecto` (con
  `IAddinLifecycleObserver`).

### CA-04 — Tolerancia a `VBE == null`
- [ ] `Application.VBE` null → add-in arranca sin bridge ni menú.
- [ ] Log `WARN` con razón.
- [ ] No lanza excepción.
- **Tests:** `Addin_StartVbeNull_ArrancaSinBridge`,
  `Addin_StartVbeNull_LogueaWarn`,
  `Addin_StartVbeNull_NoLanza`.

### CA-05 — Tolerancia a `COMException`
- [ ] `Application.VBE` que lanza → add-in arranca sin bridge.
- [ ] La razón incluye el HRESULT.
- **Tests:** `Addin_StartVbeComException_ArrancaSinBridge`,
  `Addin_StartVbeComException_LogIncluyeHresult`.

### CA-06 — Handlers globales
- [ ] `OnStartupComplete` registra los 3 handlers.
- [ ] `Shutdown` los desregistra.
- **Tests:** `Addin_Start_RegistraHandlers`,
  `Addin_Shutdown_DesregistraHandlers`.

### CA-07 — Bridge arranca solo con VBE
- [ ] `VbeAttachResult.Available` → `Bridge.Start()`.
- [ ] No disponible → bridge no se arranca, no se crea.
- **Tests:** `Addin_StartVbeOk_BridgeArrancado`,
  `Addin_StartVbeNull_BridgeNoCreado`.

### CA-08 — Menú Tools
- [ ] Con VBE → `EnsureMenu` llamado con el handler.
- [ ] Sin VBE → no se llama.
- **Tests:** `Addin_StartVbeOk_MenuInstalado`,
  `Addin_StartVbeNull_MenuNoInstalado`.

### CA-09 — Terminal on-demand
- [ ] El menú no abre terminal hasta que el usuario lo pide.
- [ ] `OnNimTerminalRequested` crea la terminal con las dependencias
      correctas.
- [ ] Segunda petición → foco, no duplica.
- **Tests:** `Addin_MenuClick_AbreTerminal`,
  `Addin_MenuClickDoble_ReusaTerminal`.

### CA-10 — Shutdown ordenado
- [ ] Orden de parada: terminal → menú → bridge → historial →
      handlers → servicios → logger.
- [ ] Timeouts respetados (bridge 5s, terminal 2s).
- [ ] Shutdown completa en < 8s en el peor caso.
- **Tests:** `Addin_Shutdown_OrdenDeParadaCorrecto`,
  `Addin_Shutdown_RespetaTimeouts`,
  `Addin_Shutdown_CompletaEnMenosDe8s`.

### CA-11 — Shutdown con turno activo
- [ ] Terminal con turno activo → `RequestCancelAndClose` cancela.
- [ ] No bloquea más de 2s.
- **Tests:** `Addin_ShutdownConTurnoActivo_CancelaYEspera`,
  `Addin_ShutdownConTurnoActivo_NoBloqueaMasDe2s`.

### CA-12 — Shutdown sin VBE
- [ ] Sin VBE → no se llama a `RemoveMenu` ni a `Bridge.StopAsync`.
- [ ] Shutdown completa sin errores.
- **Test:** `Addin_ShutdownSinVbe_CompletaSinErrores`.

### CA-13 — `OnDisconnection` tolerante
- [ ] `OnDisconnection` nunca relanza.
- [ ] Aunque `Shutdown` falle, `_addin` se pone a null.
- **Tests:** `Connect_OnDisconnection_NoRelanza`,
  `Connect_OnDisconnectionShutdownFalla_Sigue`.

### CA-14 — `OnAddInsUpdate`
- [ ] No hace nada (no-op).
- **Test:** `Connect_OnAddInsUpdate_NoOp`.

### CA-15 — Diagnóstico
- [ ] `Addin.Logger` accesible para tests.
- [ ] `Addin.IsRunning` refleja el estado.
- **Tests:** `Addin_IsRunning_TrueTrasStart`,
  `Addin_IsRunning_FalseTrasShutdown`.

---

## 10. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── Core/
│   ├── BalansoftConnect.cs                       (nuevo)
│   ├── BalansoftAddin.cs                         (nuevo)
│   ├── AddinServiceContainer.cs                  (nuevo, internal)
│   ├── VbeAttachResult.cs                        (nuevo, internal)
│   ├── ShutdownReason.cs                         (nuevo, internal)
│   ├── IAddinLifecycleObserver.cs                (nuevo, internal)
│   ├── NullLifecycleObserver.cs                  (nuevo, internal)
│   ├── NullLogger.cs                             (nuevo, internal)
│   └── GlobalExceptionHandlers.cs                (ya existe del #7)
├── UI/
│   ├── WinFormsUiScheduler.cs                    (nuevo)
│   └── WindowWrapper.cs                          (nuevo, internal)
└── Helpers/
    └── (sin cambios)

tests/Balansoft.AIAddin.Tests/
├── Core/
│   ├── BalansoftConnectTests.cs                  (nuevo)
│   ├── BalansoftAddinTests.cs                    (nuevo)
│   └── AddinServiceContainerTests.cs             (nuevo)
└── Fakes/
    ├── FakeLifecycleObserver.cs                  (nuevo)
    ├── FakeVbe.cs                                (nuevo)
    ├── FakeAccessApplication.cs                  (nuevo)
    └── FakeBridgeServer.cs                       (nuevo)
```

---

## 11. Decisiones internas (no requieren ADR)

### 11.1 Lógica pesada en `OnStartupComplete`, no en `OnConnection`
`OnConnection` es llamado por Access **antes** de que la UI esté lista.
Hacer mucho ahí degrada `LoadBehavior`. Todo el arranque va en
`OnStartupComplete`.

### 11.2 `BalansoftAddin` separado de `BalansoftConnect`
`BalansoftConnect` es COM y no se puede testear sin Access.
`BalansoftAddin` es POCO y se testea con fakes. El connect solo
delega.

### 11.3 Sin DI framework
Contenedor explícito (`AddinServiceContainer.Create`). Para ~10
servicios, la reflexión y el escaneo de ensamblados son overkill.

### 11.4 `IUiScheduler` con `Control` oculto
`WinFormsUiScheduler` crea un `Control` en el hilo UI (donde corre
`OnStartupComplete`). Ese control sirve de marshaling anchor para todo
el add-in. `Dispose` libera el control.

**Nota:** el control se crea **sin** `Handle` explícito; el primer
`Invoke` lo fuerza. Es el patrón estándar.

### 11.5 `HttpClient` creado una vez
`Timeout = InfiniteTimeSpan` (los timeouts por chunk los gestiona
`BlsNimClient`). `AutomaticDecompression.All`.

### 11.6 Terminal no se crea en `Start`, solo el handler
La terminal se crea on-demand (menú Tools). Esto evita que un fallo en
la UI rompa el arranque.

### 11.7 `_terminal` como campo del Addin, no de `BalansoftConnect`
El connect no conoce la terminal. El addin sí, porque la crea y la
destruye.

### 11.8 `TryAttachVbe` captura todo
`COMException` y `Exception` se capturan y se devuelven como
`VbeAttachResult`. Nunca relanza. Un VBE roto no debe impedir que
Access arranque.

### 11.9 Orden de shutdown: terminal primero
La terminal puede tener un turno activo usando el bridge y el NIM
client. Se cancela y se cierra **antes** de tocar el bridge.

### 11.10 `RequestCancelAndClose` con timeout interno
La terminal implementa el timeout de 2s internamente. `BalansoftAddin`
no se preocupa por eso.

### 11.11 Bridge `StopAsync` con timeout externo
`BalansoftAddin` espera 5s. Si el bridge no para, se loguea `WARN` y se
sigue. Un pipe huérfano lo libera el OS al morir el proceso.

### 11.12 Logger se dispone **al final**
Todos los servicios loguean durante su dispose. El logger debe vivir
hasta el último momento.

### 11.13 `NullLogger` como fallback
Si `BlsLogger` no se puede crear (raro: `%APPDATA%` sin permisos), se
usa `NullLogger` (implementación no-op). El add-in arranca sin logs.

### 11.14 `OnBeginShutdown` es solo informativo
Access puede llamarlo antes de `OnDisconnection`. No hacemos nada
especial; logueamos y ya. El shutdown real ocurre en `OnDisconnection`.

### 11.15 `OnAddInsUpdate` es no-op
Access lo llama cuando cambia la lista de add-ins. No nos afecta.

### 11.16 `_started` / `_shutdown` banderas en `BalansoftAddin`
Idempotencia explícita. Sin ellas, dos `Start` duplicarían el menú
Tools y el bridge.

### 11.17 `IAddinLifecycleObserver` opcional
En producción se usa `NullLifecycleObserver` (no-op). En tests, un
`FakeLifecycleObserver` que graba el orden. Permite CA-03 y CA-10.

---

## 12. Fuera de alcance (recordatorio)

- Recarga sin reiniciar Access.
- Múltiples conexiones COM.
- Access x64.
- Word/Excel.
- Instalación por ClickOnce/MSI.
- Auto-actualización.
- Modo "sin VBE" con funcionalidad completa.
- Panel de configuración post-arranque.
- Auto-rotación de API key.
- Persistencia de estado del add-in entre sesiones (más allá del
  historial y la API key).

---

## 13. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (reglas 1, 2, 3, 5).
2. `.opencode/skills/bls-project-context/SKILL.md`.
3. `.opencode/skills/bls-com-late-binding/SKILL.md` (`Application` como
   `dynamic`).
4. `.opencode/skills/bls-vbe-automation/SKILL.md` (`AttachVbe`, menú Tools).
5. `.opencode/skills/bls-winforms-threading/SKILL.md` (UI scheduler, cierre
   limpio).
6. `.opencode/skills/bls-registration-scripts/SKILL.md` (verificación en
   runtime).
7. `.opencode/skills/bls-logging-errors/SKILL.md` (log de arranque, handlers
   globales).
8. `.opencode/skills/bls-testing-strategy/SKILL.md` (fakes, observer).
9. `docs/design/ADR-003-abort-model.md` (shutdown con turno activo).

Y los 7 docs de diseño previos:
10. `docs/design/20250101-vbe-bridge.md`.
11. `docs/design/20250102-nim-client.md`.
12. `docs/design/20250103-nim-terminal.md`.
13. `docs/design/20250104-secure-store.md`.
14. `docs/design/20250106-registration-scripts.md`.
15. `docs/design/20250107-logging-version.md`.

---

## 14. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Connect"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Addin"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~ServiceContainer"

# Verificación manual (checklist en docs/manual-tests.md)
# 1. Registrar add-in
# 2. Abrir Access 2016 x86
# 3. Comprobar log de arranque
# 4. Abrir Tools -> NIM Terminal
# 5. Enviar prompt
# 6. Cerrar Access
# 7. Verificar log de shutdown
```

Criterio de éxito: build verde + tests de CA-01..CA-15 verdes +
checklist manual firmada.

---

## 15. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §9 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §13 están listados.
- [ ] Los 6 docs de diseño previos están mergeados (dependencias).
- [ ] `docs/manual-tests.md` está escrito (checklist de verificación
      manual en Access real).

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-15 (los automatizables).
- [ ] No modificar interfaces del §3 ni de los docs previos.
- [ ] Referenciar este doc en el commit:
      `feat: add-in lifecycle (refs design/20250108-addin-lifecycle.md)`.
- [ ] Incluir el checklist manual firmado para CA-01, CA-04, CA-10,
      CA-11 (integración con Access real).

---

## 16. Referencias

- `AGENTS.md` reglas 1, 2, 3, 5.
- `PRD.md` §3.1, §7 FR-01, FR-05, §11.
- `docs/design/ADR-002-pipe-framing.md`.
- `docs/design/ADR-003-abort-model.md`.
- `docs/design/20250101-vbe-bridge.md`.
- `docs/design/20250102-nim-client.md`.
- `docs/design/20250103-nim-terminal.md`.
- `docs/design/20250104-secure-store.md`.
- `docs/design/20250106-registration-scripts.md`.
- `docs/design/20250107-logging-version.md`.
- `.opencode/skills/bls-com-late-binding/SKILL.md`.
- `.opencode/skills/bls-vbe-automation/SKILL.md`.
- `.opencode/skills/bls-winforms-threading/SKILL.md`.
- `.opencode/skills/bls-registration-scripts/SKILL.md`.
- `.opencode/skills/bls-logging-errors/SKILL.md`.
- `.opencode/skills/bls-testing-strategy/SKILL.md`.
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa el lifecycle (bloqueado hasta que #1..#7 estén mergeados)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. docs/design/20250108-addin-lifecycle.md (el diseño)
4. Los skills citados en §13 del diseño
5. Los 6 docs de diseño previos citados en §16

Implementa la feature 'Add-in Lifecycle' exactamente como está
especificado.
Incluye:
- BalansoftConnect (IDTExtensibility2)
- BalansoftAddin (orquestador POCO)
- AddinServiceContainer (orden de construcción)
- VbeAttachResult, IAddinLifecycleObserver, NullLogger
- WinFormsUiScheduler, WindowWrapper

No cambies interfaces del §3. No añadas features del §12.
Entrega diff completo + tests + comando de verificación del §14."
```

### Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/bls-workflow-nemotron/SKILL.md (checklist de rechazo)
3. docs/design/20250108-addin-lifecycle.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill bls-workflow-nemotron.
Presta especial atención a:
- OnConnection sin lógica pesada (FR-01).
- Tolerancia a VBE == null y COMException (FR-01).
- Orden de arranque (§5).
- Orden de parada inverso, con timeouts (§5, CA-10).
- Handlers globales registrados y desregistrados (§7).
- Idempotencia de Start/Shutdown (CA-02).
- Bridge solo arranca si VBE disponible (CA-07).
- Logger se dispone al final (§11.12).

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño — **COMPLETO**

| # | Doc                                | Estado            | Depende de |
|---|------------------------------------|-------------------|------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado       | —          |
| 2 | `20250102-nim-client.md`           | ✅ Entregado       | —          |
| 3 | `20250103-nim-terminal.md`         | ✅ Entregado       | #1, #2, #4 |
| 4 | `20250104-secure-store.md`         | ✅ Entregado       | ADR-001    |
| 5 | `20250105-chat-history.md`         | ⚠️ Fusionado en #4 | —          |
| 6 | `20250106-registration-scripts.md` | ✅ Entregado       | #7         |
| 7 | `20250107-logging-version.md`      | ✅ Entregado       | #4         |
| 8 | `20250108-addin-lifecycle.md`      | ✅ Entregado       | Todos      |

**Ruta crítica del MVP (completa):**
```
#1 bridge ─┐
           ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client ─┘       ↑
                   │
#4 store ──────────┤ (alimenta #2, #3, #7)
#6 reg ────────────┤ (alimenta #8)
#7 log ────────────┘ (transversal)
```

**Todos los frentes pueden avanzar en paralelo** una vez que el
scaffolding esté listo:

- **Frente A:** #1 bridge
- **Frente B:** #2 client
- **Frente C:** #4 store + history
- **Frente D:** #6 reg + #7 log

Luego:
- **#3 terminal** (necesita A, B, C)
- **#8 lifecycle** (integra todo)

---

## Cierre del backlog de diseño

Con este doc quedan cubiertos:

- **7 docs de diseño** (uno por feature, con #5 fusionado en #4).
- **4 ADRs** vigentes (identidad, framing, abort, object_detail).
- **11 skills** (00 contexto, 01–10 dominio).
- **1 AGENTS.md** con las reglas duras.
- **~150 tests** distribuidos en los CA de cada doc (los 79 del MVP
  más los añadidos en cada feature).
- **Cobertura 100% del PRD** (§3.1–§3.6, §7 FR-01–FR-12, §11 criterios
  1–10).
