s---
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
- AGENTS.md
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
- AGENTS.md
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
- AGENTS.md
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

#
## Próximo Bug ID disponible: BalanceSoft-017

### Política de cierre de bugs
1. Cuando se corrige un bug, se añade la fecha de corrección
2. Se marca el estado como "Fixed in vX.Y.Z"
3. Se añade a la sección "Bugs Resueltos" del CHANGELOG.md
4. El bug queda en el registro histórico pero ya no es "abierto"
- Fecha: 2026-11-22 (revision tras auditoria de gobernanza)
- Fecha: 2025-11-15
- Responsable: propietario del producto (agente unico bls-architect)
- Verificar: todos los bugs abiertos desde v1.0.0

---

## Estado de bugs abiertos (revision 2026-11-22)

Los tres bugs historicos siguen **abiertos**; la Fase 0 no los cerro porque no se han implementado las correcciones:

| ID | Gravedad | Estado | Motivo de permanencia |
|----|----------|--------|-----------------------|
| BalanceSoft-001 | Critical | Abierto | `Application.VBE` es null en modo runtime `.accde`; requiere discriminacion de `IsAvailable` en el arranque (Fase 1). |
| BalanceSoft-002 | Medium | Abierto | Posible retencion de memoria en streams SSE largos; requiere perfilado en Access real. |
| BalanceSoft-003 | High | Abierto | Timeout por chunk que se reinicia con multiples tool_calls; requiere timeout global de turno (Fase 4). |

**Nota de gobernanza**: los 12 riesgos R-01..R-12 del registro de IA siguen cerrados. Los bugs de esta tabla son de producto, no de gobernanza, y se registran aqui para evitar que el cierre de riesgos se interprete como "sin incidencias abiertas".
### Bug #14: BalanceSoft-014
**Titulo**: HITL y kill switch eludibles mediante cliente directo del pipe
**Gravedad**: Critical
**Area**: Core/Protocol | Named Pipe | Frontera de seguridad
**Fecha**: 2026-11-22
**Reporte por**: Auditoria de gobernanza (asistida por IA)
**Estado**: Fixed in v1.1.0

**Descripcion**:
`BlsSafetyGate.EnsureAllowed` y `BlsHumanApprovalGate.Evaluate` solo se invocaban desde `BlsNimTerminal`. `JsonRpcDispatcher` no las conocia, de modo que un frame JSON-RPC directo al pipe podia ejecutar `delete_module`, `execute_sql` o `migrate_*` sin aprobacion ni kill switch. La ACL por defecto del pipe tampoco estaba restringida al usuario actual.

**Correccion**: token de aprobacion de un solo uso (`BlsToolAuthorizationRegistry`), comprobaciones de fail-safe/HITL/limite de iteraciones en el dispatcher y ACL por SID en `BlsVbeBridgeServer.BuildPipeSecurity`. El next bug ID pasa a BalanceSoft-015.
### Bug #15: BalanceSoft-015
**Titulo**: Cadena de auditoria no verificada en ejecucion y reiniciada cada dia
**Gravedad**: High
**Area**: Helpers/BlsAuditChain, Helpers/BlsLogger, AddinConnect
**Fecha**: 2026-11-22
**Reporte por**: Auditoria de gobernanza (asistida por IA)
**Estado**: Fixed in v1.1.0

**Descripcion**: `BlsAuditChain.Verify` solo se usaba en tests; cada fichero diario empezaba con `GENESIS`, por lo que borrar un dia completo era indetectable. Ademas `BlsLogger` silenciaba los fallos de escritura con un `catch { }` y ADR-005 tenia solo precondiciones.

**Correccion**: `VerifyDirectory` + `SeedFor` encadenan los dias; `AddinConnect` verifica al arranque y dispara el kill switch si la cadena esta rota; `LastWriteError` expone el fallo; se anaden postcondiciones e invariantes. El next bug ID pasa a BalanceSoft-016.
### Bug #16: BalanceSoft-016
**Titulo**: Skill de workflow con orquestacion de modelos retirada
**Gravedad**: Medium
**Area**: Documentacion | .opencode/skills
**Fecha**: 2026-11-22
**Reporte por**: Auditoria de gobernanza (asistida por IA)
**Estado**: Fixed in v1.1.0

**Descripcion**: once skills con 2.879 LOC duplicaban ADRs y specs. `bls-workflow-nemotron` describia handoffs con `bls-implementer` y un modelo de dos agentes que F1 habia retirado, contradiciendo el agente unico y generando deriva documental.

**Correccion**: eliminados siete skills; los cuatro restantes son canonicos. `skills_index` y la lista de documentos obligatorios de CI actualizados; referencias historicas redirigidas a AGENTS.md. Next bug ID: BalanceSoft-017.
