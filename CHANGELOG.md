---
version: 1.0.0
date: 2026-10-16
status: active
iso_standard: [12207]
aligns_to: [README.md, AGENTS.md]
tags: [changelog, keep-a-changelog, iso-12207, maintenance]
---

# Changelog

> **Norma de referencia:** ISO/IEC 12207:2017 (Proceso de Gestión de Modificaciones y Mantenimiento)

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]
- Cierre de BalanceSoft-001: degradacion elegante en modo runtime `.accde`
  (`TryGetVbe`, `MsgNoVbeRuntime` en 4 idiomas).
- Cierre de BalanceSoft-002: `SseLineReader` con limite de 2 MB y dispose;
  coalescing de chunks de texto en la terminal (un `BeginInvoke` por turno).
- Red teaming determinista de inyeccion de prompt: 4 payloads, incluido
  intento de cierre de etiqueta, ahora escapado.
- Correccion de `RegisterAddin.ps1`/`UnregisterAddin.ps1`: las rutas CLSID
  con llaves fallaban en PowerShell 5.1 x86; se usa la API de registro .NET.
- Verificacion real en Access 2010 x86: add-in conectado, menu Tools ->
  NIM Terminal, JSON-RPC `list_modules` respondiendo y log 48/48 valido.
- Documentos de gobernanza anadidos: AIA (`AI-IMPACT-ASSESSMENT.md`),
  arquitectura C4 con diagramas Mermaid (`ARCHITECTURE-C4.md`) y
  `AI-INCIDENT-PLAYBOOK.md`. Los gates 3 y 8 exigen su presencia.

### Added (Fase 4, auditoria 2026-11-22)
- Clave de idempotencia derivada en el servidor: las claves del cliente se ignoran.
- Timeout global de turno (5 min) y `HttpClient.Timeout` explicito (10 min).
- Delimitacion `<untrusted_vbe_context>` en el system prompt y reglas `[NIF]`/`[IBAN]`.
- `scripts/GenerateSbom.ps1`: SBOM de dependencias NuGet publicado en CI.
- `BlsLogger.EmitSloSnapshot`: metricas SLO como evento JSONL cubierto por la cadena SHA-256.

### Changed
- `Nerdbank.GitVersioning` fijado a 3.6.146 (antes version flotante 3.6.*).
- SHA-256 del system prompt actualizado en el AI-BOM.
- 5 tests nuevos; suite 320/320 en x86 y x64.

### Removed (Fase 3, auditoria 2026-11-22)
- Siete skills redundantes u obsoletos: `bls-workflow-nemotron` (describia
  el flujo de dos modelos retirado en F1), `bls-logging-errors`,
  `bls-nim-client-streaming`, `bls-project-context`,
  `bls-registration-scripts`, `bls-vbe-automation` y `bls-winforms-threading`.
- Aproximadamente 2.500 LOC de documentacion duplicada; quedan cuatro skills canonicos.

### Changed
- `skills_index` y la lista de documentos obligatorios de CI reducidos a 4 skills.
- Las referencias historicas a skills retirados en ADRs y specs se redirigen
  a `AGENTS.md` como fuente canonica de reglas.

### Added (Fase 2, auditoria 2026-11-22)
- `BlsAuditChain.VerifyDirectory` y `SeedFor`: cadena SHA-256 entre dias, con
  compatibilidad para los logs legacy que empezaban con GENESIS cada dia.
- Verificacion de integridad al arranque: una cadena rota dispara el kill switch.
- `BlsLogger.LastWriteError`: los fallos de escritura dejan de silenciarse.
- Postcondiciones e invariantes en registro de autorizaciones, almacen seguro
  y cache de idempotencia (ADR-005).
- 10 tests nuevos: `BlsAuditChainDirectoryTests` y `BlsPostconditionTests`.

### Added (Fase 1, auditoria 2026-11-22)
- `BlsToolAuthorizationRegistry`: tokens de aprobacion de un solo uso, con TTL
  de 60 s y vinculados a metodo, turno y SHA-256 del payload.
- Fail-safe, HITL y tope de 32 tool calls por turno reforzados en
  `JsonRpcDispatcher` (codigos -32005, -32006 y -32007).
- ACL del named pipe restringida al SID del usuario actual
  (`BlsVbeBridgeServer.BuildPipeSecurity`).
- 8 tests nuevos: `JsonRpcSafetyTests` y `BlsPipeSecurityTests`.
 

## [0.2.0] - 2026-10-16

### Added
- Versioning policy with Nerdbank.GitVersioning (version.json)
- Release workflow on tag push (`.github/workflows/release.yml`)
- This CHANGELOG.md

### Changed
- Modelo **CPU-agnóstico** (x86 y x64) documentado de forma consistente en
  `AGENTS.md`, `README.md`, `MEMORY.md`, `docs/CONSTITUTION.md` (§II/§III/§IV),
  `docs/PRD.md`, `docs/CS/CODE-ISSUES.md`, `docs/MANUAL-TESTS.md`,
  `specs/README.md`, `specs/009` y las definiciones de agentes/skills
  (`.opencode/`).
- `Nullable=annotations` alineado en la documentación (los `.csproj` ya lo usaban).
- `Directory.Build.props`: removed hardcoded `<Version>`, now sourced from NBGV
- CI (`.github/workflows/ci.yml`): switched to `dotnet build` / `dotnet vstest`, fixed all 8 gates
- `scripts/Pack.ps1`, `RegisterAddin.ps1`, `UnregisterAddin.ps1`, `dev.ps1`: use `dotnet build`/`dotnet vstest` and correct `bin\x86\<Config>\net48\` paths
- Cross-references: 0 broken (was 11)

### Fixed
- `scripts/CopyDocs.ps1`: skill 10 mapping corrected
- `tests/Balansoft.AIAddin.Tests/UI/NimTerminalIconTests.cs`: test hermético
  (imagen sintética en memoria + destino temporal); antes leía una ruta absoluta
  y reescribía `src/.../Resources/NimTerminal.ico`.
- `ADR-010` (CA-09) y `specs/009` (R002): criterios anotados al modelo
  CPU-agnóstico (x86 y x64).

## [0.1.0] - 2026-10-02

### Added
- Initial SDD: constitution v1.1 + 9 specs (001–009)
- Bridge JSON-RPC (46 tests), NIM client streaming (53 tests)
- Secure store DPAPI, chat history per project
- Terminal WinForms with tool loop
- 99 tests green, build clean x86

### Known gaps
- COM add-in entry point (spec 001) — add-in does not load in Access yet
- Real `IBlsVbe` / `IBlsAccess` implementations
- `BlsComHelper`, `BlsLogger`, `BlsVersion`, `BlsConstants`
- Tests for specs 004, 005, 006
