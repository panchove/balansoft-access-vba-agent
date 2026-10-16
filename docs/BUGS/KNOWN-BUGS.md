---
version: 1.1.0
date: 2026-10-15
status: active
iso_standard: [12207, 25010]
aligns_to: [PRD.md, CONSTITUTION.md, MANUAL-TESTS.md]
tags: [bugs, quality, iso-12207, problem-resolution, square]
---

# Registro de Bugs Conocidos - Balansoft Access VBA Agent

## Versión: 1.1.0
## Fecha: 2026-10-15
## Estado: activo
## Norma de referencia: ISO/IEC 12207:2017 (Proceso de Resolución de Problemas) / ISO/IEC 25010:2023

## Política de documentación de bugs
- Todos los bugs deben registrarse en este archivo antes de crear un issue
- Cada bug debe tener: ID, descripción, gravedad, área afectada, workaround si existe
- Los bugs críticos bloquean el release; los menores se acumulan para la próxima versión
- Los bugs duplicados deben marcarse como tale

---

## Bugs Registrados

### Bug #1: BalanceSoft-001
**Título**: Add-in no carga en Access modo runtime (.accde)
**Gravedad**: Critical
**Área**: Registration | COM loading
**Fecha**: 2025-10-10
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
El add-in falla al cargarse cuando Access está en modo runtime (.accde) porque `Application.VBE` es nulo. El código actual asume que VBE siempre está disponible después de `OnConnection`.

**Impacto**: El usuario no puede usar la terminal NIM en archivos .accde.

**Workaround**: 
- Verificar `IsAvailable` property antes de cualquier operación VBE
- Mostrar mensaje informativo: "Terminal NIM no disponible en modo runtime"

**Fecha de corrección estimada**: Próxima versión v1.1.0

**Referencias**: 
- ADR-001 (Identidad de proyecto)
- .opencode/skills/bls-vbe-automation/SKILL.md
- AGENTS.md regla 4

---

### Bug #2: BalanceSoft-002
**Título**: Memory leak en streaming largo de NIM
**Gravedad**: Medium
**Área**: UI | NimClient | streaming
**Fecha**: 2025-10-12
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
Durante streams de NIM prolongados (>30 minutos), el consumo de memoria del add-in aumenta progresivamente sin liberarse adecuadamente. Se sospecha que chunks SSE no se están liberando de memoria después de procesarlos.

**Impacto**: El add-in puede consumir toda la memoria disponible y causar lentitud o cierre de Access.

**Workaround**: 
- Reiniciar el add-in periódicamente
- Limitar la duración de los prompts a 20 minutos

**Fecha de corrección estimada**: v1.2.0

**Referencias**: 
- .opencode/skills/bls-nim-client-streaming/SKILL.md
- AGENTS.md regla 5 (errores reales)

---

### Bug #3: BalanceSoft-003
**Título**: Timeout incorrecto en chunks de tool_call
**Gravedad**: High
**Área**: NIM | streaming | tool_calls
**Fecha**: 2025-10-13
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
El timeout de 120 segundos por chunk se reinicia incorrectamente cuando hay múltiples tool_calls fragmentados. Si el modelo envía tool_calls en 5 chunks separados, el timeout se reinicia 5 veces en lugar de ser global para toda la operación.

**Impacto**: Los tool_calls pueden timeout injustificadamente o no timeout cuando deberían.

**Workaround**: 
- Monitorear los logs en busca de códigos -32002
- Reenviar el prompt si se detecta timeout indebido

**Fecha de corrección estimada**: v1.1.1

**Referencias**: 
- .opencode/skills/bls-nim-client-streaming/SKILL.md
- ADR-003 (Abort model)
- AGENTS.md regla 5

---

### Bug #4: BalanceSoft-004
**Título**: MSACCESS.EXE no cierra su proceso y queda zombi tras abrir la terminal
**Gravedad**: Critical
**Área**: Core | Bridge | Lifecycle | WinForms
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
Al cerrar Access después de abrir la terminal NIM, el proceso `MSACCESS.EXE` continuaba en ejecución en segundo plano. En .NET Framework 4.8, `NamedPipeServerStream.WaitForConnectionAsync` ignora el `CancellationToken` a menos que se cierre la instancia del pipe explícitamente (`_currentListeningPipe?.Close()`). Además, las referencias COM (RCWs) y la instancia del formulario WinForms no se liberaban formalmente en `OnDisconnection`.

**Impacto**: Procesos huérfanos de Access consumiendo memoria y bloqueando archivos de base de datos.

**Corrección**:
- Se añadió tracking del pipe activo en `BlsVbeBridgeServer` y se invoca `_currentListeningPipe?.Close()` en `StopAsync()`.
- Se añadieron `_terminal?.Close()`, nulificación de todas las referencias de servicios y COM, y llamadas explícitas a `GC.Collect()` / `GC.WaitForPendingFinalizers()` en `OnDisconnection` de `AddinConnect`.

---

### Bug #5: BalanceSoft-005
**Título**: Formulario de la terminal NIM invisible / no se muestra
**Gravedad**: High
**Área**: UI | WinForms | HWND Owner
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
La conversión del HWND del VBE en `BlsComHelper.GetVbeHwnd` fallaba por casteo incorrecto en Office 32 bits, devolviendo `IntPtr.Zero`. `AddinConnect` abortaba la apertura si el HWND era cero. Además, `ShowInTaskbar` estaba en `false`, `StartPosition` en `CenterParent` y no tenía tamaño inicial definido, quedando oculta detrás de la ventana principal de Access.

**Impacto**: El usuario hacía clic en Herramientas -> NIM Terminal y no aparecía ninguna ventana.

**Corrección**:
- Se corrigió `GetVbeHwnd` usando `Convert.ToInt32(rawHwnd)` de forma segura.
- `AddinConnect` ya no aborta si el HWND es cero.
- Se configuró `ShowInTaskbar = true`, `StartPosition = CenterScreen`, `Size = (800, 600)`, y se invocan `BringToFront()` y `Activate()`.

---

### Bug #6: BalanceSoft-006
**Título**: Layout de BlsNimTerminal aplastado verticalmente (caja de texto y botones invisibles)
**Gravedad**: High
**Área**: UI | WinForms | SplitContainer
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
El `SplitContainer` principal tenía `Orientation = Orientation.Vertical` en vez de `Horizontal`. En WinForms, `Orientation.Vertical` divide en Izquierda y Derecha. La lista de conversación ocupaba todo el ancho disponible y el panel inferior (TextBox de entrada, botones y barra de estado) quedó confinado a una franja de pocos píxeles en el borde derecho.

**Impacto**: El usuario sólo veía una lista vacía con dos columnas ("Rol" y "Mensaje") sin lugar para escribir ni enviar mensajes.

**Corrección**:
- Se cambió la orientación a `Orientation.Horizontal` con `FixedPanel = Panel2` (110 px mín).
- Se configuró `_input` con `Dock = Fill` y los botones en panel inferior.
- Se añadió resize dinámico a la columna "Mensaje" del `ListView`.

---

### Bug #7: BalanceSoft-007
**Título**: FileNotFoundException al cargar System.Runtime.CompilerServices.Unsafe en host COM
**Gravedad**: Critical
**Área**: Interop | Assembly Resolution | CLR Fusion
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
Al ejecutarse como COM Add-in dentro de `MSACCESS.EXE`, el directorio base del AppDomain apunta a `C:\Program Files (x86)\Microsoft Office\Office14\`. Cuando el add-in utiliza dependencias de NuGet (`System.Runtime.CompilerServices.Unsafe`, `System.Text.Json`, `System.Memory`), el CLR falla al buscarlas en el directorio de Office y arroja `FileNotFoundException`.

**Impacto**: Diálogo de excepción no controlada al inicializar `BlsNimTerminal`.

**Corrección**:
- Se implementó `AppDomain.CurrentDomain.AssemblyResolve` en el constructor estático de `AddinConnect` y `BlsNimTerminal` para cargar ensamblados automáticamente desde el directorio de la DLL del add-in.

---

### Bug #8: BalanceSoft-008
**Título**: KeyNotFoundException en RunTurnAsync al leer "name" en project_summary
**Gravedad**: High
**Área**: UI | NimTerminal | JSON-RPC
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
En `RunTurnAsync`, se ejecutaba `project_summary` y se invocaba `projectSummary.GetProperty("name")`. Al serializarse en PascalCase (`"Name"`), la llamada case-sensitive arrojaba `KeyNotFoundException`. La llamada era además innecesaria en ese punto ya que `SystemPromptBuilder` no consumía esos datos y el modelo invoca la tool si la necesita. Al ser `async void`, la excepción generaba un diálogo JIT no controlado.

**Impacto**: Diálogo de error crítico al presionar Enviar.

**Corrección**:
- Se eliminó la llamada redundante y frágil a `project_summary`.
- Se envolvió `RunTurnAsync` y `OnSendClick` en bloques `try / catch` completos para capturar cualquier fallo y mostrarlo limpiamente en la conversación con `AppendError`.

---

### Bug #9: BalanceSoft-009
**Título**: Error al enviar la solicitud (HttpRequestException / TLS en .NET Framework 4.8)
**Gravedad**: High
**Área**: NIM Client | HTTPS | TLS
**Fecha**: 2026-10-15
**Reporte por**: Usuario
**Estado**: Fixed in v0.1.8

**Descripción**:
En procesos host de Office 32-bit (.NET Framework 4.8), `ServicePointManager.SecurityProtocol` no tiene TLS 1.2 activado por defecto si el host se ejecuta en compatibilidad heredada, provocando `HttpRequestException` al conectarse a `https://integrate.api.nvidia.com/...`. Además, `BlsNimClient` capturaba la excepción y emitía el mensaje genérico "Error interno del cliente NIM" ocultando la excepción interna (`InnerException`).

**Impacto**: Falla en el envío de prompts con mensaje de error opaco.

**Corrección**:
- Se activó explícitamente `SecurityProtocolType.Tls12 | Tls13` en `BlsNimClient`.
- Se mejoró el formato de error para reportar la `InnerException` en el mensaje al usuario y registrar la excepción completa en `BlsLogger`.

---

## Próximo Bug ID disponible: BalanceSoft-010

### Política de cierre de bugs
1. Cuando se corrige un bug, se añade la fecha de corrección
2. Se marca el estado como "Fixed in vX.Y.Z"
3. Se añade a la sección "Bugs Resueltos" del CHANGELOG.md
4. El bug queda en el registro histórico pero ya no es "abierto"
### Próxima revisión de bugs
- Fecha: 2025-11-15
- Responsable: bls-documentador
- Verificar: todos los bugs abiertos desde v1.0.0