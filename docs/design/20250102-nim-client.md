# Feature: NIM Client (HTTP/2 SSE streaming + tool_calls)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-02

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

---

## 1. Contexto

`BlsNimClient` es el componente que habla con la API de Nvidia NIM.
Es el corazón del flujo conversacional: recibe los mensajes del usuario
(junto al contexto VBE), los envía a NIM en streaming, y emite los
chunks de texto y los tool calls conforme llegan.

Es la pieza que separa este proyecto de un chat HTTP simple: streaming
real (SSE), acumulación de tool_calls fragmentados, cancelación
cooperativa, y timeouts por chunk (no por request completa).

Cubre:
- PRD §3.3 (cliente NIM embebido).
- PRD §7 FR-09 (POST streaming, tool calls, reconexión, timeout 120s).
- PRD §11 criterios 3, 10.

Y materializa:
- ADR-003 (abort: un `CancellationTokenSource` por turno).

---

## 2. Alcance

### Incluido
- POST streaming a `https://integrate.api.nvidia.com/v1/chat/completions`.
- HTTP/2, `ResponseHeadersRead`, SSE.
- Parseo de `data:` chunks y `[DONE]`.
- Acumulación de `tool_calls` fragmentados por `index`.
- Emisión de eventos tipados a un `IBlsNimSink`.
- Timeout por chunk (120 s).
- Retry con backoff para 429/5xx (1s, 2s, 4s).
- Cancelación cooperativa con `CancellationToken`.
- API key obtenida de `IBlsSecureStore`.
- Modelo fijo `nvidia/nemotron-3-ultra-550b-a55b` (override por env
  `NIM_MODEL`).
- Tests unitarios sin red usando `FakeHttpHandler`.

### Excluido (fuera de MVP)
- Otros proveedores (OpenAI, Anthropic, local).
- Selección de modelo desde UI.
- Embeddings / visión / audio.
- Function calling paralelo (NIM devuelve un tool_call por turno en
  MVP; si devuelve varios, se soportan, pero no se optimiza).
- Reanudación de un stream interrumpido (se reintenta la request).
- Compresión de request.

---

## 3. Interfaces nuevas

### 3.1 `IBlsNimClient` (UI/ o Core/Nim/)
Contrato que ve la terminal.

```csharp
namespace Balansoft.AIAddin.UI;

public interface IBlsNimClient
{
    Task SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IBlsNimSink sink,
        CancellationToken ct);
}
```

**Notas:**
- `SendAsync` **no** lanza por cancelación de usuario (la emite como
  `sink.OnCancelled()`).
- Sí lanza `BlsNimException` para errores reales (auth, timeout,
  protocolo, red).
- `tools` puede ser vacío (modo chat sin tools).

### 3.2 `IBlsNimSink` (UI/ o Core/Nim/)
Receptor de eventos. La terminal lo implementa.

```csharp
public interface IBlsNimSink
{
    void OnTextChunk(string text);
    void OnToolCall(ToolCallComplete call);
    void OnError(BlsNimException ex);
    void OnDone();
    void OnCancelled();
}
```

**Notas:**
- El orden de los callbacks es: N × `OnTextChunk`, luego N ×
  `OnToolCall`, luego `OnDone` | `OnCancelled` | `OnError`.
- Un solo `OnDone`, `OnCancelled` u `OnError` por `SendAsync`.
- El cliente **no** ejecuta tools: solo las reporta.

### 3.3 Modelos de dominio

```csharp
public sealed record ChatMessage(string Role, string Content);

public sealed record ToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema);

public sealed record ToolCallComplete(
    string Id,
    string Name,
    string ArgumentsJson);
```

`Role` ∈ {`"system"`, `"user"`, `"assistant"`, `"tool"`}.

### 3.4 `BlsNimClient` (UI/)
Implementación.

```csharp
public sealed class BlsNimClient : IBlsNimClient, IDisposable
{
    public BlsNimClient(
        HttpClient httpClient,          // inyectado, único
        IBlsSecureStore secureStore,
        IBlsLogger logger,
        string model = null,            // null → NIM_MODEL o default
        string endpoint = null);        // null → default NIM

    public Task SendAsync(
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        IBlsNimSink sink,
        CancellationToken ct);

    public void Dispose();
}
```

**Inyección:**
- `HttpClient`: viene de fuera. La terminal o el host lo construyen
  una vez y lo reutilizan.
- `IBlsSecureStore`: para leer la API key.
- `IBlsLogger`: para diagnóstico.
- `model` / `endpoint`: parametrizables para tests; por defecto leen
  `NIM_MODEL` o usan la constante.

### 3.5 `NimStreamParser` (Core/Nim/, interno)
Máquina de parseo SSE + acumulación de tool_calls. Aislado para
testear sin HTTP.

```csharp
internal sealed class NimStreamParser
{
    public NimStreamParser(IBlsNimSink sink);

    // Alimentar línea a línea
    public void Feed(string line);

    // Al recibir [DONE]
    public void Complete();

    // Estado acumulado
    public IReadOnlyList<ToolCallComplete> PendingToolCalls { get; }
}
```

**Notas:**
- Aislado de HTTP: se puede testear con strings de chunks.
- Acumula tool_calls por `index`.
- Valida JSON de argumentos al `Complete()`.

### 3.6 `NimHttpRequestBuilder` (Core/Nim/, interno)
Construye el payload JSON del POST. Aislado para tests.

```csharp
internal static class NimHttpRequestBuilder
{
    public static StringContent Build(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ToolDefinition> tools,
        string apiKey);
}
```

**Notas:**
- Serializa con `System.Text.Json`.
- Añade headers: `Authorization: Bearer <key>`, `Accept: text/event-stream`.
- `stream: true` siempre.
- `tool_choice: "auto"` solo si `tools.Count > 0`.

---

## 4. Invariantes afectados

| Fuente             | Invariante                | Cómo se cumple                                                                     |
|--------------------|---------------------------|------------------------------------------------------------------------------------|
| AGENTS.md regla 4  | API key solo DPAPI        | Se lee de `IBlsSecureStore`, nunca se loguea                                       |
| AGENTS.md regla 5  | Errores reales            | Excepciones tipadas, body preservado                                               |
| AGENTS.md regla 10 | Abort ADR-003             | `CancellationToken` por turno, `OnCancelled()` no error                            |
| ADR-003            | Cancelación ≠ timeout     | `OperationCanceledException` → `OnCancelled()`; timeout → `BlsNimTimeoutException` |
| ADR-003            | Retry respeta cancelación | `ct.ThrowIfCancellationRequested()` antes de cada retry                            |
| PRD FR-09          | Timeout 120s              | Timeout por chunk con `CancelAfter(120s)`                                          |
| PRD FR-09          | Tool calls via bridge     | El cliente **no** ejecuta; emite `OnToolCall`                                      |
| PRD FR-09          | Reconexión                | Retry con backoff 1s/2s/4s en 429/5xx                                              |
| skill 03 §1        | HttpClient único          | Inyectado, no creado por request                                                   |
| skill 03 §2        | `ResponseHeadersRead`     | Forzado en `HttpRequestMessage`                                                    |
| skill 03 §4        | Tool_calls por `index`    | `NimStreamParser` acumula por índice                                               |

---

## 5. Flujo interno

```
SendAsync(messages, tools, sink, ct)
  │
  ├─ 1. Leer API key de IBlsSecureStore
  │     ├─ no existe → sink.OnError(BlsNimAuthException("API key no configurada")); return
  │     └─ existe    → continuar
  │
  ├─ 2. Construir request (NimHttpRequestBuilder.Build)
  │
  ├─ 3. Retry loop (máx 3 intentos)
  │     │
  │     ├─ ct.ThrowIfCancellationRequested()  // ADR-003
  │     │
  │     ├─ POST con ResponseHeadersRead
  │     │
  │     ├─ status 200 → break al paso 4
  │     ├─ status 401 → sink.OnError(Auth); return
  │     ├─ status 429/5xx → backoff (1s/2s/4s) y reintentar
  │     └─ otro status → sink.OnError(BlsNimException(body)); return
  │
  ├─ 4. Leer stream SSE línea a línea
  │     │
  │     └─ loop
  │         │
  │         ├─ ct.ThrowIfCancellationRequested() → sink.OnCancelled(); return
  │         │
  │         ├─ chunkCts = CreateLinkedTokenSource(ct); CancelAfter(120s)
  │         │
  │         ├─ line = await reader.ReadLineAsync(chunkCts.Token)
  │         │     ├─ OperationCanceledException + !ct → TimeoutException
  │         │     ├─ null (EOF) → break loop (sin [DONE] → protocolo raro)
  │         │     └─ line → continuar
  │         │
  │         ├─ si line == "data: [DONE]" → parser.Complete(); break
  │         ├─ si line empieza por "data:" → parser.Feed(line)
  │         ├─ si line vacía o ":..." → ignorar
  │         └─ otro → ignorar (tolerancia)
  │
  ├─ 5. Emitir tool_calls pendientes
  │     └─ foreach call in parser.PendingToolCalls: sink.OnToolCall(call)
  │
  └─ 6. sink.OnDone()
```

### Manejo de excepciones
```
try
{
    // pasos 1-6
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    sink.OnCancelled();                 // ADR-003: no error
}
catch (BlsNimException ex)
{
    sink.OnError(ex);                   // error tipado
}
catch (Exception ex)
{
    logger.Error("BlsNimClient", "Excepción no controlada", ex);
    sink.OnError(new BlsNimException("Error interno", ex));
}
```

---

## 6. Contratos de eventos (orden garantizado)

| Evento        | Orden | Cuándo                                            |
|---------------|-------|---------------------------------------------------|
| `OnTextChunk` | 0..N  | Chunks de texto conforme llegan                   |
| `OnToolCall`  | 0..N  | Tras el último `OnTextChunk`, en orden de `index` |
| `OnDone`      | 1     | Al recibir `[DONE]` y emitir todos los tool_calls |
| `OnCancelled` | 0..1  | Si el usuario cancela                             |
| `OnError`     | 0..1  | Si hay error fatal                                |

**Nunca** se emiten dos de {`OnDone`, `OnCancelled`, `OnError`} en una
misma llamada a `SendAsync`.

---

## 7. Criterios de aceptación

### CA-01 — Streaming SSE
- [ ] Chunk único → `OnTextChunk("Hola")` + `OnDone`.
- [ ] Múltiples chunks → `OnTextChunk` en orden + `OnDone`.
- [ ] `[DONE]` cierra el stream.
- [ ] Líneas vacías y comentarios ignorados.
- **Tests:** `Nim_ChunkUnico_EmiteOnTextChunk`,
  `Nim_MultiplesChunks_EmiteEnOrden`,
  `Nim_DoneCierraStream`,
  `Nim_LineasVaciasIgnoradas`.

### CA-02 — Tool calls fragmentados
- [ ] Tool_call en un solo chunk → `OnToolCall` con args completos.
- [ ] Tool_call fragmentado → acumulado correctamente.
- [ ] Múltiples tool_calls → emitidos en orden de `index`.
- [ ] Args JSON inválido → `OnError(ProtocolException)`.
- **Tests:** `Nim_ToolCallUnChunk_EnsamblaCorrectamente`,
  `Nim_ToolCallFragmentado_EnsamblaCorrectamente`,
  `Nim_MultiplesToolCalls_EmiteEnOrden`,
  `Nim_ToolCallArgsInvalidos_EmiteError`.

### CA-03 — Timeout por chunk
- [ ] Sin datos por 120 s → `BlsNimTimeoutException` + `OnError`.
- [ ] Datos fluyendo con pausas < 120 s → no hay timeout.
- **Tests:** `Nim_Timeout120s_LanzaBlsNimTimeoutException`,
  `Nim_ChunksEspaciados_SinTimeout`.

### CA-04 — Cancelación (ADR-003)
- [ ] Cancelación durante stream → `OnCancelled`, sin `OnError`.
- [ ] Cancelación antes del POST → `OnCancelled`, sin request.
- [ ] Cancelación durante retry → no reintenta más.
- **Tests:** `Nim_CancelacionUsuario_EmiteOnCancelled`,
  `Nim_CancelacionAntesDelPost_NoHaceRequest`,
  `Nim_CancelacionDuranteRetry_NoReintenta`.

### CA-05 — Auth y errores HTTP
- [ ] 401 → `BlsNimAuthException`.
- [ ] 429 → retry 3 veces con backoff, luego `BlsNimRateLimitException`.
- [ ] 500 → retry 3 veces con backoff, luego `BlsNimServerException`.
- [ ] 400 → `BlsNimException` con body preservado, sin retry.
- **Tests:** `Nim_401_LanzaBlsNimAuthException`,
  `Nim_429_ReintentaConBackoff`,
  `Nim_500_ReintentaConBackoff`,
  `Nim_400_NoReintentaYPreservaBody`.

### CA-06 — API key
- [ ] Sin API key → `BlsNimAuthException("API key no configurada")`.
- [ ] La API key **nunca** aparece en logs ni excepciones.
- **Tests:** `Nim_SinApiKey_EmiteAuthError`,
  `Nim_LoggerNoRegistraApiKey`.

### CA-07 — HttpClient único
- [ ] No se crea un `HttpClient` por request.
- [ ] El cliente inyectado se reutiliza.
- **Test:** `Nim_HttpClientUnico_NoSeRecrea`.

### CA-08 — Modelo fijo
- [ ] Por defecto usa `nvidia/nemotron-3-ultra-550b-a55b`.
- [ ] `NIM_MODEL` env var lo sobrescribe.
- [ ] El parámetro `model` del constructor tiene prioridad sobre env.
- **Tests:** `Nim_ModeloDefault_EsNemotron`,
  `Nim_ModeloEnvOverride_UsaEnv`,
  `Nim_ModeloParametro_PrioridadSobreEnv`.

---

## 8. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── UI/
│   ├── IBlsNimClient.cs                    (nuevo)
│   ├── IBlsNimSink.cs                      (nuevo)
│   ├── BlsNimClient.cs                     (nuevo)
│   └── Nim/
│       ├── ChatMessage.cs                  (nuevo)
│       ├── ToolDefinition.cs               (nuevo)
│       ├── ToolCallComplete.cs             (nuevo)
│       ├── NimStreamParser.cs              (nuevo, internal)
│       └── NimHttpRequestBuilder.cs        (nuevo, internal)
└── Helpers/
    └── NimExceptions.cs                    (nuevo, o en Helpers/Exceptions)

tests/Balansoft.AIAddin.Tests/
├── UI/Nim/
│   ├── BlsNimClientTests.cs                (nuevo)
│   ├── NimStreamParserTests.cs             (nuevo)
│   └── NimHttpRequestBuilderTests.cs       (nuevo)
└── Fakes/
    ├── FakeHttpHandler.cs                  (nuevo)
    ├── FakeSecureStore.cs                  (nuevo)
    └── FakeLogger.cs                       (ya existe del bridge)
```

---

## 9. Decisiones internas (no requieren ADR)

### 9.1 `HttpClient` inyectado, no estático
En el proyecto real será un único `HttpClient` creado por el host. En
tests se inyecta un `HttpClient` con `FakeHttpHandler`. Evita la
complejidad de un singleton global que dificulta tests.

### 9.2 `System.Text.Json` (no Newtonsoft)
Alineado con el resto del proyecto. Fuente única.

### 9.3 Parseo SSE línea a línea con `StreamReader`
`ReadLineAsync` es suficiente: NIM emite eventos como una línea por
`data:`. No se hace `ReadAsStringAsync` (rompe el streaming).

### 9.4 Timeout por chunk, no global
`HttpClient.Timeout` se deja en `InfiniteTimeSpan`. El timeout se aplica
con un `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(120s)`
**por cada `ReadLineAsync`**. Esto permite que un stream largo (con
chunks frecuentes) dure más de 120 s sin timeout, mientras que un stream
silencioso corta a los 120 s.

### 9.5 Acumulación de tool_calls en `NimStreamParser`
El parser mantiene `Dictionary<int, ToolCallAccumulator>`. Cada chunk con
`delta.tool_calls[i]` lo actualiza. En `Complete()` se emiten los
acumulados con `id` y `name` no nulos. Si falta alguno, se descarta y se
loguea `WARN`.

### 9.6 Cancelación ≠ timeout
- `OperationCanceledException` con `ct.IsCancellationRequested` →
  `OnCancelled()`.
- `OperationCanceledException` sin `ct.IsCancellationRequested` (i.e.
  canceló `chunkCts`) → `BlsNimTimeoutException`.

Esta distinción es crítica (ADR-003). El test CA-04 la cubre.

### 9.7 Retry solo para 429/5xx
Errores de cliente (400, 401, 403, 404) no se reintentan: son bugs del
request o de la config, no problemas transitorios.

### 9.8 Respeto a `Retry-After`
Si el 429/5xx trae header `Retry-After`, se usa ese valor (en segundos)
en lugar del backoff exponencial, con tope de 30 s.

### 9.9 Body de error truncado a 8 KB
El body de respuesta NIM se preserva en `BlsNimException.ResponseBody`
para diagnóstico, truncado a 8 KB (evita loguear MB de JSON si NIM
devuelve un error gigante).

### 9.10 Modelo: parámetro > env > default
Prioridad explícita para permitir tests deterministas.

---

## 10. Fuera de alcance (recordatorio)

- Otros proveedores.
- Selección de modelo en UI.
- Embeddings / visión / audio.
- Reanudación de stream interrumpido.
- Compresión de request.
- Function calling paralelo optimizado.
- Caching de respuestas.
- Rate limiting interno.

---

## 11. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (reglas duras).
2. `.opencode/skills/00-project-context.md` (contexto general).
3. `.opencode/skills/03-nim-client-streaming.md` (este dominio).
4. `.opencode/skills/05-secure-store-dpapi.md` (origen de la API key).
5. `.opencode/skills/08-logging-errors.md` (excepciones tipadas).
6. `.opencode/skills/09-testing-strategy.md` (fake HttpMessageHandler).
7. `docs/design/ADR-003-abort-model.md` (cancelación vs timeout).

Opcional:
- `.opencode/skills/06-winforms-threading.md` (cómo la terminal consume
  los eventos).

---

## 12. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests sin red
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Nim"

# Integración NIM (opt-in, requiere NIM_API_KEY)
$env:NIM_API_KEY = "..."
dotnet test --no-build --filter "Category=Integration"
```

Criterio de éxito: build verde + todos los tests del cliente verdes.

---

## 13. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §7 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §11 están listados.

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-08.
- [ ] No modificar interfaces del §3.
- [ ] Referenciar este doc en el commit:
      `feat: NIM client SSE + tool_calls (refs design/20250102-nim-client.md)`.

---

## 14. Referencias

- `AGENTS.md` reglas 4, 5, 10.
- `docs/design/ADR-003-abort-model.md`.
- `PRD.md` §3.3, §7 FR-09, §11.
- `.opencode/skills/03-nim-client-streaming.md`.
- `.opencode/skills/05-secure-store-dpapi.md`.
- `.opencode/skills/08-logging-errors.md`.
- `.opencode/skills/09-testing-strategy.md`.
- `.opencode/skills/06-winforms-threading.md`.
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa el NIM client (en paralelo al bridge)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/00-project-context.md
3. docs/design/20250102-nim-client.md (el diseño)
4. Los skills citados en §11 del diseño
5. Los ADRs referenciados en §14 del diseño

Implementa la feature 'NIM Client' exactamente como está especificado.
No cambies interfaces del §3. No añadas features del §10.
Entrega diff completo + tests + comando de verificación del §12."
```

### Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/10-workflow-big-pickle-nemotron.md (checklist de rechazo)
3. docs/design/20250102-nim-client.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill 10.
Presta especial atención a:
- Cancelación ≠ timeout (ADR-003).
- HttpClient único (skill 03 §1).
- API key nunca en log (skill 03 §anti-patrones).

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño

| # | Doc                                | Estado      | Paralelizable         |
|---|------------------------------------|-------------|-----------------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado | Sí (con #2)           |
| 2 | `20250102-nim-client.md`           | ✅ Entregado | Sí (con #1)           |
| 3 | `20250103-nim-terminal.md`         | Pendiente   | No (necesita #1 y #2) |
| 4 | `20250104-secure-store.md`         | Pendiente   | Sí (con #1, #2)       |
| 5 | `20250105-chat-history.md`         | Pendiente   | Sí (tras ADR-001)     |
| 6 | `20250106-registration-scripts.md` | Pendiente   | Sí (con cualquiera)   |
| 7 | `20250107-logging-version.md`      | Pendiente   | Sí (con cualquiera)   |
| 8 | `20250108-addin-lifecycle.md`      | Pendiente   | No (integra todo)     |

**Ruta crítica hacia el MVP:**
```
#1 bridge  ─┐
            ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client  ─┘
#4 store  ─→ (alimenta #2 y #3)
#5 history ─→ (alimenta #3)
#6 reg     ─→ (necesario para #8)
#7 log     ─→ (transversal, alimenta todo)
```

**Paralelización inmediata posible (3 frentes):**
- Frente A: #1 bridge (Nemotron A)
- Frente B: #2 client (Nemotron B)
- Frente C: #4 store + #6 reg + #7 log (Nemotron C, o secuencial si
  hay un solo Nemotron)
