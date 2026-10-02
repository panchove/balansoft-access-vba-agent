# AGENTS.md — Balansoft Access VBA Agent (NIM Direct)

## Contexto
COM Add-in para Access 2010+ (x86) que integra Nvidia NIM en el VBE.
ProgID: Balansoft.AIAddin.Access
CLSID:  {d31bf73e-dbe1-4739-84bc-2c974fcd3171}
Stack:  C# .NET Framework 4.8, LangVersion 10, **Nullable=annotations**,
        x86 only, sin PIAs, enlace tardío vía dynamic.

## Primer uso (repo nuevo)
1. Clonar este repo como template (no usar `Scaffold.ps1`: está roto, 4 errores parser)
2. `dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86`
3. `dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86`
4. `powershell -ExecutionPolicy Bypass -File scripts/RegisterAddin.ps1` — **requiere PowerShell x86**
5. Abrir Access → VBE → Tools → NIM Terminal

## Versionado y release
- **SemVer automático** con **Nerdbank.GitVersioning** (`version.json`).
  - La versión viene de tags `vX.Y.Z` + altura de commit desde el tag.
  - `Directory.Build.props` **no** tiene `<Version>` hardcoded; NBGV lo inyecta.
- **Release flow**: push tag `vX.Y.Z` → CI (`.github/workflows/release.yml`) build + Pack → GitHub Release con ZIP + `version.txt`.
- **CHANGELOG.md** (Keep a Changelog) actualizado a mano en cada PR.

## Reglas duras (no negociables)
1. NUNCA añadir referencia a PIAs de Office (Microsoft.Office.Interop.*).
2. NUNCA usar Marshal.GetActiveObject ni ROT para obtener VBE/Application.
3. TODO acceso a VBE/Access desde hilos no-UI pasa por marshaling explícito.
4. API key SOLO en DPAPI. Prohibido log, config, JSON, registry en claro.
5. Errores COM/NIM se propagan como excepción con categoría + mensaje original.
   PROHIBIDO devolver éxito falso ante fallo.
6. PlatformTarget=x86 obligatorio. No tocar a AnyCPU ni x64.
7. TreatWarningsAsErrors=true. Los warnings bloquean merge.
8. Framing del Named Pipe: 4 bytes LE (longitud) + UTF-8 JSON. Ver ADR-002.
9. Identidad de proyecto: SHA256(ruta normalizada). Ver ADR-001.
10. Abort: un CancellationTokenSource por turno de usuario. Ver ADR-003.
11. **Antes de cada compactación de la conversación (resumen), reindexar el repositorio con `codebase-memory`** (`index_repository` mode `fast`).

## Flujo de trabajo entre modelos
- Big Pickle: diseño de interfaces, contratos, revisión.
- Nemotron 3 Ultra: implementación, scripts, tests.
- Antes de implementar, leer los skills 00-10 en .opencode/skills/.
- ADRs vigentes en docs/design/. Si un ADR contradice un skill, gana el ADR
  y hay que abrir PR para actualizar el skill.

## Comandos canónicos
- Build:      `dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86`
- Build tests:`dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86`
- Test (unit):`dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86`
- Integr (NIM):`$env:NIM_API_KEY="..."; dotnet vstest ...` (sin `/Settings:`)
- Reg (x86 PS):`powershell -ExecutionPolicy Bypass -File scripts/RegisterAddin.ps1`
- Unreg (x86 PS):`powershell -ExecutionPolicy Bypass -File scripts/UnregisterAddin.ps1`
- Pack:      `powershell -ExecutionPolicy Bypass -File scripts/Pack.ps1 -Configuration Release`

## Estilo
- Español en UI y mensajes de log.
- Comentarios XML en APIs públicas de Core/ y Helpers/.
- Sin regiones; sin #if DEBUG salvo en tests.

## Archivos clave
- `.runsettings` — filtra tests, configura coverage, TargetFramework=net48
- `Directory.Build.props` — x86, LangVersion 10, TreatWarningsAsErrors, NBGV package
- `version.json` — config Nerdbank.GitVersioning (semVer 2, publicReleaseRefSpec main + tags)
- `.env` — API key local para desarrollo (no se commitea, ver `.env.example`)
- `docs/design/` — ADRs (001-004) + 8 docs de diseño (20250101–20250108)
- `.opencode/skills/` — 11 guías de implementación por frente (`00-` a `10-`)
- `CHANGELOG.md` — historial de cambios (Keep a Changelog)

## Gotchas operativos
- `RegisterAddin.ps1` / `UnregisterAddin.ps1` **deben** correr en PowerShell **x86** (menú "Windows PowerShell (x86)")
- Tests de integración NIM requieren `NIM_API_KEY` en env (o `.env`)
- `Scaffold.ps1` **roto** (4 errores parser líneas 762-775); no usar
- Build con `dotnet build` (el MSBuild del SDK es el único que resuelve `Microsoft.NET.Sdk`; un MSBuild de VS sin el workload de .NET falla con MSB4236)
- Compilar **los `.csproj`, no `Balansoft.AIAddin.slnx`**: la solución no propaga `Platform` a proyectos SDK-style y escribe en `bin\<Config>\` en vez de `bin\x86\<Config>\net48\`
- Release: **solo tag `v*` en `main`** dispara workflow release; no hay release manual ni `draft`