---
name: bls-nim-client-streaming
description: Cliente HTTP/2 streaming SSE a Nvidia NIM y acumulador de tool calls
---

# SKILL 03 — BlsNimClient (HTTP/2 SSE + tool_calls)

> **Referencia ISO/IEC**: 42001 (AI Management) + 25010 (Reliability & Performance)
> **Área**: nim | streaming | sse | http2
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `UI/BlsNimClient.cs`, la lógica de streaming SSE, parseo de `tool_calls`,
> retry, abort o timeouts del cliente NIM.

---

## Endpoint

```
POST https://integrate.api.nvidia.com/v1/chat/completions
```

### Headers obligatorios
```
Authorization: Bearer <api-key-desde-DPAPI>
Accept:        text/event-stream
Content-Type:  application/json
```

### Modelo fijo
`nvidia/nemotron-3-ultra-550b-a55b` (override por variable de entorno
`NIM_MODEL`). **Nunca** se elige desde la UI (fuera de MVP).

### Body (resumen)
```json
{
  "model": "nvidia/nemotron-3-ultra-550b-a55b",
  "messages": [ /* system + historial + user */ ],
  "tools": [ /* 10 tools del bridge, ver skill bls-jsonrpc-pipe-bridge */ ],
  "tool_choice": "auto",
  "stream": true,
  "temperature": 0.2,
  "max_tokens": 8192
}
```

---

## Requisitos duros

### 1. `HttpClient` único
- **Un** `HttpClient` estático o inyectado para toda la vida del add-in.
- Crear uno por request → *socket exhaustion* + TIME_WAIT acumulados.
- Configuración:
  ```csharp
  var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
  var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
  http.DefaultRequestHeaders.Accept.Add(new("text/event-stream"));
  ```
- El timeout **no** se pone aquí; se controla por chunk (ver §5).

### 2. Streaming real
- `HttpCompletionOption.ResponseHeadersRead` obligatorio.
- Leer con `StreamReader` sobre `response.Content.ReadAsStreamAsync()`.
- **Prohibido** `ReadAsStringAsync()` en streaming: rompe SSE al no
  devolver hasta que cierra la conexión.

### 3. Protocolo SSE
- Los eventos se separan por línea vacía (`\n\n`).
- Cada línea relevante empieza por `data:`.
- `data: [DONE]` cierra el stream limpiamente.
- Líneas en blanco entre eventos se ignoran.
- Comentarios (`:keep-alive`) se ignoran.
- **Nunca** asumir que un `data:` es un JSON completo: puede ser
  multilínea en teoría (NIM no lo hace, pero se contempla).

### 4. Parseo de `tool_calls`
Los tool calls llegan **fragmentados** en múltiples chunks. Reglas:

- Acumular por `choices[0].delta.tool_calls[i].index`.
- Cada fragmento puede traer:
  - `id` (solo el primero)
  - `type` (solo el primero, siempre `"function"`)
  - `function.name` (solo el primero)
  - `function.arguments` (fragmentos concatenables)
- Al cerrar el stream (`[DONE]`), todo tool_call con `id` y `name` se
  considera **completo** y se emite.
- Validar que `arguments` es JSON parseable; si no → error tipado.

### 5. Timeout por chunk (no por request)
- 120 s **sin recibir ningún dato** → `BlsNimTimeoutException`.
- Implementación:
  ```csharp
  using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
  chunkCts.CancelAfter(TimeSpan.FromSeconds(120));
  var read = await reader.ReadLineAsync(chunkCts.Token);
  ```
- Se reinicia el contador tras cada chunk recibido.
- El timeout **no** se debe confundir con cancelación de usuario
  (ADR-003): el timeout es error, la cancelación es `OnCancelled()`.

### 6. Abort cooperativo (ADR-003)
- Recibe `CancellationToken` del turno actual.
- Si se cancela:
  - Cerrar el stream (`response.Dispose()`).
  - Emitir `OnCancelled()`.
  - **No** lanzar excepción al consumidor.
  - **No** loguear como `ERROR` (es `INFO`).

### 7. Retry
- Solo para **429** y **5xx**.
- 3 intentos máximo con backoff exponencial: 1 s, 2 s, 4 s.
- Respetar `Retry-After` si viene en el header.
- **No** reintentar si el token del turno está cancelado (ADR-003).
- **No** reintentar 4xx distintos de 429 (son errores del cliente).
- Tras agotar reintentos → `BlsNimException` con el último body.

---

## Contrato de eventos (`IBlsNimSink`)

El cliente emite eventos tipados a un *sink* (la terminal):

```csharp
public interface IBlsNimSink
{
    void OnTextChunk(string text);
    void OnToolCall(ToolCallComplete call);
    void OnError(BlsNimException ex);
    void OnDone();
    void OnCancelled();
}

public sealed record ToolCallComplete(
    string Id,
    string Name,
    string ArgumentsJson);
```

El cliente **nunca** ejecuta la tool: la entrega a la terminal, que la
reenvía al bridge (skill bls-jsonrpc-pipe-bridge) y devuelve el resultado como mensaje
`role:"tool"` al siguiente POST.

---

## Contrato de tool_call (formato NIM)

Ejemplo de chunk inicial:
```json
{"choices":[{"delta":{"tool_calls":[
  {"index":0,"id":"call_x","type":"function",
   "function":{"name":"write_module","arguments":""}}
]}}]}
```

Ejemplo de chunk de continuación:
```json
{"choices":[{"delta":{"tool_calls":[
  {"index":0,"function":{"arguments":"{\"name\":\"U"}}
]}}]}
```

Ejemplo de chunk final:
```json
{"choices":[{"delta":{"tool_calls":[
  {"index":0,"function":{"arguments":"tils\",\"code\":\"...\"}"}}
]}}]}
```

Ensamblado:
```json
{
  "id":"call_x",
  "type":"function",
  "function":{
    "name":"write_module",
    "arguments":"{\"name\":\"Utils\",\"code\":\"...\"}"
  }
}
```

---

## Errores tipados

| Excepción                  | Cuándo                        | Recuperable                        |
|----------------------------|-------------------------------|------------------------------------|
| `BlsNimAuthException`      | 401                           | Prompt re-guardar API key en DPAPI |
| `BlsNimRateLimitException` | 429 tras retries              | Sí, tras esperar                   |
| `BlsNimServerException`    | 5xx tras retries              | Sí, reintentar manual              |
| `BlsNimTimeoutException`   | 120 s sin chunk               | Sí, reintentar                     |
| `BlsNimProtocolException`  | SSE malformado, JSON inválido | No                                 |
| `BlsNimException`          | Otros                         | Según caso                         |

**Todas** conservan el **body** de respuesta en `ex.ResponseBody` (máx
8 KB, truncado) para diagnóstico.

---

## Anti-patrones prohibidos (rechazo automático en PR)

| Anti-patrón                                          | Por qué                                 |
|------------------------------------------------------|-----------------------------------------|
| `new HttpClient()` por request                       | Socket exhaustion                       |
| `ReadAsStringAsync()` en streaming                   | Rompe SSE                               |
| Ignorar `[DONE]`                                     | No cierra correctamente                 |
| Asumir tool_call completo en un chunk                | Llega fragmentado                       |
| Reutilizar CTS entre turnos                          | Viola ADR-003                           |
| Timeout global en `HttpClient.Timeout`               | Mata el stream a los 100 s aunque fluya |
| Log del `Authorization` header                       | Filtra API key                          |
| Retry sin backoff o sin tope                         | Machaca el endpoint                     |
| Retry tras cancelación de usuario                    | Ignora al usuario                       |
| Parsear SSE con `Split('\n\n')` sobre string gigante | Memoria O(n²)                           |

---

## Patrón de implementación

### Loop principal
```csharp
public async Task SendAsync(
    IReadOnlyList<ChatMessage> messages,
    IBlsNimSink sink,
    CancellationToken ct)
{
    using var response = await PostStreamingAsync(messages, ct);
    await using var stream = await response.Content.ReadAsStreamAsync(ct);
    using var reader = new StreamReader(stream);

    var toolCalls = new ToolCallAccumulator();

    while (true)
    {
        ct.ThrowIfCancellationRequested();

        using var chunkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        chunkCts.CancelAfter(TimeoutPerChunk);

        string line;
        try { line = await reader.ReadLineAsync(chunkCts.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new BlsNimTimeoutException(TimeoutPerChunk); }

        if (line == null) break;               // EOF sin [DONE] → protocolo raro
        if (line.Length == 0) continue;        // separador
        if (!line.StartsWith("data:")) continue;

        var data = line.Substring(5).TrimStart();
        if (data == "[DONE]") { sink.OnDone(); break; }

        ProcessChunk(data, sink, toolCalls);
    }

    foreach (var call in toolCalls.Complete())
        sink.OnToolCall(call);
}
```

### Cancelación
```csharp
try
{
    await SendAsync(..., ct);
}
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    sink.OnCancelled();     // NO es error
}
catch (BlsNimException ex)
{
    sink.OnError(ex);
}
```

---

## Checklist antes de commitear cambios en el cliente NIM

- [ ] ¿`HttpClient` es único (estático o inyectado)?
- [ ] ¿Se usa `ResponseHeadersRead`?
- [ ] ¿El timeout es por chunk (120 s), no global?
- [ ] ¿Los `tool_calls` se acumulan por `index`?
- [ ] ¿`[DONE]` cierra el loop?
- [ ] ¿El `CancellationToken` viene del turno (ADR-003)?
- [ ] ¿La cancelación NO se reporta como error?
- [ ] ¿Retry solo en 429/5xx con backoff y tope?
- [ ] ¿Retry respeta el token cancelado?
- [ ] ¿El body de error se preserva (truncado a 8 KB)?
- [ ] ¿Nunca se loguea ni serializa la API key?

---

## Referencias

- `AGENTS.md` regla 4 (API key), 5 (errores).
- `docs/design/ADR-003-abort-model.md`.
- PRD §3.3, §7 FR-09.
- `.opencode/skills/bls-secure-store-dpapi/SKILL.md` (origen de la API key).
- `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md` (ejecución de tool calls).
- `.opencode/skills/bls-winforms-threading/SKILL.md` (recepción de eventos en UI).
- `.opencode/skills/bls-logging-errors/SKILL.md` (timeout ≠ cancelación).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (fake `HttpMessageHandler`).

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
