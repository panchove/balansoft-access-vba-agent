# Balansoft Access VBA Agent

COM Add-in para **Microsoft Access 2010+ (x86)** que integra **NVIDIA NIM** en el editor de VBA (VBE).
Permite chatear con modelos LLM, ejecutar *tool calls* y persistir el historial por proyecto, todo desde una ventana *WinForms* acoplada al VBE.

---

## ✨ Características principales
- **Puente JSON-RPC** sobre *named pipe* (framing 4 bytes LE + UTF‑8 JSON) entre el VBE y el host .NET.
- **Cliente NIM** con streaming SSE, reintentos (429/5xx), backoff exponencial y cancelación por turno (ADR‑003).
- **Almacén seguro** DPAPI para la API key (nunca en disco ni logs).
- **Historial de chat** por proyecto (identidad = SHA‑256 de la ruta normalizada – ADR‑001).
- **Terminal NIM** WinForms con loop de herramientas, *tool calls* y renderizado de resultados.
- **Registro COM** per‑user / per‑machine mediante scripts PowerShell x86.
- **CI/CD** completo: build x86, 99 tests unitarios, 8 gates de calidad, versionado SemVer automático (Nerdbank.GitVersioning) y release automático al pushear tag `v*`.

---

## 📦 Requisitos
| Requisito | Versión mínima |
|-----------|----------------|
| Windows | 10 / 11 (x64) |
| .NET SDK | 9.0.x |
| Access | 2010 o superior (32 bit) |
| PowerShell | 5.1 (versión **x86** para los scripts de registro) |

---

## 🚀 Puesta en marcha rápida

```powershell
# 1. Clonar el repo
git clone https://github.com/<tu-org>/balansoft-access-vba-agent.git
cd balansoft-access-vba-agent

# 2. Compilar (Debug x86)
dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86

# 3. Ejecutar tests (99/99)
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll `
  /Settings:.runsettings /Platform:x86

# 4. Registrar el add-in (PowerShell **x86**)
powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1

# 5. Abrir Access → VBE → Tools → NIM Terminal
```

> **Nota:** `RegisterAddin.ps1` **debe** ejecutarse en una consola *PowerShell (x86)* (menú *Windows PowerShell (x86)*).

---

## 🧪 Tests de integración contra NIM real
```powershell
$env:NIM_API_KEY = "tu-api-key"
dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Platform:x86
```
Los tests de integración están marcados con `[TestCategory("Integration")]` y **no** se ejecutan en la CI.

---

## 📂 Estructura del repositorio
```
src/
  Balansoft.AIAddin.Access/   # Add-in COM, Core, Helpers, UI (WinForms)
tests/
  Balansoft.AIAddin.Tests/    # 99 tests unitarios (MSTest)
docs/
  design/                     # 8 docs de diseño + 4 ADR
  PRD.md
  constitution.md
specs/                        # 9 specs (001–009) con criterios de aceptación
scripts/
  RegisterAddin.ps1
  UnregisterAddin.ps1
  Pack.ps1
  dev.ps1
.github/workflows/
  ci.yml          # build + test + 8 gates
  release.yml     # tag v* → GitHub Release + ZIP
.opencode/
  agents/         # bls-architect, bls-planner, bls-implementer, bls-reviewer, bls-iso
  skills/         # 11 skills modulares (bls-*/SKILL.md)
```

---

## 🔧 Comandos canónicos
| Acción | Comando |
|--------|---------|
| Build Debug x86 | `dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86` |
| Build Tests | `dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86` |
| Tests unitarios | `dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86` |
| Registrar add-in | `powershell -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1` (x86) |
| Desregistrar | `powershell -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1` (x86) |
| Empaquetar Release | `powershell -ExecutionPolicy Bypass -File scripts\Pack.ps1 -Configuration Release` |

---

## 📝 Versionado y releases
- **SemVer automático** con **Nerdbank.GitVersioning** (`version.json`).
- La versión se calcula a partir de tags `vX.Y.Z` + altura de commit.
- **Release flow**: push tag `vX.Y.Z` en `main` → workflow `release.yml` → build Release x86 → `Pack.ps1` → GitHub Release con ZIP + `version.txt`.
- `CHANGELOG.md` sigue *Keep a Changelog* y se actualiza a mano en cada PR.

---

## 🛡️ Reglas duras (enforced by CI)
1. **Sin PIAs** de Office (`Microsoft.Office.Interop.*`).
2. **Sin `Marshal.GetActiveObject` / ROT**.
3. Marshaling a UI **explícito** (`IUiScheduler`).
4. API key **solo DPAPI** (`BlsSecureStore`).
5. Excepciones propagadas con **categoría + mensaje original**.
6. `PlatformTarget=x86`, `Prefer32Bit=true`.
7. `TreatWarningsAsErrors=true` (build 0 warnings).
8. Named‑pipe framing **4 bytes LE + UTF‑8 JSON** (ADR‑002).
9. Identidad de proyecto **SHA‑256 ruta normalizada** (ADR‑001).
10. Un `CancellationTokenSource` por turno de usuario (ADR‑003).
11. `Nullable=annotations` (no `disable`).

---

## 🤝 Contribuir
1. Lee `AGENTS.md` y `docs/constitution.md`.
2. Abre un *issue* referenciando el *spec* correspondiente (`specs/NNN-*.md`).
3. El **Coordinator** asigna al **Planner** (descomposición) y al **Implementer** (código).
4. PR debe pasar **todos los gates de CI** (build, tests, 8 quality gates).
5. Merge a `main` → tag `v*` → release automático.

---

## 📄 Licencia
Este proyecto se distribuye bajo la **GNU Lesser General Public License v3.0** (o cualquier versión posterior).
Véase `LICENSE` para el texto completo. – © Balansoft 2026.