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