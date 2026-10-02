# SKILL 00 — Contexto del proyecto

> **Cuándo cargar este skill:** SIEMPRE. Es el punto de entrada.
> Cárgalo junto a `AGENTS.md` antes de cualquier tarea.

---

## Qué es

Add-in COM para el VBE de Microsoft Access que integra Nvidia NIM
directamente, sin dependencias externas (sin CLI, sin MCP, sin servidor
HTTP intermedio). Expone el proyecto Access al modelo vía un bridge
JSON-RPC sobre Named Pipe y da una terminal WinForms embebida en el VBE.

- **ProgID:** `Balansoft.AIAddin.Access`
- **CLSID:** `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`
- **Plataforma:** Access 2010–365, **solo x86**
- **Stack:** C# .NET Framework 4.8, LangVersion 10, `Nullable disable`

---

## Capas y responsabilidades

| Capa                 | Archivos clave                                                                                       | Responsabilidad                                                 |
|----------------------|------------------------------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `Core/`              | `BalansoftConnect`, `BalansoftAddin`, `BlsVbeBridgeServer`                                           | Ciclo de vida COM, orquestación, bridge server                  |
| `VbeIntegration/`    | `BlsVbeService`, `BlsCodeWindowManager`, `BlsVbeMenuManager`                                         | Automatización VBE, ventana propietaria, menú Tools             |
| `AccessIntegration/` | `BlsAccessObjectService`                                                                             | Consulta de objetos del proyecto (solo lectura)                 |
| `UI/`                | `BlsNimTerminal`, `BlsNimClient`                                                                     | Terminal WinForms, cliente HTTP/2 streaming a NIM               |
| `Registration/`      | `BlsAddinRegistrar`, `BlsRegistryHelper`                                                             | Registro HKCU, LoadBehavior=3                                   |
| `Helpers/`           | `BlsSecureStore`, `BlsChatHistory`, `BlsVersion`, `BlsLogger`, `BlsComHelper`, `BlsAssemblyResolver` | DPAPI, historial, versión, logs, COM, resolución de ensamblados |
| `Properties/`        | `AssemblyInfo.cs`                                                                                    | Solo `ComVisible`, `Guid`, `InternalsVisibleTo`                 |

---

## Las 10 operaciones del bridge

### VBE (7)
| Método              | Efecto                                             |
|---------------------|----------------------------------------------------|
| `list_modules`      | Lista módulos del proyecto VBA                     |
| `read_module`       | Devuelve el código fuente de un módulo             |
| `write_module`      | Sobrescribe el código de un módulo existente       |
| `create_module`     | Crea un módulo nuevo (estándar, clase, formulario) |
| `delete_module`     | Elimina un módulo (requiere `confirm:true`)        |
| `get_selection`     | Devuelve módulo + rango + texto seleccionado       |
| `get_active_module` | Devuelve módulo activo (nombre, tipo, proyecto)    |

### Access read-only (3)
| Método            | Efecto                                                   |
|-------------------|----------------------------------------------------------|
| `project_summary` | Nombre, projectKey, counts por categoría, versión Access |
| `list_objects`    | Nombres + fieldCount por categoría (sin SQL)             |
| `object_detail`   | Detalle de un objeto (ver ADR-004)                       |

---

## Decisiones ya tomadas (ADRs vigentes)

Lee siempre los ADRs antes de tocar el área correspondiente:

| ADR         | Decisión                                                                                                                                                                     | Skill afectado |
|-------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|----------------|
| **ADR-001** | Identidad de proyecto = `SHA256(ruta normalizada)`. Override por `VBProject.Description` con prefijo `BALANSOFT-PROJECT-KEY:`. Fallback efímero si la base no está guardada. | 02, 05         |
| **ADR-002** | Framing pipe = `[uint32 LE length][UTF-8 JSON]`, máx 1 MB. Pipe en modo mensaje + `CurrentUserOnly`.                                                                         | 04             |
| **ADR-003** | Abort = un `CancellationTokenSource` por turno de usuario. Cancelación de usuario ≠ timeout.                                                                                 | 03, 04, 06, 08 |
| **ADR-004** | `object_detail`: campos siempre, SQL solo con `includeSql:true` (cap 8 KB), `maxFields` default 200, `MSys*`/`~*` omitidos.                                                  | 04, 09         |

Si un ADR contradice un skill, **gana el ADR**. Abre PR para actualizar el
skill en el mismo commit.

---

## Invariantes del sistema

1. **Una sola terminal** por proceso. Segundo intento de abrir → foco a la
   existente.
2. **Un solo cliente del bridge** a la vez. Segundo cliente → `-32004 busy`.
3. **Modelo fijo:** `nvidia/nemotron-3-ultra-550b-a55b` (override por
   variable de entorno `NIM_MODEL`).
4. **API key** solo en DPAPI (`%APPDATA%\Balansoft\AIAddin\apikey.bin`).
   Nunca en claro, nunca en log.
5. **Historial** por `projectKey` en
   `%APPDATA%\Balansoft\AIAddin\history\{projectKey}.json`.
6. **Bridge** arranca en `AttachVbe`, muere en
   `OnDisconnection`/`OnBeginShutdown`.
7. **UI y logs en español.**
8. **Errores reales:** fallo COM/NIM → error con categoría y mensaje
   original. Nunca "hecho" falso.

---

## Flujo de un turno (resumen)

```
Usuario escribe prompt
  → BlsNimTerminal añade mensaje + contexto VBE (módulo activo, selección)
  → BlsChatHistory persiste
  → BlsNimClient POST streaming SSE a NIM
  → render en ListView conforme llegan chunks
  → si hay tool_call:
       BlsNimClient → BlsVbeBridgeServer (pipe) → BlsVbeService/AccessService
       → resultado → NIM (siguiente turno)
  → NIM responde [DONE]
  → historial persiste conversación completa
```

---

## Cómo se organiza el trabajo (resumen)

- **Big Pickle:** diseño de interfaces, contratos, revisión (skill 10).
- **Nemotron 3 Ultra:** implementación, scripts, tests (skill 10).
- Toda feature nueva sigue el protocolo de handoff del skill 10.
- Los ADRs viven en `docs/design/` y son fuente de verdad.

---

## Skills disponibles (índice rápido)

| Skill | Área                             |
|-------|----------------------------------|
| 01    | COM late-binding sin PIAs        |
| 02    | Automatización VBE               |
| 03    | Cliente NIM streaming            |
| 04    | Bridge JSON-RPC sobre Named Pipe |
| 05    | DPAPI + historial                |
| 06    | WinForms + threading             |
| 07    | Registro HKCU + scripts          |
| 08    | Logging y propagación de errores |
| 09    | Estrategia de tests              |
| 10    | Workflow Big Pickle + Nemotron   |

---

## Anti-recordatorio (lo que NO es este proyecto)

- **No** usa `opencode` CLI ni MCP bridge (arquitectura anterior
  descartada).
- **No** soporta Access x64, Word, Excel.
- **No** permite cambiar modelo/proveedor desde la UI.
- **No** tiene autocompletado inline en el VBE (fuera de MVP).
- **No** permite chat multi-sesión concurrente.
- **No** usa PIAs de Office.
- **No** requiere admin para registrarse.
