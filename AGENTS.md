# AGENTS.md — Balansoft Access VBA Agent (NIM Direct)

**ProgID:** `Balansoft.AIAddin.Access`  
**CLSID:** `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`  
**Stack:** C# .NET Framework 4.8, LangVersion 10, `Nullable=annotations`, **x86 only**, sin PIAs, enlace tardío vía `dynamic`.

---
## Modelo de IA y API key

- **Modelo:** `nvidia/nemotron-3-ultra-550b-a55b` (overrideable vía `NIM_MODEL`).
- **API key:** solo en DPAPI (`BlsSecureStore`). Nunca en log, JSON, excepción ni disco.
- **Historial:** por proyecto, identidad = SHA-256 de la ruta normalizada (ADR-001).
- **Framing pipe:** `[uint32 LE length][UTF-8 JSON]`, máx 1 MB (ADR-002).
- **Abort por turno:** un `CancellationTokenSource` por turno de usuario (ADR-003).
- **object_detail:** campos siempre, SQL solo con `includeSql:true`, `maxFields` default 200 (ADR-004).
- **Diseño por Contrato (DbC):** contratos formales en APIs públicas vía `BlsContract` (ADR-005).

---
## Reglas duras (no negociables)

1. **NUNCA** añadir referencia a PIAs de Office (`Microsoft.Office.Interop.*`).
2. **NUNCA** usar `Marshal.GetActiveObject` ni ROT para obtener VBE/Application.
3. Acceso a VBE/Access desde hilos no‑UI pasa por marshaling explícito (skill `bls-winforms-threading`).
4. **API key SOLO en DPAPI.** Prohibido log, config, JSON, registry en claro.
5. Errores COM/NIM se propagan como excepción con categoría + mensaje original. **PROHIBIDO** devolver éxito falso ante fallo.
6. **PlatformTarget=x86** obligatorio. No tocar a AnyCPU ni x64.
7. **TreatWarningsAsErrors=true**. Los warnings bloquean merge.
8. **Framing del Named Pipe:** 4 bytes LE (longitud) + UTF‑8 JSON. Ver ADR‑002.
9. **Identidad de proyecto:** SHA‑256(ruta normalizada). Ver ADR‑001.
10. **Abort:** un `CancellationTokenSource` por turno de usuario. Ver ADR‑003.
11. **Nullable=annotations** (no `disable`).
12. **DbC:** todo método o servicio público debe validar precondiciones vía `BlsContract` (ADR-005).

---
## Comandos canónicos (usar `pwsh`)

| Acción                                              | Comando                                                                                                                            |
|-----------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------|
| **Build Debug x86**                                 | `dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86`                               |
| **Build tests**                                     | `dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86`                               |
| **Test unit** (solo unitarios, sin integración NIM) | `dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86` |
| **Registrar add-in**                                | `pwsh -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1` **(x86 only)**                                                      |
| **Desregistrar**                                    | `pwsh -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1` **(x86 only)**                                                    |
| **Empaquetar Release**                              | `pwsh -ExecutionPolicy Bypass -File scripts/Pack.ps1 -Configuration Release`                                                       |
| **Verificar tests** (canónico)                      | `pwsh -c "dotnet test tests/Balansoft.AIAddin.Tests/Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86"`                      |

---
## Flujo de trabajo típico

1. **Leer contexto:** `AGENTS.md`, `.opencode/opencode.json`, skills en `.opencode/skills/bls-*/SKILL.md`.
2. **Implementar:** C# .NET 4.8 x86, sin PIAs, con tests.
3. **Verificar:** `dotnet test` verde (tests de unidad; tests de integración requieren `NIM_API_KEY`).
4. **Commit:** mensaje `feat: <nombre> (refs design/YYYYMMDD-feature.md)`.
5. **Revisar:** checklist Nemotron (sin Big Pickle).
6. **Merge:** un solo commit por feature a `main` → tag `v*` → release automático.

---
## Archivos clave

- `.runsettings` — filtra tests (category≠Integration), configura coverage, TargetFramework=net48
- `Directory.Build.props` — x86, LangVersion 10, TreatWarningsAsErrors, NBGV package
- `version.json` — config Nerdbank.GitVersioning (semVer 2)
- `.env` — API key local para desarrollo (no se commitea)
- `docs/design/` — ADRs (001‑005)
- `.opencode/opencode.json` — configuración y skills index
- `scripts/` — `RegisterAddin.ps1` / `UnregisterAddin.ps1` deben correr en PowerShell **x86**

---
## Gotchas operativas

- **Scripts de registro:** `RegisterAddin.ps1` / `UnregisterAddin.ps1` **deben** correr en PowerShell **x86** (usar `pwsh` desde menú x86 o verificar `[Environment]::Is64BitProcess`).
- **Tests de integración NIM:** requieren `NIM_API_KEY` en el entorno (o `.`). No se ejecutan en CI por defecto (filtro `TestCategory!=Integration` en `.runsettings`).
- **Scaffold.ps1 roto:** 4 errores parser líneas 762‑775; **no usar**.
- **Compilar .csproj, no .slnx:** la solución no propaga `Platform` a proyectos SDK-style; usar siempre `dotnet build src/... -c Debug -p:Platform=x86`.
- **Release:** solo tag `v*` en `main` dispara workflow `release.yml`; no hay release manual ni `draft`.
- **Uniquete modelo:** `nvidia/nemotron-3-ultra-550b-a55b` (override por `NIM_MODEL`).
- **Compilación en Windows:** .NET SDK 9.0.x requerido; Access 2010+ (32 bit) solo x86.
- **`.env`:** copiar desde `.env.example`, nunca commitear.
- **PowerShell x86** necesario para registrar/desregistrar el add‑in.

---
## Anti‑patrones prohibidos (rechazo automático en PR)

| Anti‑patrón                                     | Por qué                                                |
|-------------------------------------------------|--------------------------------------------------------|
| `dynamic` fuera de `Helpers/BlsComHelper.cs`    | Dispersión incontrolable de late-binding               |
| `Marshal.GetActiveObject("Access.Application")` | ROT prohibido                                          |
| `Marshal.ReleaseComObject(vbe)`                 | Doble release, cierres inesperados de Access           |
| `catch (Exception) { }` en llamadas COM         | Traga errores; usar `BlsComHelper` / `BlsComException` |
| Llamar COM desde hilo NIM sin marshaling        | Crash del host                                         |
| `Application.VBE` sin comprobar `null`          | Rompe en runtime/ACCDE                                 |
| Referencias a `Microsoft.Office.Interop.*`      | Bloquea merge (TreatWarningsAsErrors)

---
## Testing & QA

- **Tests unitarios:** `dotnet test ... -c Debug -p:Platform=x86` (123 tests, sin clave NIM).
- **Tests de integración:** requieren `NIM_API_KEY` en el entorno; están excluidos de la CI por filtro `TestCategory!=Integration` en `.runsettings`.
- **Skill de testing:** `bls-testing-strategy` — directrices para escribir tests y fakes sin COM real.

### Registro y ejecución del add‑in

Para instalar o registrar el add‑in **Balansoft.AIAddin.Access** se debe utilizar **Windows PowerShell (x86)** (disponible en el menú **Windows PowerShell (x86)** del inicio). Se recomienda establecer la política de ejecución temporal para que el script pueda correr sin prompts:

```powershell
Set-ExecutionPolicy Bypass -Scope Process
```

Después de establecer la política, ejecuta el script de registro desde la misma sesión:

```powershell
.\scripts\RegisterAddin.ps1
```

Esto registrará el COM‑add‑in en Access (arquitectura x86) y hará aparecer el menú **NIM Terminal** en la cinta de Access (o bajo *Tools → NIM Terminal* en el VBE). Si la ejecución pide confirmación de seguridad, asegúrate de estar ejecutando la consola como **Administrador** o ajustar la política de ejecución del sistema.