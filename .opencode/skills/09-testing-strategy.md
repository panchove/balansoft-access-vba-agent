# SKILL 09 — Estrategia de tests

> **Cuándo cargar este skill:** al escribir, modificar o revisar tests;
> al introducir una nueva feature que toque COM, threading, red o
> persistencia; al decidir qué va a unitarias vs integración.

---

## Objetivo

Garantizar que el add-in es **verificable sin Office** en la mayoría de
casos, que los invariantes críticos (ADR-001..004) están cubiertos por
tests, y que los 79 tests del MVP no se degradan.

---

## Capas de test

### 1. Unitarias sin Office (obligatorias, CI sin Access)
Cubren > 90% de la lógica. **Nunca** instancian Access ni VBE reales.

Proyecto: `Balansoft.AIAddin.Tests` (`net48`, x86, MSTest o xUnit).

Cubren:
- `BlsVbeService` contra `MockVbe`.
- `BlsAccessObjectService` contra `FakeAccessApp`.
- `BlsNimClient` con `HttpMessageHandler` fake que emite SSE predecible.
- `BlsVbeBridgeServer` con `PipeStream` en memoria (o `NamedPipeServerStream`
  real en loopback).
- `BlsSecureStore` con `IDataProtector` fake + `filePath` temporal.
- `BlsChatHistory` con directorio temporal.
- `BlsLogger` con directorio temporal.
- `BlsComHelper` con excepciones sintéticas (`COMException` construidas
  con HRESULT conocidos).
- Parsers (SSE, JSON-RPC, framing).

**Meta:** 73 tests sin Office.

### 2. Integración NIM (opt-in)
Llamadas reales al endpoint de Nvidia. **Marcadas y excluidas por
defecto** para no consumir tokens en cada build.

```csharp
[Trait("Category", "Integration")]
[Fact]
public async Task NIM_StreamSimplePrompt_DevuelveTexto()
{ /* requiere NIM_API_KEY en env */ }
```

**Meta:** 6 tests, todos con skip si falta la API key.

### 3. Integración Access (manual, no automatizable)
Checklist documentada en `docs/manual-tests.md`, ejecutada antes de un
release:

- Carga del add-in en Access 2010, 2016 y 365 (x86).
- Menú Tools → "NIM Terminal" abre la ventana con owner correcto.
- Prompt simple en streaming se renderiza en vivo.
- Tool call `create_module` crea un módulo real.
- Historial persiste al cerrar y reabrir.
- API key se guarda en DPAPI y no se vuelve a pedir.
- `LoadBehavior` sigue en 3 tras el arranque (no se degrada a 2).
- Cancelación con "Detener" funciona en medio de un stream.

**Meta:** 0 automatizables; checklist firmada antes de release.

---

## Mocks y fakes canónicos

### `MockVbe` (implementa `IBlsVbe`)
```csharp
public sealed class MockVbe : IBlsVbe
{
    public bool IsAvailable { get; set; } = true;
    public List<string> Modules { get; } = new();
    public Dictionary<string, string> Code { get; } = new();
    public SelectionInfo Selection { get; set; } = SelectionInfo.Empty;
    public ModuleInfo ActiveModule { get; set; } = ModuleInfo.Empty;
    public ProjectKeyInfo ProjectKey { get; set; } =
        new() { Key = "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
                Ephemeral = false, Source = "path" };
    // ...
}
```

### `FakeVbeCom`
Simula el `dynamic` COM real. Útil para probar `BlsComHelper` y
`BlsVbeService` contra propiedades que devuelven `object`.

### `FakeAccessApp`
Implementa `IBlsAccess` devolviendo estructuras predefinidas (tablas,
queries, formularios) para probar `BlsAccessObjectService`.

### `TestUiScheduler` (implementa `IUiScheduler`)
```csharp
public sealed class TestUiScheduler : IUiScheduler
{
    public T Run<T>(Func<T> op) => op();
    public Task<T> RunAsync<T>(Func<T> op) => Task.FromResult(op());
}
```

Ejecuta en el hilo del test. No hay STA en tests.

### `FakeHttpHandler`
Emite una secuencia de chunks SSE prefijados:
```csharp
var handler = new FakeHttpHandler(
    "data: {\"choices\":[{\"delta\":{\"content\":\"Hola\"}}]}\n\n" +
    "data: {\"choices\":[{\"delta\":{\"content\":\" mundo\"}}]}\n\n" +
    "data: [DONE]\n\n");
```

### `FakeDataProtector`
XOR con clave fija. **Nunca** `ProtectedData` real en tests.

### `FakeLogger`
Captura en memoria; los tests verifican que la API key **no** aparece en
ningún mensaje.

### `FakeChatHistory`
En memoria, con hooks para verificar debounce, rotación, atomicidad.

---

## Convenciones de test

### Naming
`Metodo_Escenario_ResultadoEsperado` (español).

Ejemplos:
- `WriteModule_ModuloInexistente_LanzaBlsVbeException`
- `GetProjectKey_BaseSinGuardar_RetornaEphemeral`
- `SendAsync_ChunkConToolCall_EmiteOnToolCall`
- `Frame_Excede1MB_CierraConError`

### Estructura
Patrón **Arrange / Act / Assert** con comentarios `// Arrange`, `// Act`,
`// Assert`. Sin `[SetUp]` global salvo para infraestructura común
(logger, directorios temporales).

### Prohibiciones
- **Nunca** `Thread.Sleep`. Usar `TaskCompletionSource` + timeout
  (`WaitAsync(TimeSpan)`).
- **Nunca** `[TestInitialize]` que toque COM real.
- **Nunca** depender del orden de tests.
- **Nunca** escribir en `%APPDATA%` real; usar `Path.GetTempPath()` +
  `Guid.NewGuid()`.
- **Nunca** tocar el registro real (usar un `IRegistry` inyectable o
  `Registry.CurrentUser` con subclaves de test que se limpian).

### Timeout global
Todos los tests async usan `.WaitAsync(TimeSpan.FromSeconds(5))`. Un test
que tarda más → falla (indica deadlock o hang).

---

## Tests obligatorios por ADR

### ADR-001 — Identidad de proyecto (6 tests)
- `ProjectKey_BaseGuardada_RetornaHash32Hex`
- `ProjectKey_BaseSinGuardar_RetornaHashConFlagEphemeral`
- `ProjectKey_DescriptionOverride_UsaValorDeclarado`
- `ProjectKey_DescriptionOverrideInvalido_IgnoraYCaeAPath`
- `ProjectKey_RutaConMayusculas_NormalizaAntesDeHashear`
- `ProjectKey_RutaConSeparadorInverso_NormalizaAntesDeHashear`

### ADR-002 — Framing del pipe (11 tests)
- `Frame_EnvioSimple_LeeMismoPayload`
- `Frame_PayloadConNewlines_NoRompeFraming`
- `Frame_PayloadUtf8Multibyte_LengthEsEnBytes`
- `Frame_Excede1MB_CierraConError`
- `Frame_HeaderParcial_EsperaResto`
- `Frame_PayloadParcial_EsperaResto`
- `Frame_Vacio_RespondeInvalidRequest`
- `Frame_JsonInvalido_RespondeInvalidRequest`
- `Frame_SegundoCliente_RespondeBusy`
- `Frame_TimeoutInactivo_MitadDeFrame_Cierra`
- `Frame_TimeoutIdle_EntreFrames_NoCierra`

### ADR-003 — Abort (8 tests)
- `Turno_CanceladoDuranteStream_DetieneYEmitCancelled`
- `Turno_CanceladoAntesDeTool_NoEjecutaTool`
- `Turno_CanceladoDuranteToolVbe_EsperaTerminarYDescarta`
- `Turno_TimeoutChunk_LanzaTimeoutNoCancelled`
- `Turno_DobleDetener_EsNoOp`
- `Turno_CerrarTerminalConTurnoActivo_CancelaYDispose`
- `Turno_CanceladoDuranteRetry_NoReintenta`
- `Turno_Cancelado_HistorialMarcaCancelled`

### ADR-004 — object_detail (11 tests)
- `ObjectDetail_Tabla_DevuelveCamposSinSql`
- `ObjectDetail_QueryConIncludeSql_DevuelveSql`
- `ObjectDetail_SqlExcede8KB_TruncaYMarca`
- `ObjectDetail_MasDeMaxFields_MarcaTruncated`
- `ObjectDetail_ObjetoMSys_EsOmitido`
- `ObjectDetail_ObjetoTemporal_EsOmitido`
- `ObjectDetail_TipoInvalido_LanzaInvalidParams`
- `ObjectDetail_ObjetoInexistente_ErrorConMensaje`
- `ObjectDetail_RecordCountTimeout_OmitePropiedad`
- `ListObjects_MasDe500Objetos_MarcaTruncated`
- `ProjectSummary_DevuelveCountsYProjectKey`

**Subtotal ADRs: 36 tests.**

---

## Tests transversales obligatorios

### Seguridad (API key nunca en claro)
- `SecureStore_SaveApiKey_ArchivoNoContienePlaintext`
- `SecureStore_LoggerNoRegistraApiKey` (FakeLogger + búsqueda del string)
- `SecureStore_ExcepcionNoIncluyeApiKey`
- `SecureStore_HistorialNoSerializaApiKey`

### Propagación de errores (nunca "hecho" falso)
- `Bridge_HandlerFalla_DevuelveErrorNoExito`
- `Nim_ErrorAuth_EmiteOnErrorNoOnDone`
- `Com_FalloEnWriteModule_LanzaBlsComException`
- `Logger_UnhandledException_NoRelanza`

### Threading / UI
- `Terminal_OnTextChunkDesdeHiloFondo_MarshalingAlHiloUI`
- `Terminal_CerrarDuranteTurno_CancelaYFlush`
- `Terminal_CerrarNoCierraAccess` (integración manual, marcada)
- `UiScheduler_OperacionLanzaExcepcion_PropagaAlCaller`

### Historial
- `History_GuardarYCargar_RoundTrip`
- `History_ArchivoCorrupto_RenombraYEmpiezaVacio`
- `History_Debounce500ms_NoEscribePorCadaChunk`
- `History_Rotacion1MB_ArchivaYEmpiezaNuevo`
- `History_EscrituraAtomica_NoDejaArchivoParcial`

### NIM cliente
- `Nim_ChunkUnico_EmiteOnTextChunk`
- `Nim_ToolCallFragmentado_EnsamblaCorrectamente`
- `Nim_DoneCierraStream`
- `Nim_429_ReintentaConBackoff`
- `Nim_Timeout120s_LanzaBlsNimTimeoutException`
- `Nim_CancelacionUsuario_EmiteOnCancelled`
- `Nim_401_LanzaBlsNimAuthException`
- `Nim_HttpClientUnico_NoSeRecrea`

**Subtotal transversales: ~29 tests.**

**Total MVP: 36 (ADRs) + 29 (transversales) + 6 (integración NIM) + otros
unitarios = 73 sin Office + 6 integración = 79.** ✅

---

## Cobertura mínima por capa

| Capa                 | Cobertura                                    |
|----------------------|----------------------------------------------|
| `Core/`              | 80%                                          |
| `VbeIntegration/`    | 70%                                          |
| `AccessIntegration/` | 70%                                          |
| `UI/`                | 50% (solo lógica no visual)                  |
| `Helpers/`           | 80%                                          |
| `Registration/`      | 60% (verificable sin tocar el registro real) |

Se mide con `dotnet test --collect:"XPlat Code Coverage"` o
equivalente. **No** se exige cobertura del 100% en `UI/` porque la
mayoría es código de layout sin lógica.

---

## Configuración de test

### `.runsettings`
```xml
<RunSettings>
  <RunConfiguration>
    <TestCaseFilter>Category!=Integration</TestCaseFilter>
  </RunConfiguration>
</RunSettings>
```

### CI
- Ejecuta `dotnet test --settings .runsettings` (sin integración).
- Publica cobertura como artefacto.
- Falla si algún test de ADR no existe (verificación por atributo o
  por nombre).

### Local con integración
```powershell
$env:NIM_API_KEY = "..."     # NO commitear
dotnet test --filter "Category=Integration"
```

---

## Prohibiciones absolutas

| Anti-patrón                               | Por qué                         |
|-------------------------------------------|---------------------------------|
| `Thread.Sleep` en tests                   | Hangs, tests lentos, flaky      |
| Test que toca COM real                    | No corre en CI sin Office       |
| Escribir en `%APPDATA%` real              | Contamina el perfil del dev     |
| Test con dependencia de orden             | Flaky                           |
| Omitir tests de ADR                       | Rompe el contrato del proyecto  |
| Test que loguea la API key                | Filtra secreto al CI log        |
| Reducir el número de tests sin justificar | Degrada calidad                 |
| `[Ignore]` sin issue asociado             | Se olvidan                      |
| Mock de `ProtectedData` en producción     | Rompe la abstracción            |
| Depender de `DateTime.Now`                | Flaky; usar `IClock` inyectable |

---

## Checklist antes de commitear tests

- [ ] ¿El test corre sin Office?
- [ ] ¿Usa `Path.GetTempPath()` + GUID si toca disco?
- [ ] ¿Usa `WaitAsync(TimeSpan)` en lugar de `Thread.Sleep`?
- [ ] ¿El nombre sigue `Metodo_Escenario_ResultadoEsperado`?
- [ ] ¿Cubre un caso nuevo o un invariante existente?
- [ ] Si toca un ADR, ¿está el test correspondiente del ADR?
- [ ] Si es de integración, ¿está marcado `Category=Integration`?
- [ ] ¿No loguea secretos?
- [ ] ¿No depende del orden de otros tests?
- [ ] ¿No baja la cobertura mínima de su capa?

---

## Referencias

- `AGENTS.md` regla 7 (warnings as errors incluye tests).
- `docs/design/ADR-001..004` (tests obligatorios listados en cada ADR).
- PRD §7 FR-11, §8 (tests unitarias sin Office), §11 (79 tests).
- `skills/01-com-late-binding.md` (`BlsComException`).
- `skills/02-vbe-automation.md` (`MockVbe`, `TestUiScheduler`).
- `skills/03-nim-client-streaming.md` (`FakeHttpHandler`).
- `skills/04-jsonrpc-pipe-bridge.md` (tests de framing).
- `skills/05-secure-store-dpapi.md` (`FakeDataProtector`).
- `skills/06-winforms-threading.md` (tests de marshaling).
- `skills/07-registration-scripts.md` (verificación manual de `LoadBehavior`).
- `skills/08-logging-errors.md` (`FakeLogger` para verificar no-fuga).
