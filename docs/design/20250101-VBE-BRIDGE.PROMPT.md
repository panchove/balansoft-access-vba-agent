---
version: 1.0.0
date: 2025-01-01
status: approved
iso_standard: [12207, 29148]
aligns_to: [20250101-VBE-BRIDGE.md, AGENTS.md]
tags: [prompt, implementer, instructions, vbe-bridge]
---

Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

CONTEXTO
========
Proyecto: COM Add-in para Access 2010+ (x86) que integra Nvidia NIM en el VBE.
Stack: C# .NET Framework 4.8, LangVersion 10, Nullable disable, x86 only, sin PIAs, enlace tardío vía dynamic aislado.
Feature a implementar: VBE Bridge (JSON-RPC 2.0 sobre Named Pipe).
Estado del repo: scaffolding completo, sin código de implementación aún.

LECTURA OBLIGATORIA (en este orden)
===================================
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. .opencode/skills/bls-com-late-binding/SKILL.md
4. .opencode/skills/bls-vbe-automation/SKILL.md
5. .opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md
6. .opencode/skills/bls-logging-errors/SKILL.md
7. .opencode/skills/bls-testing-strategy/SKILL.md
8. docs/design/ADR-002-pipe-framing.md
9. docs/design/ADR-003-abort-model.md
10. docs/design/ADR-004-object-detail.md
11. docs/design/20250101-vbe-bridge.md  ← EL DISEÑO

REGLAS DURAS (AGENTS.md)
========================
- NUNCA añadir PIAs de Office (Microsoft.Office.Interop.*).
- NUNCA usar Marshal.GetActiveObject ni ROT.
- 'dynamic' SOLO en Helpers/BlsComHelper.cs.
- Todo acceso a VBE/Access desde hilos no-UI pasa por IUiScheduler.
- API key SOLO en DPAPI; nunca en logs, JSON, excepciones ni registro.
- Errores reales con categoría + mensaje original; PROHIBIDO devolver éxito falso.
- PlatformTarget=x86, TreatWarningsAsErrors=true.
- Framing del pipe: [uint32 LE length][UTF-8 JSON], máx 1 MB (ADR-002).
- Abort: un CancellationTokenSource por turno de usuario (ADR-003).

LO QUE DEBES IMPLEMENTAR
========================
Del doc docs/design/20250101-vbe-bridge.md:

Interfaces (§3):
- IBlsBridgeServer (Core/): Start(), StopAsync(TimeSpan), IsRunning, Dispose.
- JsonRpcRequest, JsonRpcResponse, JsonRpcError, JsonRpcErrorData (Core/Protocol/).
- JsonRpcMethods (Core/Protocol/): constantes de los 10 métodos.
- JsonRpcErrorCodes (Core/Protocol/): -32600..-32603, -32000..-32004.
- BlsPipeFraming (Core/Protocol/): ReadFrameAsync, WriteFrameAsync, MaxFrameSize=1MB.
- BlsVbeBridgeServer (Core/): implementación de IBlsBridgeServer.
- BlsFrameTooLargeException, BlsFrameIncompleteException (Core/Protocol/).
- JsonRpcDispatcher (Core/Protocol/): dispatch de los 10 métodos.

Los 10 métodos del bridge (§6):
VBE (7):   list_modules, read_module, write_module, create_module,
           delete_module, get_selection, get_active_module
Access (3): project_summary, list_objects, object_detail

Interfaces consumidas (solo declarar, no implementar):
- IBlsVbe (VbeIntegration/): ListModules, ReadModule, WriteModule,
  CreateModule, DeleteModule, GetSelection, GetActiveModule,
  GetProjectKey, IsAvailable.
- IBlsAccess (AccessIntegration/): ProjectSummary, ListObjects,
  ObjectDetail.
- IUiScheduler (UI/): Run<T>, RunAsync<T>.
- IBlsLogger (Helpers/): Debug, Info, Warn, Error.

CONTROL DE ALCANCE
==================
INCLUIDO:
- Servidor NamedPipeServerStream con framing (ADR-002).
- Whitelist de 10 métodos + validación de params por método.
- Dispatch serializado al hilo UI (SemaphoreSlim(1,1) + IUiScheduler).
- Propagación de CancellationToken del turno (ADR-003).
- Códigos de error reservados (-32600..-32604, -32000..-32004).
- Ciclo de vida: Start/StopAsync con timeouts.
- Un solo cliente concurrente (-32004 busy).
- Tolerancia a frame_too_large (-32001 + cierre).
- Tolerancia a frame_incomplete (-32003 + cierre).
- Tests unitarios sin Office.

EXCLUIDO (NO implementar):
- BlsVbeService, BlsAccessObjectService, BlsLogger,
  WinFormsUiScheduler, BlsComHelper, DpapiDataProtector,
  BlsSecureStore, BlsNimClient, BlsNimTerminal.
- Batching JSON-RPC, notificaciones, múltiples clientes,
  compresión, cifrado del pipe.

ESTRUCTURA DE ARCHIVOS A CREAR
==============================
src/Balansoft.AIAddin.Access/
├── Core/
│   ├── IBlsBridgeServer.cs
│   ├── BlsVbeBridgeServer.cs
│   └── Protocol/
│       ├── JsonRpcRequest.cs
│       ├── JsonRpcResponse.cs
│       ├── JsonRpcError.cs
│       ├── JsonRpcErrorData.cs
│       ├── JsonRpcMethods.cs
│       ├── JsonRpcErrorCodes.cs
│       ├── JsonRpcDispatcher.cs
│       ├── BlsPipeFraming.cs
│       ├── BlsFrameTooLargeException.cs
│       └── BlsFrameIncompleteException.cs
├── VbeIntegration/IBlsVbe.cs
├── AccessIntegration/IBlsAccess.cs
├── UI/IUiScheduler.cs
└── Helpers/IBlsLogger.cs

tests/Balansoft.AIAddin.Tests/
├── Core/Protocol/
│   ├── BlsPipeFramingTests.cs
│   ├── JsonRpcDeserializationTests.cs
│   └── JsonRpcDispatcherTests.cs
├── Core/BlsVbeBridgeServerTests.cs
└── Fakes/
    ├── MockVbe.cs
    ├── FakeAccess.cs
    ├── TestUiScheduler.cs
    ├── FakeLogger.cs
    └── FakePipeServer.cs

TESTS OBLIGATORIOS
==================
Framing (ADR-002):
Frame_EnvioSimple_LeeMismoPayload
Frame_PayloadConNewlines_NoRompeFraming
Frame_PayloadUtf8Multibyte_LengthEsEnBytes
Frame_Excede1MB_CierraConError
Frame_HeaderParcial_EsperaResto
Frame_PayloadParcial_EsperaResto
Frame_Vacio_RespondeInvalidRequest
Frame_JsonInvalido_RespondeInvalidRequest
Frame_SegundoCliente_RespondeBusy
Frame_TimeoutInactivo_MitadDeFrame_Cierra
Frame_TimeoutIdle_EntreFrames_NoCierra

JSON-RPC:
Bridge_MetodoDesconocido_RespondeMethodNotFound
Bridge_ParamsInvalidos_RespondeInvalidParams
Bridge_ExcepcionNoControlada_RespondeInternalError

10 métodos:
Bridge_ListModules_DevuelveLista
Bridge_ReadModule_DevuelveCodigo
Bridge_WriteModule_EscribeEnVbe
Bridge_CreateModule_CreaEnVbe
Bridge_DeleteModule_ConConfirm_Borra
Bridge_DeleteModule_SinConfirm_RespondeInvalidParams
Bridge_GetSelection_DevuelveRango
Bridge_GetActiveModule_DevuelveInfo
Bridge_ProjectSummary_DevuelveCounts
Bridge_ListObjects_DevuelveCategorias
Bridge_ObjectDetail_DevuelveCampos

Marshaling:
Bridge_HandlerVbe_PasaPorUiScheduler

Cancelación (ADR-003):
Turno_CanceladoAntesDeTool_NoEjecutaTool
Turno_CanceladoDuranteToolVbe_EsperaTerminarYDescarta

Ciclo de vida:
Bridge_StartArrancaLoop_NoBloquea
Bridge_StopAsync_CierraPipeYEspera
Bridge_Dispose_Idempotente

Propagación de errores:
Bridge_HandlerFalla_DevuelveErrorNoExito
Bridge_CancelacionUsuario_NoEsError

Cada test debe:
- Correr sin Office.
- Usar WaitAsync(TimeSpan), no Thread.Sleep.
- Nombre en español: Metodo_Escenario_ResultadoEsperado.

CONVENCIONES
============
- Comentarios XML en APIs públicas de Core/ y Protocol/.
- Comentarios de línea en español.
- Sin regiones. Sin #if DEBUG salvo en tests.
- Sin 'using' no usados.
- Sin 'dynamic' fuera de Helpers/BlsComHelper.cs.
- System.Text.Json (no Newtonsoft).
- Records para DTOs.
- Async/await; sin .Result ni .Wait() salvo Stop/Dispose con timeout.

ENTREGABLE
==========
1. Diff completo.
2. Tests pasando.
3. Comandos de verificación:
     scripts\dev.ps1 build
     scripts\dev.ps1 test -Filter "FullyQualifiedName~Bridge"
     scripts\dev.ps1 test -Filter "FullyQualifiedName~Frame"
4. Commit message:
     feat: bridge JSON-RPC sobre Named Pipe (refs design/20250101-vbe-bridge.md)

REGLAS DE INTERACCIÓN
=====================
- Si algo es ambiguo, PREGUNTA antes de asumir.
- Si hay contradicción diseño vs skills/ADRs, señálala y para.
- Si necesitas una interfaz no diseñada, NO la inventes: pregunta.
- NO modifiques archivos fuera del alcance.
- NO añadas features del apartado EXCLUIDO.
- Si el diseño viola AGENTS.md, para y avisa.

Empieza confirmando que has leído los 11 documentos, y luego procede.