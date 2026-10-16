---
version: 1.0.0
date: 2025-01-04
status: approved
iso_standard: [29148, 42010, 12207, 25010]
aligns_to: [PRD.md, AGENTS.md, ADR-001-PROJECT-IDENTITY.md, specs/005-secure-store.md, specs/006-chat-history.md]
tags: [design, architecture, dpapi, security, chat-history, persistence]
---

# Feature: Secure Store (DPAPI) + Chat History

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-04

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

## Depende de
- `docs/design/ADR-001-project-identity.md` (projectKey para historial).

---

## 1. Contexto

Dos responsabilidades relacionadas con **persistencia local de datos del
usuario** viven en `Helpers/`:

1. **`BlsSecureStore`**: guarda la API key de Nvidia NIM cifrada con
   DPAPI, atada al usuario de Windows. Nunca toca disco en claro.
2. **`BlsChatHistory`**: persiste la conversación por proyecto Access en
   un JSON, para restaurarla al reabrir la terminal.

Ambas comparten:
- Ciclo de vida del add-in (`%APPDATA%\Balansoft\AIAddin\`).
- Inyección de dependencias para tests (sin tocar el perfil del dev).
- Reglas duras de seguridad (skill bls-secure-store-dpapi).

Cubre:
- PRD §3.5 (API key en DPAPI).
- PRD §3.6 (historial persistente).
- PRD §7 FR-08 (DPAPI, prompt si falta).
- PRD §7 FR-10 (historial por proyecto).
- PRD §11 criterios 5, 6.

Y materializa:
- ADR-001 (projectKey).

---

## 2. Alcance

### Incluido
- Cifrado/descifrado DPAPI (`CurrentUser` + entropía fija).
- Persistencia binaria en `%APPDATA%\Balansoft\AIAddin\apikey.bin`.
- Diálogo modal de API key si no existe (delegado a UI, ver #3).
- Historial JSON por `projectKey` en
  `%APPDATA%\Balansoft\AIAddin\history\{projectKey}.json`.
- Escritura atómica (`.tmp` + `File.Replace`).
- Rotación de historial a 1 MB, con archivado (máx 5).
- Detección de historial corrupto → renombrar a `.corrupt` y empezar
  vacío.
- Debounce de guardado (500 ms) en ediciones rápidas.
- Tests sin tocar el perfil real (rutas temporales + `IDataProtector`
  fake).

### Excluido (fuera de MVP)
- Cifrado del historial (solo la API key se cifra).
- Migración de esquemas de historial (solo se lee `version == 1`).
- Sincronización entre máquinas (cada máquina tiene su perfil).
- Compresión del historial.
- Índice de búsqueda.
- Exportar/importar historial.
- Rotación de la API key.
- Múltiples API keys (una por usuario).

---

## 3. Interfaces nuevas

### 3.1 `IBlsSecureStore` (Helpers/)
Contrato público.

```csharp
namespace Balansoft.AIAddin.Helpers;

public interface IBlsSecureStore
{
    bool TryGetApiKey(out string? key);
    void SaveApiKey(string key);
    void DeleteApiKey();
}
```

**Notas:**
- `TryGetApiKey` **no lanza** si no existe: devuelve `false` y
  `key = null`.
- `TryGetApiKey` sí lanza `BlsSecureStoreException` si el archivo existe
  pero no se puede descifrar.
- `SaveApiKey` valida null/vacío/whitespace →
  `BlsInvalidParamsException`.
- `DeleteApiKey` es idempotente.

### 3.2 `IDataProtector` (Helpers/, interno)
Abstracción del cifrado, inyectable para tests.

```csharp
internal interface IDataProtector
{
    byte[] Protect(byte[] plaintext);
    byte[] Unprotect(byte[] ciphertext);
}
```

**Notas:**
- Implementación real: `DpapiDataProtector` (usa `ProtectedData`).
- Implementación de test: `FakeDataProtector` (XOR con clave fija).
- Inyectable en `BlsSecureStore` por constructor.

### 3.3 `BlsSecureStore` (Helpers/)
Implementación.

```csharp
public sealed class BlsSecureStore : IBlsSecureStore
{
    public BlsSecureStore(
        IDataProtector protector,
        string filePath,              // inyectable para tests
        IBlsLogger logger);

    public bool TryGetApiKey(out string? key);
    public void SaveApiKey(string key);
    public void DeleteApiKey();
}
```

**Nota:** la ruta por defecto
(`%APPDATA%\Balansoft\AIAddin\apikey.bin`) se calcula en un helper
`BlsPaths` (ver §3.7) y se pasa al constructor desde el host. En tests,
se inyecta una ruta temporal.

### 3.4 `IBlsChatHistory` (Helpers/)
Contrato público.

```csharp
public interface IBlsChatHistory
{
    Task<HistoryDocument> LoadAsync(
        string projectKey, CancellationToken ct);

    Task SaveAsync(
        string projectKey, HistoryDocument doc, CancellationToken ct);

    Task FlushAsync(CancellationToken ct);

    Task ClearAsync(
        string projectKey, CancellationToken ct);
}
```

**Notas:**
- `LoadAsync` **nunca lanza** por archivo corrupto o inexistente:
  devuelve un documento vacío y loguea `WARN`.
- `SaveAsync` con debounce 500 ms: la escritura real puede ocurrir
  después. Devuelve cuando el write está encolado, no cuando termina.
- `FlushAsync` fuerza la escritura pendiente (usado en cierre de
  terminal).
- `ClearAsync` borra el archivo (y solo ese).

### 3.5 `HistoryDocument` (Helpers/)
Modelo del historial. Serializado con `System.Text.Json`.

```csharp
public sealed class HistoryDocument
{
    public int Version { get; set; } = 1;
    public string ProjectKey { get; set; } = "";
    public bool Ephemeral { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public List<HistoryMessage> Messages { get; set; } = new();
}

public sealed class HistoryMessage
{
    public string Role { get; set; } = "";      // user | assistant | tool
    public string Content { get; set; } = "";
    public DateTime Ts { get; set; }
    public bool Cancelled { get; set; }
    public string? ToolName { get; set; }       // solo si Role == "tool"
    public string? ToolCallId { get; set; }     // solo si Role == "tool"
}
```

### 3.6 `BlsChatHistory` (Helpers/)
Implementación.

```csharp
public sealed class BlsChatHistory : IBlsChatHistory
{
    public BlsChatHistory(
        string directory,             // inyectable para tests
        IBlsLogger logger,
        TimeSpan? debounce = null);   // default 500ms

    // IBlsChatHistory
    public Task<HistoryDocument> LoadAsync(string projectKey, CancellationToken ct);
    public Task SaveAsync(string projectKey, HistoryDocument doc, CancellationToken ct);
    public Task FlushAsync(CancellationToken ct);
    public Task ClearAsync(string projectKey, CancellationToken ct);
}
```

### 3.7 `BlsPaths` (Helpers/, interno)
Centraliza rutas del perfil.

```csharp
internal static class BlsPaths
{
    public static string Root { get; }              // %APPDATA%\Balansoft\AIAddin
    public static string ApiKeyFile { get; }        // Root\apikey.bin
    public static string HistoryDir { get; }        // Root\history
    public static string LogsDir { get; }           // Root\logs
}
```

**Notas:**
- En producción, `Root` = `Path.Combine(Environment.GetFolderPath(
  SpecialFolder.ApplicationData), "Balansoft", "AIAddin")`.
- En tests, las clases que usan rutas las reciben por constructor (no
  consultan `BlsPaths` directamente).

### 3.8 `DpapiDataProtector` (Helpers/, interno)
Implementación real de `IDataProtector`.

```csharp
internal sealed class DpapiDataProtector : IDataProtector
{
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Balansoft.AIAddin.Access.v1");

    public byte[] Protect(byte[] plaintext)
        => ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

    public byte[] Unprotect(byte[] ciphertext)
        => ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
}
```

---

## 4. Invariantes afectados

| Fuente            | Invariante                      | Cómo se cumple                                      |
|-------------------|---------------------------------|-----------------------------------------------------|
| AGENTS.md regla 4 | API key solo DPAPI              | `DpapiDataProtector` con `CurrentUser`              |
| AGENTS.md regla 4 | Nunca en claro                  | Archivo binario, sin base64/JSON/logs               |
| ADR-001           | projectKey para historial       | `BlsChatHistory` recibe `projectKey` como parámetro |
| skill bls-secure-store-dpapi          | Entropía fija                   | `"Balansoft.AIAddin.Access.v1"`                     |
| skill bls-secure-store-dpapi          | Escritura atómica               | `.tmp` + `File.Replace`                             |
| skill bls-secure-store-dpapi          | Debounce 500 ms                 | `System.Threading.Timer` interno                    |
| skill bls-secure-store-dpapi          | Rotación a 1 MB, máx 5          | `RotateIfNeeded()`                                  |
| skill bls-secure-store-dpapi          | Fallo historial no bloquea chat | `LoadAsync` devuelve vacío en error                 |
| skill bls-logging-errors          | Nunca loguear API key           | Revisión de cada `logger.*` en la clase             |
| PRD FR-08         | Prompt si no existe             | El diálogo vive en UI, no en store                  |

---

## 5. Flujo interno

### `BlsSecureStore.TryGetApiKey`
```
1. if (!File.Exists(_filePath)) → return false, key=null
2. cipher = File.ReadAllBytes(_filePath)
3. try
     plain = _protector.Unprotect(cipher)
     key   = Encoding.UTF8.GetString(plain)
     return true, key
   catch (CryptographicException ex)
     _logger.Warn("BlsSecureStore", "No se pudo descifrar la API key");
     throw new BlsSecureStoreException(
         "Fallo al descifrar la API key. Vuelve a guardarla.", ex)
```

### `BlsSecureStore.SaveApiKey`
```
1. if (string.IsNullOrWhiteSpace(key)) → throw BlsInvalidParamsException
2. plain  = Encoding.UTF8.GetBytes(key)
3. cipher = _protector.Protect(plain)
4. File.WriteAllBytes(_filePath, cipher)
5. _logger.Info("BlsSecureStore", "API key guardada")   // sin valor
```

### `BlsSecureStore.DeleteApiKey`
```
1. if (File.Exists(_filePath)) File.Delete(_filePath)
2. _logger.Info("BlsSecureStore", "API key eliminada")
```

### `BlsChatHistory.LoadAsync`
```
1. path = Path.Combine(_directory, projectKey + ".json")
2. if (!File.Exists(path)) → return new HistoryDocument { ProjectKey=projectKey }
3. try
     json = File.ReadAllText(path, UTF8)
     doc  = JsonSerializer.Deserialize<HistoryDocument>(json)
     if (doc is null || doc.Version != 1)
         throw new BlsHistorySchemaException(doc?.Version ?? -1)
     return doc
   catch (JsonException ex)
     RenameCorrupt(path)                       // path + ".corrupt"
     _logger.Warn("BlsChatHistory", "Historial corrupto renombrado")
     return new HistoryDocument { ProjectKey=projectKey }
   catch (BlsHistorySchemaException ex)
     RenameCorrupt(path)
     _logger.Warn("BlsChatHistory", $"Versión desconocida: {ex.Version}")
     return new HistoryDocument { ProjectKey=projectKey }
   catch (IOException ex)
     _logger.Warn("BlsChatHistory", "No se pudo leer el historial")
     return new HistoryDocument { ProjectKey=projectKey }
```

### `BlsChatHistory.SaveAsync` (con debounce)
```
1. _pending[projectKey] = doc                // en memoria
2. _timer.Change(500ms, Timeout.Infinite)    // reinicia debounce
3. return Task.CompletedTask                 // no espera la escritura
```

### `BlsChatHistory.FlushAsync`
```
1. _timer.Change(Timeout.Infinite, Timeout.Infinite)   // detiene
2. foreach (key, doc) in _pending: WriteAtomic(key, doc)
3. _pending.Clear()
```

### `BlsChatHistory.WriteAtomic`
```
1. tmp    = Path.Combine(_directory, projectKey + ".json.tmp")
2. final  = Path.Combine(_directory, projectKey + ".json")
3. json   = JsonSerializer.Serialize(doc, options)
4. File.WriteAllText(tmp, json, UTF8)
5. RotateIfNeeded(final)                    // ver §6
6. if (File.Exists(final))
       File.Replace(tmp, final, destinationBackupFileName: null)
   else
       File.Move(tmp, final)
```

### `BlsChatHistory.RotateIfNeeded`
```
1. if (!File.Exists(path)) return
2. info = new FileInfo(path)
3. if (info.Length < 1MB) return
4. archive = Path.Combine(dir, $"{projectKey}.{yyyyMMdd-HHmmss}.json")
5. File.Move(path, archive)
6. PruneOldArchives(projectKey)             // borra > 5 más antiguos
```

---

## 6. Formato del historial (ADR-001)

### Nombre del archivo
`{projectKey}.json` en `%APPDATA%\Balansoft\AIAddin\history\`.

`projectKey` = resultado de `vbe.GetProjectKey().Key` (ADR-001).
Nunca contiene caracteres inválidos (hex 32 chars, o override validado
`[A-Za-z0-9_-]{1,64}`).

### Esquema JSON
```json
{
  "version": 1,
  "projectKey": "a3f1c8e2b904d7f6a1c2e3d4b5a6c7d8",
  "ephemeral": false,
  "updatedUtc": "2025-01-01T12:00:00Z",
  "messages": [
    {
      "role": "user",
      "content": "Crea un módulo Utils con una función Suma",
      "ts": "2025-01-01T11:59:00Z"
    },
    {
      "role": "assistant",
      "content": "Voy a crear el módulo Utils.",
      "ts": "2025-01-01T11:59:02Z",
      "cancelled": false
    },
    {
      "role": "tool",
      "content": "{\"ok\": true, \"module\": \"Utils\"}",
      "ts": "2025-01-01T11:59:03Z",
      "toolName": "create_module",
      "toolCallId": "call_x"
    }
  ]
}
```

### Reglas de serialización
- `System.Text.Json` con `PropertyNamingPolicy = CamelCase`.
- `DefaultIgnoreCondition = WhenWritingNull` (no escribe `cancelled`
  cuando es `false`, ni `toolName`/`toolCallId` cuando son null).
- `WriteIndented = false` (compacto).
- UTF-8 sin BOM.

### Archivos archivados
`{projectKey}.{yyyyMMdd-HHmmss}.json` en el mismo directorio.
Máximo 5; los más antiguos se borran.

### Archivos corruptos
`{projectKey}.json.corrupt` (renombrado). Se conserva para diagnóstico
manual; no se carga.

---

## 7. Criterios de aceptación

### CA-01 — API key: guardar y leer
- [ ] `SaveApiKey` escribe un archivo binario cifrado.
- [ ] El archivo **no** contiene el plaintext.
- [ ] `TryGetApiKey` devuelve la key original.
- [ ] `TryGetApiKey` sin archivo → `false`, `key=null`, sin excepción.
- **Tests:** `SecureStore_SaveApiKey_ArchivoNoContienePlaintext`,
  `SecureStore_TryGetApiKey_RoundTrip`,
  `SecureStore_TryGetApiKey_SinArchivo_RetornaFalseSinExcepcion`.

### CA-02 — API key: fallos
- [ ] Archivo corrupto → `BlsSecureStoreException`.
- [ ] `SaveApiKey(null)` → `BlsInvalidParamsException`.
- [ ] `SaveApiKey("")` → `BlsInvalidParamsException`.
- [ ] `SaveApiKey("   ")` → `BlsInvalidParamsException`.
- **Tests:** `SecureStore_ArchivoCorrupto_LanzaExcepcion`,
  `SecureStore_SaveApiKeyNull_LanzaInvalidParams`,
  `SecureStore_SaveApiKeyVacio_LanzaInvalidParams`,
  `SecureStore_SaveApiKeyWhitespace_LanzaInvalidParams`.

### CA-03 — API key: nunca en claro
- [ ] El logger nunca recibe la key.
- [ ] Ninguna excepción incluye la key en su `Message` o `Data`.
- [ ] El historial JSON nunca contiene la key.
- **Tests:** `SecureStore_LoggerNoRegistraApiKey`,
  `SecureStore_ExcepcionNoIncluyeApiKey`,
  `SecureStore_HistorialNoSerializaApiKey`.

### CA-04 — API key: delete
- [ ] `DeleteApiKey` borra el archivo.
- [ ] `DeleteApiKey` sin archivo → no falla (idempotente).
- **Tests:** `SecureStore_DeleteApiKey_BorraArchivo`,
  `SecureStore_DeleteApiKey_SinArchivo_NoFalla`.

### CA-05 — Historial: round-trip
- [ ] `SaveAsync` + `FlushAsync` + `LoadAsync` devuelve el mismo doc.
- [ ] Los campos opcionales nulos se omiten en el JSON.
- **Tests:** `History_GuardarYCargar_RoundTrip`,
  `History_SerializacionOmiteNulos`.

### CA-06 — Historial: archivo inexistente
- [ ] `LoadAsync` sin archivo devuelve doc vacío con `ProjectKey` correcto.
- [ ] Sin excepción, sin log de error (WARN no; INFO si acaso).
- **Test:** `History_LoadSinArchivo_DevuelveDocVacio`.

### CA-07 — Historial: archivo corrupto
- [ ] JSON inválido → renombra a `.corrupt`, devuelve doc vacío, log
      `WARN`.
- [ ] `version` desconocida → mismo comportamiento.
- **Tests:** `History_ArchivoCorrupto_RenombraYEmpiezaVacio`,
  `History_VersionDesconocida_RenombraYEmpiezaVacio`.

### CA-08 — Historial: debounce
- [ ] 10 `SaveAsync` seguidos en < 500 ms → una sola escritura.
- [ ] `FlushAsync` fuerza la escritura pendiente inmediatamente.
- **Tests:** `History_Debounce500ms_NoEscribePorCadaSave`,
  `History_FlushAsync_FuerzaEscritura`.

### CA-09 — Historial: escritura atómica
- [ ] Se escribe a `.tmp` y se hace `File.Replace`.
- [ ] Si el destino no existe, se hace `File.Move`.
- [ ] No queda `.tmp` tras una escritura exitosa.
- **Tests:** `History_EscrituraAtomica_NoDejaTmp`,
  `History_DestinoNoExiste_UsaMove`.

### CA-10 — Historial: rotación
- [ ] Archivo > 1 MB se archiva con timestamp.
- [ ] Se mantienen máx 5 archivados (los más viejos se borran).
- **Tests:** `History_Rotacion1MB_ArchivaYEmpiezaNuevo`,
  `History_Max5Archivados_BorraMasViejos`.

### CA-11 — Historial: marca `ephemeral`
- [ ] Doc con `Ephemeral=true` se persiste con la marca.
- [ ] Al cargar, se respeta la marca (la UI muestra aviso, ver #3).
- **Tests:** `History_Ephemeral_SeGuardaYSeCarga`.

### CA-12 — Historial: marca `cancelled` (ADR-003)
- [ ] Mensaje `assistant` con `Cancelled=true` se persiste.
- [ ] Al cargar, el campo se preserva.
- **Tests:** `History_Cancelled_SeGuardaYSeCarga`.

### CA-13 — Fallo de IO no bloquea
- [ ] Directorio sin permisos → `SaveAsync` no lanza; log `WARN`.
- [ ] `LoadAsync` con `IOException` → doc vacío; log `WARN`.
- **Tests:** `History_SaveIOPermiso_NoLanzaSoloWarn`,
  `History_LoadIOException_DevuelveDocVacio`.

---

## 8. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── Helpers/
│   ├── IBlsSecureStore.cs                  (nuevo)
│   ├── BlsSecureStore.cs                   (nuevo)
│   ├── IDataProtector.cs                   (nuevo, internal)
│   ├── DpapiDataProtector.cs               (nuevo, internal)
│   ├── IBlsChatHistory.cs                  (nuevo)
│   ├── BlsChatHistory.cs                   (nuevo)
│   ├── HistoryDocument.cs                  (nuevo)
│   ├── HistoryMessage.cs                   (nuevo)
│   ├── BlsPaths.cs                         (nuevo, internal)
│   └── Exceptions/
│       ├── BlsSecureStoreException.cs      (nuevo)
│       ├── BlsHistoryIOException.cs        (nuevo)
│       ├── BlsHistoryCorruptException.cs   (nuevo)
│       └── BlsHistorySchemaException.cs    (nuevo)

tests/Balansoft.AIAddin.Tests/
├── Helpers/
│   ├── BlsSecureStoreTests.cs              (nuevo)
│   ├── BlsChatHistoryTests.cs              (nuevo)
│   └── HistorySerializationTests.cs        (nuevo)
└── Fakes/
    ├── FakeDataProtector.cs                (nuevo)
    ├── FakeLogger.cs                       (ya existe)
    └── TempDirectory.cs                    (nuevo helper de tests)
```

---

## 9. Decisiones internas (no requieren ADR)

### 9.1 `IDataProtector` inyectable
Permite testear `BlsSecureStore` sin tocar DPAPI real. En producción se
inyecta `DpapiDataProtector`.

### 9.2 Rutas por constructor, no estáticas
`BlsSecureStore` recibe `filePath`; `BlsChatHistory` recibe `directory`.
En producción se pasan desde `BlsPaths`; en tests, temporales. Esto
permite tests paralelos sin colisión de archivos.

### 9.3 Debounce con `System.Threading.Timer`
No se usa `Task.Delay` porque múltiples `SaveAsync` deben **reiniciar**
el mismo timer, no crear timers paralelos. Un único `Timer` por
`BlsChatHistory` es suficiente y determinista.

### 9.4 `FlushAsync` en cierre y antes de rotación
Llamado explícitamente desde la terminal al cerrar. Sin él, un cierre
rápido perdería hasta 500 ms de mensajes.

### 9.5 Rotación antes de escribir
`RotateIfNeeded` se ejecuta **antes** del `File.Replace`. Así el archivo
nuevo empieza limpio en lugar de crecer hasta 2 MB antes de rotar.

### 9.6 Marcas `cancelled` y `ephemeral`
- `cancelled`: ADR-003. La terminal lo pasa al `HistoryMessage`.
- `ephemeral`: ADR-001. La terminal lo pasa al `HistoryDocument`.

Ninguna de las dos altera la lógica de store; solo se persisten.

### 9.7 Sin migración de esquema
`version != 1` → se renombra a `.corrupt` y se empieza vacío. No se
intenta migrar. Si en el futuro hace falta, se añade un ADR.

### 9.8 `.corrupt` no se borra automáticamente
Se deja para diagnóstico manual. El usuario puede borrarlo. No se
acumulan (se sobreescribe si ya existe).

### 9.9 El store no conoce la UI
`BlsSecureStore` **no** muestra el diálogo de API key. Solo intenta
leer; si no existe, devuelve `false`. La UI decide mostrar el prompt
(ver #3).

### 9.10 El store no conoce el bridge
`BlsSecureStore` es independiente. Solo el cliente NIM (#2) lo usa para
leer la key.

### 9.11 UTF-8 sin BOM
Consistente con el resto del proyecto (`System.Text.Json` +
`Encoding.UTF8` sin BOM explícito).

### 9.12 Pruning de archivados
Se listan los `{projectKey}.*.json` ordenados por fecha (del nombre),
se borran los más antiguos hasta dejar 5. Si el parseo de fecha falla,
se conserva (evita borrados accidentales).

---

## 10. Fuera de alcance (recordatorio)

- Cifrado del historial.
- Migración de versiones de esquema.
- Sincronización entre máquinas.
- Compresión.
- Índice de búsqueda.
- Exportar / importar.
- Rotación de API key.
- Múltiples API keys.
- Limpieza automática del historial por antigüedad.
- Panel de configuración de rutas.

---

## 11. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (regla 4 en particular).
2. `.opencode/skills/bls-project-context/SKILL.md`.
3. `.opencode/skills/bls-secure-store-dpapi/SKILL.md` (este dominio).
4. `.opencode/skills/bls-logging-errors/SKILL.md` (nunca loguear la key).
5. `.opencode/skills/bls-testing-strategy/SKILL.md` (fakes, temp dirs).
6. `docs/design/ADR-001-project-identity.md` (projectKey).

Opcional:
- `.opencode/skills/bls-nim-client-streaming/SKILL.md` (consumidor de la key).
- `.opencode/skills/bls-winforms-threading/SKILL.md` (consumidor del historial).

---

## 12. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~SecureStore"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~ChatHistory"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~HistorySerialization"
```

Criterio de éxito: build verde + tests de CA-01..CA-13 verdes.

---

## 13. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §7 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §11 están listados.
- [ ] ADR-001 está aprobado (dependencia).

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-13.
- [ ] No modificar interfaces del §3.
- [ ] Referenciar este doc en el commit:
      `feat: secure store DPAPI + chat history (refs design/20250104-secure-store.md)`.

---

## 14. Referencias

- `AGENTS.md` regla 4.
- `docs/design/ADR-001-project-identity.md`.
- `docs/design/ADR-003-abort-model.md` (marca `cancelled`).
- `PRD.md` §3.5, §3.6, §7 FR-08, FR-10, §11.
- `.opencode/skills/bls-secure-store-dpapi/SKILL.md`.
- `.opencode/skills/bls-logging-errors/SKILL.md`.
- `.opencode/skills/bls-testing-strategy/SKILL.md`.
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa secure store + historial (paralelo a #1, #2, #6, #7)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. docs/design/20250104-secure-store.md (el diseño)
4. Los skills citados en §11 del diseño
5. ADR-001 y ADR-003

Implementa la feature 'Secure Store + Chat History' exactamente como está
especificado.
No cambies interfaces del §3. No añadas features del §10.
Entrega diff completo + tests + comando de verificación del §12."
```

### Big Pickle revisa

```bash
opencode --model big-pickle \
"Actúa como arquitecto revisor del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee:
1. AGENTS.md
2. .opencode/skills/bls-workflow-nemotron/SKILL.md (checklist de rechazo)
3. docs/design/20250104-secure-store.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill bls-workflow-nemotron.
Presta especial atención a:
- API key nunca en claro (log, JSON, excepciones).
- DataProtectionScope.CurrentUser (no LocalMachine).
- Escritura atómica con .tmp + File.Replace.
- projectKey de ADR-001 (no VBProject.Name suelto).
- Fallo de historial nunca bloquea el chat.

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño

| # | Doc                                | Estado            | Depende de |
|---|------------------------------------|-------------------|------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado       | —          |
| 2 | `20250102-nim-client.md`           | ✅ Entregado       | —          |
| 3 | `20250103-nim-terminal.md`         | ✅ Entregado       | #1, #2     |
| 4 | `20250104-secure-store.md`         | ✅ Entregado       | ADR-001    |
| 5 | `20250105-chat-history.md`         | ⚠️ Fusionado en #4 | —          |
| 6 | `20250106-registration-scripts.md` | Pendiente         | —          |
| 7 | `20250107-logging-version.md`      | Pendiente         | —          |
| 8 | `20250108-addin-lifecycle.md`      | Pendiente         | Todos      |

**Nota:** el #5 (`chat-history`) quedó fusionado en #4 porque comparten
ciclo de vida, rutas y reglas de seguridad. Un solo PR los entrega.

**Ruta crítica hacia el MVP:**
```
#1 bridge ─┐
           ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client ─┘       ↑
                   │
#4 store ──────────┤ (alimenta #2 y #3)
#6 reg ────────────┘ (necesario para #8)

#7 log ── transversal (alimenta todo)
```

**Paralelización inmediata (4 frentes):**
- Frente A: #1 bridge
- Frente B: #2 client
- Frente C: #4 store + history
- Frente D: #6 reg + #7 log

Luego #3 terminal (necesita #1+#2+#4) y por último #8 lifecycle.
