# Feature: Logging + Version (BlsLogger, BlsVersion)

## Estado
Aprobado para implementación.

## Autor
Big Pickle (arquitecto).

## Fecha
2025-01-07

## Modelo objetivo
Nemotron 3 Ultra (implementación) + Big Pickle (revisión).

## Depende de
- `docs/design/20250106-registration-scripts.md` (version.txt en el ZIP).

---

## 1. Contexto

Dos utilidades transversales que **todo** el resto del código usa:

1. **`BlsLogger`**: log diario con rotación de 7 días, thread-safe,
   formato canónico, y prohibición absoluta de loguear la API key.
2. **`BlsVersion`**: versión + commit del build, usada en la primera
   línea del log del día, en `version.txt` del ZIP y en el diagnóstico
   de arranque.

Sin ellas no hay forma de:
- Diagnosticar fallos en producción (skill bls-logging-errors).
- Saber qué versión está corriendo cada usuario.
- Cumplir la promesa de "versión + commit en primera línea del log"
  (PRD FR-07).

Cubre:
- PRD §7 FR-07 (logger rotación 7 días, captura global, versión+commit).
- PRD §11 criterio 8 (logger rota, captura excepciones, versión+commit).

Y materializa:
- AGENTS.md regla 4 (nunca API key en log).
- AGENTS.md regla 5 (errores reales, nunca éxito falso).
- skill bls-logging-errors (logging y propagación de errores).

---

## 2. Alcance

### Incluido
- `BlsLogger` con rotación diaria de 7 días.
- Formato canónico de línea.
- Primera línea del día con contexto (versión + commit + OS + Access).
- Escritura thread-safe con lock interno.
- 3 handlers globales de excepciones no controladas.
- `BlsVersion` con versión, commit, OS, bitness.
- `BlsVersion.IsDebug` para condicionar detalles.
- Tests sin escribir en `%APPDATA%` real.

### Excluido (fuera de MVP)
- Logging estructurado (JSON).
- Envío de logs a servidor remoto.
- Rotación por tamaño dentro del mismo día.
- Niveles configurables en runtime.
- Filtros por componente.
- Compresión de logs antiguos.
- Firma digital del log.
- Auditoría de usuario (qué usuario hizo qué).
- Versionado semántico automático.

---

## 3. Interfaces nuevas

### 3.1 `IBlsLogger` (Helpers/)
Contrato público.

```csharp
namespace Balansoft.AIAddin.Helpers;

public interface IBlsLogger
{
    void Debug(string component, string message);
    void Info (string component, string message);
    void Warn (string component, string message);
    void Error(string component, string message, Exception? ex = null);
}
```

**Notas:**
- `component` = nombre de clase sin namespace (ej. `BlsNimClient`).
- `message` = string en español, una línea (se escapan `\n` internos).
- `Error` con `ex` añade una línea adicional con la excepción.

### 3.2 `BlsLogger` (Helpers/)
Implementación.

```csharp
public sealed class BlsLogger : IBlsLogger, IDisposable
{
    public BlsLogger(
        string directory,               // inyectable para tests
        BlsVersion version,
        bool debugEnabled = false,      // true en build Debug
        TimeSpan? retention = null);    // default 7 días

    public void Debug(string component, string message);
    public void Info (string component, string message);
    public void Warn (string component, string message);
    public void Error(string component, string message, Exception? ex = null);

    public void Dispose();
}
```

**Notas:**
- El constructor **no** crea el directorio; lo hace la primera escritura.
- El constructor **sí** ejecuta la rotación (borra > 7 días) al
  instanciarse, una vez por proceso.
- `Dispose` cierra el `StreamWriter` si está abierto.
- En tests, `debugEnabled=true` y `directory` temporal.

### 3.3 `BlsVersion` (Helpers/)
Metadatos del build.

```csharp
public sealed class BlsVersion
{
    public string Version { get; }         // "0.1.0"
    public string Commit { get; }          // "a3f1c8e" o "unknown"
    public string OsVersion { get; }       // "Windows 10.0.19045"
    public string Bitness { get; }         // "x86"
    public bool IsDebug { get; }           // true en build Debug

    public static BlsVersion Current { get; }
}
```

**Notas:**
- `Current` es un singleton lazy.
- `Version` lee de `AssemblyInformationalVersionAttribute`.
- `Commit` lee en orden:
  1. `AssemblyMetadataAttribute("Commit")` si existe.
  2. `version.txt` en el directorio del assembly (segunda línea).
  3. `"unknown"`.
- `OsVersion` = `Environment.OSVersion.VersionString`.
- `Bitness` = `"x86"` (fijo; el proyecto solo soporta x86).
- `IsDebug` = compilado con `#if DEBUG`.

### 3.4 `BlsVersionContext` (Helpers/, interno)
Helper para la primera línea del log del día.

```csharp
internal static class BlsVersionContext
{
    // Se llama una vez por proceso, antes de la primera escritura.
    public static string BuildFirstLine(BlsVersion version, string? accessVersion);
}
```

**Notas:**
- `accessVersion` lo provee el host (`Application.Version` late-bound) en
  el arranque. Si es `null`, se omite en la línea.
- El resto del contexto viene de `BlsVersion`.

### 3.5 `GlobalExceptionHandlers` (Core/, interno)
Registro de los 3 handlers. Vive en `Core/` porque se invoca desde
`OnConnection`.

```csharp
internal static class GlobalExceptionHandlers
{
    public static void Register(IBlsLogger logger);
    public static void Unregister();
}
```

**Notas:**
- `Register` es idempotente: no duplica handlers si se llama dos veces.
- `Unregister` quita los handlers (para tests o shutdown).

---

## 4. Invariantes afectados

| Fuente            | Invariante                        | Cómo se cumple                                    |
|-------------------|-----------------------------------|---------------------------------------------------|
| AGENTS.md regla 4 | Nunca API key en log              | Revisión de cada llamada + test                   |
| AGENTS.md regla 5 | Errores reales                    | `Error(ex)` incluye tipo + mensaje + HResult      |
| PRD FR-07         | Rotación 7 días                   | `RotateOldLogs()` en constructor                  |
| PRD FR-07         | Captura global                    | `GlobalExceptionHandlers.Register`                |
| PRD FR-07         | Versión + commit en primera línea | `BlsVersionContext.BuildFirstLine`                |
| skill bls-logging-errors          | Formato canónico                  | `Write(...)` centralizado                         |
| skill bls-logging-errors          | Nivel alineado 5 chars            | `level.PadRight(5)`                               |
| skill bls-logging-errors          | Stack solo en Debug               | `#if DEBUG` en `Error`                            |
| skill bls-logging-errors          | Cancelación ≠ error               | `BlsLogger` no distingue; lo hacen los llamadores |
| skill bls-logging-errors          | No loguear código VBA completo    | Llamadores truncan a 80 chars                     |

---

## 5. Flujo interno

### Arranque (una vez por proceso)
```
1. BlsVersion.Current (lazy)
2. BlsLogger(directory: BlsPaths.LogsDir, version: Current, debugEnabled)
3. Constructor de BlsLogger:
   a. Directory.CreateDirectory(directory)   // idempotente
   b. RotateOldLogs()                        // borra > 7 días
4. GlobalExceptionHandlers.Register(logger)
5. Logger.Info("BlsVersion",
     $"Balansoft.AIAddin.Access v{version.Version} commit={version.Commit} " +
     $"os={version.OsVersion} bitness={version.Bitness}")
```

### `Write` (interno)
```
private void Write(string level, string component, string message)
{
    lock (_lock)
    {
        EnsureWriter();                       // abre/rota si cambió el día
        var ts = DateTime.UtcNow.ToString("O");
        var msg = Escape(message);
        _writer!.WriteLine($"{ts} [{level,-5}] [{component}] {msg}");
    }
}
```

### `EnsureWriter`
```
1. hoy = DateTime.UtcNow.Date
2. if (_writer != null && _currentDay == hoy) return
3. if (_writer != null) { _writer.Dispose(); _writer = null; }
4. _currentDay = hoy
5. path = Path.Combine(_directory, $"balansoft-{hoy:yyyyMMdd}.log")
6. _writer = new StreamWriter(path, append: true, Encoding.UTF8)
   { AutoFlush = true }
7. if (firstWriteOfDay)
   {
       var accessVersion = TryGetAccessVersion();
       _writer.WriteLine(BlsVersionContext.BuildFirstLine(_version, accessVersion));
       _writer.WriteLine();      // separador
       firstWriteOfDay = false;
   }
```

**Nota:** `firstWriteOfDay` es local a la instancia del logger. Si el
proceso se reinicia el mismo día, la línea de contexto se vuelve a
escribir. Es aceptable (marca el inicio de la sesión).

### `Escape`
```
- Reemplaza \r\n, \n, \r por "\\n" literal.
- Trunca a 4000 chars (evita líneas gigantes).
- Nunca toca el contenido (no sanitiza la API key; eso es responsabilidad
  del llamador).
```

### `RotateOldLogs`
```
1. cutoff = DateTime.UtcNow.Date.AddDays(-7)
2. foreach file in Directory.EnumerateFiles(_directory, "balansoft-*.log")
     a. name = Path.GetFileNameWithoutExtension(file)
     b. if name.StartsWith("balansoft-") && name.Length >= 19
        {
            var datePart = name.Substring(10, 8);     // yyyyMMdd
            if (DateTime.TryParseExact(datePart, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                && d < cutoff)
            {
                try { File.Delete(file); }
                catch (IOException) { /* log WARN si el writer está abierto */ }
            }
        }
        else
        {
            // nombre no parseable → no borrar
            Warn("BlsLogger", $"Archivo de log con nombre inesperado: {name}");
        }
```

### `Error` con excepción
```
Write("ERROR", component, message);
if (ex != null)
{
    var detail = BuildExceptionDetail(ex);
    Write("ERROR", component, detail);
}

private string BuildExceptionDetail(Exception ex)
{
    var hr = (ex as COMException)?.HResult.ToString("X8");
    #if DEBUG
        return $"  └─ {ex.GetType().Name}" +
               (hr != null ? $" (0x{hr})" : "") +
               $": {ex.Message}\n{ex.StackTrace}";
    #else
        return $"  └─ {ex.GetType().Name}" +
               (hr != null ? $" (0x{hr})" : "") +
               $": {ex.Message}";
    #endif
}
```

**Reglas:**
- El `Message` de la excepción **nunca** contiene la API key (responsabilidad
  de quien la lanza).
- El `StackTrace` solo en Debug.
- El HResult se muestra como hex si la excepción es `COMException`.

---

## 6. Formato del log

### Nombre
`balansoft-yyyyMMdd.log` (ej. `balansoft-20250107.log`).
Directorio: `%APPDATA%\Balansoft\AIAddin\logs\`.

### Primera línea del día (una por proceso)
```
2025-01-07T08:00:00.0000000Z [INFO ] [BlsVersion] Balansoft.AIAddin.Access v0.1.0 commit=a3f1c8e os=Microsoft Windows NT 10.0.19045.0 bitness=x86 access=16.0.14332.20556
```

### Líneas normales
```
2025-01-07T08:00:01.1234567Z [INFO ] [BlsNimClient] Streaming iniciado (turnId=42)
2025-01-07T08:00:02.2345678Z [INFO ] [BlsNimClient] Chunk recibido (12 bytes)
2025-01-07T08:00:03.3456789Z [WARN ] [BlsChatHistory] Historial no cargable, se empieza vacío
2025-01-07T08:00:04.4567890Z [ERROR] [BlsVbeService] write_module falló: Módulo 'X' no encontrado
2025-01-07T08:00:04.4567891Z [ERROR] [BlsVbeService]   └─ BlsVbeException: Módulo 'X' no encontrado
```

### Reglas del formato
- Timestamp ISO 8601 en UTC (`"O"`), 7 decimales.
- Nivel alineado a 5 chars (`DEBUG`, `INFO `, `WARN `, `ERROR`).
- Componente entre `[...]`.
- Mensaje en español, una línea.
- Detalles de excepción en líneas subsiguientes con prefijo `  └─ `.
- **Nunca** se loguea la API key.

---

## 7. Captura global de excepciones

### Registro
Se llama en `OnConnection` (después de crear el logger, antes de crear
el bridge):

```csharp
GlobalExceptionHandlers.Register(logger);
```

### Handlers

```csharp
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    _logger.Error("AppDomain", "Excepción no controlada",
                  e.ExceptionObject as Exception);
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

### Reglas
- `Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException)`
  se llama en `OnConnection` **antes** de crear cualquier Form.
- `Register` es idempotente: usa una bandera estática.
- `Unregister` quita los handlers (para tests o shutdown).
- El `MessageBox` de `Application.ThreadException` es la única UI que se
  muestra desde un handler global. Está en el hilo UI, es seguro.

---

## 8. `BlsVersion` — resolución de Commit

### Orden de búsqueda
```
1. AssemblyMetadataAttribute("Commit")
     Buscar en el assembly actual:
       var attrs = Assembly.GetExecutingAssembly()
                           .GetCustomAttributes<AssemblyMetadataAttribute>();
       var commit = attrs.FirstOrDefault(a => a.Key == "Commit")?.Value;
     Si no es null ni vacío → usarlo.

2. version.txt en el directorio del assembly
     var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
     var path = Path.Combine(dir, "version.txt");
     if (File.Exists(path))
     {
         var lines = File.ReadAllLines(path);
         if (lines.Length >= 2 && !string.IsNullOrWhiteSpace(lines[1]))
             return lines[1].Trim();
     }

3. "unknown"
```

### `Version`
```csharp
var info = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion;
return info ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
```

### `IsDebug`
```csharp
#if DEBUG
    public bool IsDebug => true;
#else
    public bool IsDebug => false;
#endif
```

---

## 9. Criterios de aceptación

### CA-01 — Formato canónico
- [ ] Línea `INFO` tiene el formato esperado.
- [ ] Nivel alineado a 5 chars.
- [ ] Timestamp ISO 8601 UTC.
- **Tests:** `Logger_FormatoInfo_LineaCanonica`,
  `Logger_NivelAlineado5Chars`,
  `Logger_TimestampUtcIso8601`.

### CA-02 — Rotación 7 días
- [ ] Logs > 7 días se borran al instanciar.
- [ ] Logs de ≤ 7 días se conservan.
- [ ] Nombres no parseables no se borran.
- **Tests:** `Logger_Rotacion7Dias_BorraAntiguos`,
  `Logger_Rotacion7Dias_ConservaRecientes`,
  `Logger_NombreNoParseable_NoBorra`.

### CA-03 — Primera línea del día
- [ ] La primera escritura del día incluye versión + commit + OS +
      bitness + Access.
- [ ] Se escribe una sola vez por instancia.
- **Tests:** `Logger_PrimeraLineaIncluyeContexto`,
  `Logger_PrimeraLineaSoloUnaVez`.

### CA-04 — Thread safety
- [ ] 100 escrituras concurrentes no corrompen el archivo.
- [ ] Todas las líneas están completas.
- **Test:** `Logger_100EscriturasConcurrentes_SinCorrupcion`.

### CA-05 — Escape de saltos
- [ ] Mensaje con `\n` interno se escribe como una sola línea.
- [ ] El escape es `\\n` literal.
- **Tests:** `Logger_MensajeConSalto_EscapaComoLiteral`,
  `Logger_MensajeLargo_TruncaA4000`.

### CA-06 — Nunca API key
- [ ] `Logger` no tiene ninguna ruta que reciba la key directamente.
- [ ] (Test de integración) Tras un turno completo, el archivo de log no
      contiene la key.
- **Tests:** `Logger_NoContieneApiKeyTrasTurnoCompleto` (integración
  ligera con `FakeSecureStore`).

### CA-07 — Detalles de excepción
- [ ] `Error(ex)` escribe tipo + mensaje.
- [ ] `Error(COMException)` incluye HResult en hex.
- [ ] En Debug, incluye stack; en Release, no.
- **Tests:** `Logger_ErrorConExcepcion_IncluyeTipoYMensaje`,
  `Logger_ErrorComException_IncluyeHResultHex`,
  `Logger_ErrorRelease_NoIncluyeStack` (condicional al build).

### CA-08 — `BlsVersion.Commit`
- [ ] Lee `AssemblyMetadata("Commit")` si existe.
- [ ] Fallback a `version.txt` (segunda línea).
- [ ] Fallback a `"unknown"`.
- **Tests:** `Version_CommitDesdeMetadata`,
  `Version_CommitDesdeVersionTxt`,
  `Version_CommitSinFuente_RetornaUnknown`.

### CA-09 — `BlsVersion.Version`
- [ ] Lee `AssemblyInformationalVersionAttribute`.
- [ ] Fallback a `AssemblyVersion`.
- **Tests:** `Version_VersionDesdeInformational`,
  `Version_VersionFallbackAssemblyVersion`.

### CA-10 — `BlsVersion.IsDebug`
- [ ] `true` en Debug, `false` en Release.
- **Tests:** condicionales al build (2 tests, uno se salta).

### CA-11 — Handlers globales
- [ ] `Register` es idempotente (llamarlo dos veces no duplica).
- [ ] `Unregister` los quita.
- [ ] `UnobservedTaskException` llama `SetObserved()`.
- **Tests:** `Global_Register_EsIdempotente`,
  `Global_Unregister_QuitaHandlers`,
  `Global_UnobservedTaskException_SetObserved`.

### CA-12 — Nunca relanzar
- [ ] `UnhandledException` no relanza.
- [ ] El proceso sobrevive a una excepción no controlada.
- **Test:** `Global_UnhandledException_NoRelanza` (test de integración
  ligero, sin UI).

### CA-13 — `BlsVersionContext`
- [ ] Con `accessVersion` no null, incluye `access=...`.
- [ ] Con `accessVersion` null, omite `access=`.
- **Tests:** `Context_ConAccessVersion_IncluyeAccess`,
  `Context_SinAccessVersion_OmiteAccess`.

---

## 10. Estructura de archivos

```
src/Balansoft.AIAddin.Access/
├── Core/
│   └── GlobalExceptionHandlers.cs                (nuevo, internal)
└── Helpers/
    ├── IBlsLogger.cs                             (nuevo)
    ├── BlsLogger.cs                              (nuevo)
    ├── BlsVersion.cs                             (nuevo)
    ├── BlsVersionContext.cs                      (nuevo, internal)
    └── BlsPaths.cs                               (ya existe del #4)

tests/Balansoft.AIAddin.Tests/
├── Helpers/
│   ├── BlsLoggerTests.cs                         (nuevo)
│   ├── BlsVersionTests.cs                        (nuevo)
│   └── BlsVersionContextTests.cs                 (nuevo)
├── Core/
│   └── GlobalExceptionHandlersTests.cs           (nuevo)
└── Fakes/
    └── TempDirectory.cs                          (ya existe del #4)
```

---

## 11. Decisiones internas (no requieren ADR)

### 11.1 Log en UTC, no local
El log en UTC evita ambigüedades en cambios de horario y permite
correlacionar con logs de servidor. La UI puede mostrar local; el log
no.

### 11.2 Rotación en constructor, no en cada escritura
Borrar logs viejos en cada `Write` es caro. Una vez por proceso es
suficiente.

### 11.3 `AutoFlush = true`
Cada línea se escribe a disco inmediatamente. Es más lento, pero
garantiza que un crash no pierda las últimas líneas. Aceptable porque
el volumen es bajo (unos pocos cientos de líneas por sesión).

### 11.4 `Escape` trunca a 4000 chars
Líneas gigantes rompen el parseo manual y llenan el disco. 4000 es
suficiente para cualquier mensaje razonable.

### 11.5 Stack solo en Debug
En Release, el stack revela rutas internas, nombres de métodos y
estructura del código. Se omite por seguridad. En Debug, ayuda.

### 11.6 `BlsVersion` singleton lazy
Se accede desde el logger (una vez por proceso) y desde el log de
arranque. Un singleton lazy con `Lazy<BlsVersion>` es suficiente.

### 11.7 `version.txt` en el ZIP
El ZIP de #6 incluye `version.txt` con versión + commit. En el repo no
se versiona; en el directorio del assembly en el ZIP sí existe.
`BlsVersion.Commit` lo encuentra si `AssemblyMetadata` no está.

### 11.8 `GlobalExceptionHandlers` idempotente
Se puede llamar `Register` varias veces sin duplicar handlers. Esto
permite llamarlo defensivamente desde `OnConnection` sin preocuparse
por recargas del add-in.

### 11.9 `Application.ThreadException` con MessageBox
Es la única forma de que el usuario se entere de un error en el hilo UI
sin que la app muera silenciosamente. El MessageBox es modal y bloquea
hasta que el usuario acepta. Está en el hilo UI, es seguro.

### 11.10 `UnhandledException` sin MessageBox
Puede venir de un hilo no-UI. Mostrar un MessageBox ahí es peligroso.
Solo se loguea.

### 11.11 `BlsVersion.IsDebug` en el log
El campo `IsDebug` **no** se incluye en la primera línea del log. Se
puede inferir del entorno (build). Mantener la primera línea corta.

### 11.12 Sin sanitización de la API key en `BlsLogger`
El logger **no** sabe qué es una API key. Es responsabilidad de cada
llamador no pasarla. Los tests de integración verifican que ningún
flujo la pase. La alternativa (regex para detectar patrones de Nvidia)
es frágil y se descarta.

---

## 12. Fuera de alcance (recordatorio)

- Logging estructurado (JSON).
- Envío a servidor remoto.
- Rotación por tamaño dentro del día.
- Niveles configurables en runtime.
- Filtros por componente.
- Compresión.
- Firma del log.
- Auditoría de usuario.
- Versionado semántico automático.
- Detección automática de API key.

---

## 13. Skills que el implementador (Nemotron 3 Ultra) debe leer

En este orden:

1. `AGENTS.md` (reglas 4, 5).
2. `.opencode/skills/bls-project-context/SKILL.md`.
3. `.opencode/skills/bls-logging-errors/SKILL.md` (este dominio).
4. `.opencode/skills/bls-testing-strategy/SKILL.md` (temp dirs, fakes).
5. `docs/design/20250106-registration-scripts.md` (version.txt en el
   ZIP).

Opcional:
- `.opencode/skills/bls-registration-scripts/SKILL.md` (relación con el
  packaging).

---

## 14. Comando de verificación

```powershell
# Build
msbuild Balansoft.AIAddin.sln /p:Configuration=Release /p:Platform=x86 /v:minimal

# Tests
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Logger"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Version"
dotnet test --no-build --settings .runsettings --filter "FullyQualifiedName~Global"
```

Criterio de éxito: build verde + tests de CA-01..CA-13 verdes.

---

## 15. Checklist de handoff (Big Pickle → Nemotron)

- [ ] Este doc está aprobado.
- [ ] Las interfaces del §3 están cerradas.
- [ ] Los criterios de aceptación del §9 tienen test asociado.
- [ ] Los invariantes del §4 están referenciados.
- [ ] El alcance del §2 está claro.
- [ ] Los skills del §13 están listados.
- [ ] `BlsPaths` (doc #4) está mergeado (dependencia de `LogsDir`).

Cuando Nemotron termine, el PR debe:

- [ ] Compilar Release x86 sin warnings.
- [ ] Pasar todos los tests de CA-01..CA-13.
- [ ] No modificar interfaces del §3.
- [ ] Referenciar este doc en el commit:
      `feat: logger + version (refs design/20250107-logging-version.md)`.

---

## 16. Referencias

- `AGENTS.md` reglas 4, 5.
- `PRD.md` §7 FR-07, §11 criterio 8.
- `.opencode/skills/bls-logging-errors/SKILL.md`.
- `.opencode/skills/bls-testing-strategy/SKILL.md`.
- `docs/design/20250104-secure-store.md` (`BlsPaths`).
- `docs/design/20250106-registration-scripts.md` (`version.txt` en el ZIP).
- `docs/design/20250108-addin-lifecycle.md` (integración en `OnConnection`).
```

---

## Cómo usarlo en opencode

### Nemotron 3 Ultra implementa logging + version (paralelo a #1, #2, #4, #6)

```bash
opencode --model nemotron-3-ultra \
"Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/bls-project-context/SKILL.md
3. docs/design/20250107-logging-version.md (el diseño)
4. Los skills citados en §13 del diseño

Implementa la feature 'Logging + Version' exactamente como está
especificado.
Incluye:
- BlsLogger + IBlsLogger + BlsVersion + BlsVersionContext
- GlobalExceptionHandlers (AppDomain, Application.ThreadException,
  TaskScheduler.UnobservedTaskException)
- Tests con directorios temporales

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
3. docs/design/20250107-logging-version.md (diseño)
4. El diff del PR

Aplica el checklist de rechazo del skill bls-workflow-nemotron.
Presta especial atención a:
- Nunca loguear la API key (regla 4).
- Stack solo en Debug (skill bls-logging-errors §Error).
- UnhandledException no relanza (skill bls-logging-errors §Captura global).
- UnobservedTaskException llama SetObserved (skill bls-logging-errors §Captura global).
- Formato canónico de línea (timestamp ISO 8601 UTC, nivel 5 chars).
- Rotación 7 días en constructor (no en cada Write).

Si todo pasa, aprueba con 'LGTM'. Si algo falla, indica la regla violada."
```

---

## Estado del backlog de diseño

| # | Doc                                | Estado            | Depende de    |
|---|------------------------------------|-------------------|---------------|
| 1 | `20250101-vbe-bridge.md`           | ✅ Entregado       | —             |
| 2 | `20250102-nim-client.md`           | ✅ Entregado       | —             |
| 3 | `20250103-nim-terminal.md`         | ✅ Entregado       | #1, #2, #4    |
| 4 | `20250104-secure-store.md`         | ✅ Entregado       | ADR-001       |
| 5 | `20250105-chat-history.md`         | ⚠️ Fusionado en #4 | —             |
| 6 | `20250106-registration-scripts.md` | ✅ Entregado       | #7            |
| 7 | `20250107-logging-version.md`      | ✅ Entregado       | #4 (BlsPaths) |
| 8 | `20250108-addin-lifecycle.md`      | Pendiente         | Todos         |

**Ruta crítica hacia el MVP:**
```
#1 bridge ─┐
           ├─→ #3 terminal ─→ #8 lifecycle ─→ MVP
#2 client ─┘       ↑
                   │
#4 store ──────────┤ (alimenta #2, #3, #7)
#6 reg ────────────┤ (alimenta #8)
#7 log ────────────┘ (transversal)
```

**Paralelización inmediata (4 frentes completos):**
- Frente A: #1 bridge
- Frente B: #2 client
- Frente C: #4 store + history
- Frente D: #6 reg + #7 log

Todos los frentes pueden avanzar en paralelo. Solo queda **#8
(`20250108-addin-lifecycle.md`)**, que integra todo y cierra el MVP.
