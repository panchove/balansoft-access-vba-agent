# AGENTS.md — Balansoft Access VBA Agent (NIM Direct)

COM Add-in para Microsoft Access 2010+ (x86) que integra NVIDIA NIM en el VBE.
Stack: C# 10, .NET Framework 4.8 (net48), WinForms, sin PIAs, enlace tardío vía `dynamic`.
ProgID: `Balansoft.AIAddin.Access` | CLSID: `{d31bf73e-dbe1-4739-84bc-2c974fcd3171}`.

> Estado: el producto **no carga todavía en Access** (falta el punto de entrada COM, spec 001). El bridge JSON-RPC, el cliente NIM y el almacén DPAPI están implementados con 99 tests verdes.

## Reglas duras (CI las verifica y bloquea el merge)

1. **x86 obligatorio:** `PlatformTarget=x86`, `Prefer32Bit=true`. Access x86 no carga ensamblados x64.
2. **Sin PIAs de Office:** nunca `Microsoft.Office.Interop.*`.
3. **Sin ROT:** nunca `Marshal.GetActiveObject` ni Running Object Table.
4. **`dynamic` solo en `Helpers/BlsComHelper.cs`** (CI lo comprueba en `src/`).
5. **Ningún `Thread.Sleep` en `tests/`** (CI lo comprueba).
6. **`TreatWarningsAsErrors=true`** — 0 warnings bloquean el build.
7. **API key solo en DPAPI** (`BlsSecureStore`, `CurrentUser`). Nunca en logs, excepciones, JSON, registry ni disco.
8. **Errores COM/NIM se propagan** con categoría + mensaje original. Prohibido éxito falso ni `catch (Exception) { }`.
9. **Nullable=annotations** (no `disable`).
10. **Todo acceso a VBE/Access desde hilos no-UI** pasa por marshaling explícito al hilo de UI (`IUiScheduler`).

## Comandos canónicos

Compilar los `.csproj` directamente, **no** la solución `.slnx` (no propaga `Platform` a proyectos SDK-style; la salida debe ir a `bin\x86\<Config>\net48\`):

```powershell
# Build Debug x86
dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86 -v:minimal --nologo

# Build tests
dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86

# Tests unitarios (sin integración NIM; .runsettings filtra TestCategory=Integration)
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86
```

Registro / desregistro / empaquetado requieren **PowerShell x86** (menú "Windows PowerShell (x86)"):

```powershell
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1
powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1 -Configuration Release
```

## Entorno y gotchas

- **.NET SDK 9.0.x** requerido.
- **net48 limita APIs:** no `Task.WaitAsync`, no raw strings (C# 11), no `System.Net.Http.Json`, no E/S de archivos async. Para waits con timeout usar `tests/.../TaskTestExtensions.cs`.
- **Named pipe:** framing `[uint32 LE longitud][UTF-8 JSON]`, máx 1 MB. Las clases `NamedPipe*` anteponen `\\.\pipe\` solas — pasar el nombre completo lo duplica. Usar el nombre pelado (`BlsPipeFraming.DefaultPipeName`).
- **Scaffold.ps1 está roto** (4 errores de parser, líneas 762-775). **No usar.**
- **Modelo:** `nvidia/nemotron-3-ultra-550b-a55b` (override por variable de entorno `NIM_MODEL`).
- **Tests de integración NIM** requieren `NIM_API_KEY` en el entorno (o `.env`); quedan fuera de la CI por defecto.
- **`.env`** se copia de `.env.example`; nunca se commitea (está en `.gitignore`).

## Estructura

- `src/Balansoft.AIAddin.Access/` — add-in COM, Core, Helpers, UI (WinForms)
- `tests/Balansoft.AIAddin.Tests/` — 99 tests MSTest (fakes, sin COM real). `BalansoftTest.accdb` es la base de prueba para verificar en Access real (no se usa en la suite por defecto).
- `specs/` — contratos SDD (001–012, MAYÚSCULAS `NNN-<SLUG>.md`); `specs/README.md` es el índice con estados y tests por spec.
- `docs/design/` — ADR (`ADR-NNN-<SLUG>.md`)
- `.opencode/skills/` — skills de OpenCode (bls-*)
- `Balansoft.AIAddin.slnx` — solución real; **no se compila** directamente

## Ejecutar un test o categoría enfocada

Con `vstest` directamente (no `dotnet test`, que sustituye el host en net48):

```powershell
# Un test por nombre completo
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86 /TestCaseFilter:"FullyQualifiedName~BlsPipeFramingTests"

# Una categoría (integración requiere NIM_API_KEY)
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /TestCaseFilter:"TestCategory=Integration" /Platform:x86
```

El filtro `-Filter` también está en `scripts\dev.ps1 test -Filter "<área>"`.

## Variables de entorno

Copia `.env.example` a `.env` (nunca se commitea; está en `.gitignore`):

| Variable | Obligatorio | Valor por defecto |
|----------|-------------|-------------------|
| `NIM_API_KEY` | sí (solo integración NIM) | — |
| `NIM_MODEL` | no | `nvidia/nemotron-3-ultra-550b-a55b` |
| `NIM_ENDPOINT` | no | `https://integrate.api.nvidia.com/v1/chat/completions` |
| `NIM_REASONING_EFFORT` | no | `default` |
| `NIM_DEFAULT_LANGUAGE` | no | `english` |

## Convenciones de commit y PR

Mensajes convencionales con referencia al doc de diseño:

```
feat: <nombre> (refs design/YYYYMMDD-feature.md)
fix: <descripción> (closes #<issue>)
docs: ADR-00X <título>
test: <descripción>
chore: <descripción>
```

Un solo commit por feature a `main`. El PR usa `.github/PULL_REQUEST_TEMPLATE.md` con checklist de rechazo (cualquier ítem en negativo ⇒ rechazo automático salvo justificación explícita en "Notas para el revisor"). Items clave del checklist: sin PIAs, sin ROT, `dynamic` solo en `Helpers/BlsComHelper.cs`, marshaling UI por `IUiScheduler`, API key solo DPAPI, errores propagados, x86, `TreatWarningsAsErrors`, sin `Thread.Sleep` en tests, tests ADR obligatorios presentes.

## Orden de specs (SDD)

El spec **001 (ADDIN-LIFECYCLE) es bloqueante**: sin el punto de entrada COM el add-in no carga en Access y nada más se puede probar allí. Después van 008 (logging) y 009 (registro), necesarios para diagnosticar y desplegar 001. Los specs 002–007 son el transporte y funcionalidad; 004 (terminal) es el mayor gap de calidad (~600 líneas WinForms sin tests). Índice y estados exactos en `specs/README.md`.

## Nueva dependencia NuGet

Requiere ADR (documentar por qué y evaluar compatibilidad net48/x86). No se agrega sin ADR aprobado.

## Versionado y releases

- Versión automática con **Nerdbank.GitVersioning** (`version.json`, SemVer 2).
- Push de tag `vX.Y.Z` en `main` → `release.yml` → build Release x86 → `Pack.ps1` → GitHub Release con ZIP + `version.txt`.
- Un solo commit por feature a `main`.

## Flujo de trabajo (SDD)

1. Spec en `specs/` con criterios de aceptación verificables (primero).
2. Implementar + tests (x86, sin PIAs, `dynamic` aislado).
3. Verificar: build Debug + Release x86 y suite de tests verde.
4. PR → CI ejecuta 8 gates: build, tests (99/99), documentos obligatorios, ausencia de PIAs, aislamiento de `dynamic`, ausencia de `Thread.Sleep` en tests, tests de ADR, referencias cruzadas en MDs.
5. Merge a `main` → tag `v*` → release automático.
