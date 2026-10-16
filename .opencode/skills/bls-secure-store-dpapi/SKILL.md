---
name: bls-secure-store-dpapi
description: Almacenamiento seguro DPAPI para API key e historial conversacional atómico
---

# SKILL 05 — BlsSecureStore (DPAPI) + BlsChatHistory

> **Referencia ISO/IEC**: 27001 (Information security) + 29148 (Requirements)
> **Área**: security | dpapi | history | persistence
> **Estado**: approved
> **Última revisión**: 2026-10-15

> **Cuándo cargar este skill:** al implementar o modificar
> `Helpers/BlsSecureStore.cs`, `Helpers/BlsChatHistory.cs`, o cualquier
> código que lea/escriba la API key, o que persista/recupere
> conversaciones del usuario.

---

## Parte A — `BlsSecureStore` (API key DPAPI)

### Objetivo
Guardar la API key de Nvidia NIM **cifrada**, atada al usuario de
Windows, sin que jamás toque disco en claro, ni aparezca en logs, ni se
serialice en JSON.

### Scope DPAPI
- `DataProtectionScope.CurrentUser` **obligatorio**.
- **Nunca** `LocalMachine` (permitiría a otro usuario de la máquina
  descifrarla).
- Entropía adicional fija (defensa en profundidad contra otros procesos
  del mismo usuario):
  ```
  entropy = UTF8("Balansoft.AIAddin.Access.v1")
  ```

### Persistencia
- Archivo: `%APPDATA%\Balansoft\AIAddin\apikey.bin`
- Contenido: blob binario devuelto por `ProtectedData.Protect`.
- **Nunca** base64, **nunca** JSON, **nunca** texto plano.
- Permisos del archivo: por defecto del perfil (`%APPDATA%` ya es por
  usuario). No se tocan ACLs.

### API pública
```csharp
public interface IBlsSecureStore
{
    bool TryGetApiKey(out string key);
    void SaveApiKey(string key);
    void DeleteApiKey();
}
```

Reglas:
- `TryGetApiKey` **no lanza** si no existe; devuelve `false` y `key = null`.
- `TryGetApiKey` sí lanza `BlsSecureStoreException` si el archivo existe
  pero no se puede descifrar (perfil corrupto, otro usuario, entropía
  cambiada).
- `SaveApiKey` valida que `key` no sea null/vacío/whitespace → si lo es,
  `BlsInvalidParamsException`.
- `DeleteApiKey` es idempotente: si no existe, no falla.

### Flujo del prompt
Al enviar el primer prompt de un turno:

```
1. TryGetApiKey(out key)
2. Si true  → usar key
3. Si false → mostrar diálogo modal:
      - Título: "API key de Nvidia NIM"
      - TextBox con PasswordChar = '●'
      - Botones: Guardar / Cancelar
      - Texto: "La API key se guardará cifrada en tu perfil de Windows."
   - Si Guardar  → SaveApiKey(key); continuar con el envío
   - Si Cancelar → abortar el envío con OnError("API key no configurada")
                   NO guardar nada
```

### Inyección para tests
El cifrado se abstrae en `IDataProtector`:

```csharp
public interface IDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}

internal sealed class DpapiDataProtector : IDataProtector { /* ProtectedData */ }
```

`BlsSecureStore` recibe `IDataProtector` + `string filePath` por
constructor. Tests inyectan un fake que hace XOR con clave fija, y
verifican que **nunca** se escribe el plaintext al disco.

### Prohibiciones absolutas
- **Nunca** loguear la API key (ni completa ni parcial, ni su hash).
- **Nunca** incluirla en excepciones (`ex.Data`, `message`, `ToString`).
- **Nunca** serializarla en JSON (historial, config, diagnóstico).
- **Nunca** escribirla en el registro de Windows.
- **Nunca** mostrarla en la UI tras guardada (solo permitir reemplazar).
- **Nunca** enviarla a ningún endpoint distinto de
  `https://integrate.api.nvidia.com`.

### Errores
| Excepción                   | Cuándo                                           |
|-----------------------------|--------------------------------------------------|
| `BlsSecureStoreException`   | Fallo DPAPI (perfil corrupto, entropía inválida) |
| `BlsInvalidParamsException` | `SaveApiKey` con null/vacío/whitespace           |
| `IOException`               | Fallo de escritura del archivo (permisos)        |

Los mensajes de error **no** incluyen la key.

---

## Parte B — `BlsChatHistory` (historial por proyecto)

### Objetivo
Persistir la conversación de cada proyecto Access en un JSON, para que
al reabrir la terminal el usuario recupere su contexto.

### Ubicación y nombre (ADR-001)
- Directorio: `%APPDATA%\Balansoft\AIAddin\history\`
- Archivo: `{projectKey}.json`
- `projectKey` = `BlsVbeService.GetProjectKey().Key`
  (SHA256 hex 32 chars, o override validado, o fallback efímero).

### Esquema
```json
{
  "version": 1,
  "projectKey": "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
  "ephemeral": false,
  "updatedUtc": "2025-01-01T12:00:00Z",
  "messages": [
    { "role": "user",      "content": "...", "ts": "2025-01-01T11:59:00Z" },
    { "role": "assistant", "content": "...", "ts": "2025-01-01T11:59:02Z",
      "cancelled": false },
    { "role": "tool",      "content": "{\"ok\":true}", "ts": "...",
      "toolName": "write_module", "toolCallId": "call_x" }
  ]
}
```

### API pública
```csharp
public interface IBlsChatHistory
{
    Task<HistoryDocument> LoadAsync(string projectKey, CancellationToken ct);
    Task SaveAsync(string projectKey, HistoryDocument doc, CancellationToken ct);
    Task ClearAsync(string projectKey, CancellationToken ct);
}
```

### Reglas de uso
- **Carga** al abrir la terminal (`LoadAsync` con el `projectKey` actual).
- **Guarda** tras cada mensaje (usuario, assistant, tool), con **debounce
  500 ms** para no escribir por cada chunk de streaming.
- **Guarda final** al cerrar la terminal (flush sin debounce).
- El `CancellationToken` respeta el turno (ADR-003); si se cancela el
  guardado, se reintenta al cierre.

### Rotación y archivado
- Si el archivo supera **1 MB**:
  - Renombrar el actual a `{projectKey}.{yyyyMMdd-HHmmss}.json`.
  - Empezar uno nuevo con `messages: []` y `updatedUtc` actual.
  - Máximo **5 archivos** archivados; los más antiguos se borran.
- El archivo archivado **no** se carga al abrir; solo está para
  inspección manual.

### Escritura atómica
Prohibido escribir directamente sobre el archivo destino:

```
1. Serializar a {projectKey}.json.tmp
2. File.Replace(tmp, dest, backup=null)   // atómico en NTFS
3. Si File.Replace falla (dest no existe) → File.Move(tmp, dest)
```

Esto evita archivos corruptos si Access se cierra a mitad de escritura.

### Serialización
- `System.Text.Json` (mismo que el resto del proyecto).
- `JsonSerializerOptions`:
  - `WriteIndented = false` (compacto).
  - `PropertyNamingPolicy = CamelCase`.
  - `DefaultIgnoreCondition = WhenWritingNull`.
- UTF-8 sin BOM.

### Marca de cancelación (ADR-003)
- Si un turno se cancela, el último mensaje `assistant` se guarda con
  `"cancelled": true`.
- El contenido parcial recibido **se conserva** (útil para el usuario).
- Al reabrir, la terminal muestra el mensaje con un indicador visual
  ("[cancelado]").

### Marca de efímero (ADR-001)
- Si `projectKey` proviene del fallback efímero, el documento se guarda
  con `"ephemeral": true`.
- La terminal muestra aviso: *"Historial no persistente: guarda la base
  para conservar el chat."*
- Al cerrar Access, el archivo **no** se borra (podría reabrirse en la
  misma sesión de Windows), pero se documenta que no es fiable.

### Errores
| Excepción                    | Cuándo                     | Acción                                            |
|------------------------------|----------------------------|---------------------------------------------------|
| `BlsHistoryIOException`      | Fallo IO al cargar/guardar | Log `WARN`, continuar sin historial               |
| `BlsHistoryCorruptException` | JSON inválido al cargar    | Log `WARN`, renombrar a `.corrupt`, empezar vacío |
| `BlsHistorySchemaException`  | `version` desconocida      | Log `WARN`, empezar vacío (no migrar)             |

**Regla de oro:** un fallo de historial **nunca** bloquea el chat. Si no
se puede cargar, la terminal arranca vacía; si no se puede guardar, se
loguea y se sigue.

---

## Anti-patrones prohibidos (rechazo automático en PR)

| Anti-patrón                                | Por qué                       |
|--------------------------------------------|-------------------------------|
| `DataProtectionScope.LocalMachine`         | Otro usuario podría descifrar |
| API key en JSON/config/registry en claro   | Viola AGENTS.md regla 4       |
| API key en logs o excepciones              | Fuga trivial                  |
| Escribir directo sobre `{projectKey}.json` | Corrupción en crash           |
| `File.WriteAllText` sin tmp + Replace      | Mismo motivo                  |
| Guardar en cada chunk sin debounce         | IO masivo, disco caliente     |
| Cargar historial > 1 MB sin rotar          | RAM y latencia                |
| Bloquear el chat si falla el historial     | Degrada UX innecesariamente   |
| Migrar esquema sin `version`               | Pierde compatibilidad         |
| Mockear `ProtectedData` en producción      | Rompe la abstracción de tests |

---

## Checklist antes de commitear cambios en secure store / history

- [ ] ¿`DataProtectionScope.CurrentUser`?
- [ ] ¿Entropía adicional fija `"Balansoft.AIAddin.Access.v1"`?
- [ ] ¿El archivo es binario (no base64, no JSON)?
- [ ] ¿`TryGetApiKey` no lanza si no existe?
- [ ] ¿La key nunca aparece en logs, excepciones ni JSON?
- [ ] ¿El prompt modal tiene `PasswordChar` y opción cancelar?
- [ ] ¿`IDataProtector` es inyectable para tests?
- [ ] ¿El historial usa `projectKey` de ADR-001?
- [ ] ¿Escritura atómica con `.tmp` + `File.Replace`?
- [ ] ¿Debounce 500 ms en guardado?
- [ ] ¿Rotación a los 1 MB con máximo 5 archivos archivados?
- [ ] ¿Marca `cancelled` y `ephemeral` cuando aplica?
- [ ] ¿Un fallo de historial nunca bloquea el chat?

---

## Referencias

- `AGENTS.md` regla 4 (API key solo DPAPI).
- `docs/design/ADR-001-PROJECT-IDENTITY.md`.
- `docs/design/ADR-003-ABORT-MODEL.md` (marca `cancelled`).
- PRD §3.5, §3.6, §7 FR-08, FR-10.
- `.opencode/skills/bls-vbe-automation/SKILL.md` (origen de `projectKey`).
- `.opencode/skills/bls-nim-client-streaming/SKILL.md` (consumidor de la API key).
- `.opencode/skills/bls-logging-errors/SKILL.md` (nunca loguear la key).
- `.opencode/skills/bls-testing-strategy/SKILL.md` (fake `IDataProtector`).

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
