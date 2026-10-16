---
version: 1.1.0
date: 2026-10-15
status: active
iso_standard: [25010, 12207]
aligns_to: [PRD.md, CONSTITUTION.md, AGENTS.md]
tags: [code-quality, square, good-practices, maintainability]
---

# Registro de Code Issues y Good Practices - Balansoft Access VBA Agent

## Versión: 1.1.0
## Fecha: 2026-10-15
## Estado: activo
## Norma de referencia: ISO/IEC 25010:2023 (Modelo de Calidad del Producto de Software) / ISO/IEC 12207:2017

## Propósito
Documentar issues de código, buenas prácticas,anti-patrones y sugerencias de mejora para el proyecto Balansoft Access VBA Agent. Este archivo complementa el registro de bugs (BUGS/) enfocándose en calidad de código y maintenibilidad.

---

## Good Practices Validadas (ISO/IEC 25010)

### C# 10 y .NET Framework 4.8
- [x] Usar `LangVersion 10` en `Directory.Build.props`
- [x] `Nullable=annotations` sin `disable` (a menos que sea necesario por compatibilidad COM)
- [x] `TreatWarningsAsErrors=true` en todos los proyectos
- [x] CPU-agnóstico: la arquitectura (x86\|x64) se pasa con `-p:PlatformTarget`; no forzar en los proyectos

### COM y Late Binding
- [x] `dynamic` **solo** en `Helpers/BlsComHelper.cs`
- [x] Todo acceso COM envuelto en `BlsComHelper.Invoke()`
- [x] `VBE == null` siempre verificado antes de usar
- [x] **NUNCA** `Marshal.GetActiveObject()` fuera de tests
- [x] **NUNCA** `Marshal.ReleaseComObject()` manually
- [x] Usar `dynamic` late-bound, no PIAs (`Microsoft.Office.Interop.*`)

### Seguridad y DPAPI
- [x] API key **SOLO** en `DataProtectionScope.CurrentUser`
- [x] Entropía fija: `UTF8("Balansoft.AIAddin.Access.v1")`
- [x] Archivo binario en `%APPDATA%\Balansoft\AIAddin\apikey.bin`
- [x] **NUNCA** loguear API key, ni completa ni parcial
- [x] **NUNCA** serializar API key en JSON o registry

### Threading y UI
- [x] `async void` **solo** en event handlers de UI
- [x] Todo acceso a controles desde hilo no-UI pasa por `OnUi()` / `BeginInvoke`
- [x] **NUNCA** `Application.DoEvents()`
- [x] **NUNCA** crear `Form` fuera del hilo UI de Access
- [x] Cerrar terminal **no** debe cerrar Access

### Bridge y Pipe
- [x] Framing: 4 bytes LE (uint32) + UTF-8 JSON (ADR-002)
- [x] `CurrentUserOnly` obligatorio en Named Pipe
- [x] Whitelist de métodos (10 métodos definidos)
- [x] `SemaphoreSlim(1,1)` para serializar operaciones VBE
- [x] Token del turno (ADR-003) propagado a todos los handlers

### Tests y Cobertura
- [x] 79 tests del MVP (36 ADR + 29 transversales + 6 integración NIM)
- [x] **NUNCA** `Thread.Sleep` en tests (usar `WaitAsync(TimeSpan)`)
- [x] Tests sin Office (unitarias) corren en CI
- [x] Tests de integración NIM con `NIM_API_KEY` en env
- [x] No depender del orden de ejecución de tests

### Diseño por Contrato (DbC)
- [x] Validar precondiciones (`Requires*`) en todo constructor y método público
- [x] Validar postcondiciones (`Ensures*`) en métodos generadores y transformadores
- [x] Validar invariantes (`Invariant`) en estados críticos de clase
- [x] Usar la clase canónica `Balansoft.AIAddin.Helpers.BlsContract`

---

## Code Issues Identificados (Pending)

### Issue #1: CS-001
**Título**: `dynamic` disperso en detección de código
**Gravedad**: Medium
**Archivo potencial**: `VbeIntegration/`
**Descripción**:
Se han encontrado instancias donde `dynamic` se usa para acceder a propiedades VBA que podrían tiparse. Aunque la regla es mantener `dynamic` en `BlsComHelper`, hay código de diagnóstico que evade esta regla.

**Prioridad**: Mediana
**Propone**: Mover toda lógica de diagnóstico a `BlsComHelper` y usar interfaces `IBlsVbe`/`IBlsAccess` en el resto.

**Estado**: Pendiente de revisión en próximo sprint

### Issue #2: CS-002
**Título**: Logging de duración de chunks en NIM Client
**Gravedad**: Baja
**Archivo potencial**: `UI/BlsNimClient.cs`
**Descripción**:
El logging actual de `BlsNimClient` no incluye la duración de cada chunk SSE, lo que dificulta el debugging de performance issues. Se sugiere añadir `stopwatch` tracking en el loop de lectura.

**Prioridad**: Baja
**Propone**: Añadir `DateTime` timestamps a cada log de `OnTextChunk` y reporte de duración total en `OnDone`.

**Estado**: Pendiente de implementación

### Issue #3: CS-003
**Título**: Falta reporte de versión en logs de error
**Gravedad**: Baja
**Archivo potencial**: `Helpers/BlsLogger.cs`
**Descripción**:
Los errores logged no siempre incluyen la versión actual del add-in ni el commit hash, lo que dificulta el triaje en soporte. El primer línea del log diario ya incluye esta info, pero los logs de error individual no siempre.

**Prioridad**: Baja
**Propone**: Añadir `version` y `commit` automáticamente en cada `Error()` call, o crear helper `BlsLogger.LogErrorWithContext()`.

**Estado**: Pendiente de implementación en próxima actualización

---

## Good Practices Checklist (Por cada PR)

### Antes de commitear:
- [ ] `dynamic` solo en `Helpers/BlsComHelper.cs`?
- [ ] CPU-agnóstico: `PlatformTarget`/`Prefer32Bit` no forzados en los proyectos (`-p:`)?
- [ ] `TreatWarningsAsErrors` sin warnings?
- [ ] Sin `Marshal.GetActiveObject` ni `ROT`?
- [ ] Sin `Thread.Sleep` en tests?
- [ ] Sin `Application.DoEvents`?
- [ ] API key no en logs/excepciones?
- [ ] `VBE == null` chequeado antes uso?
- [ ] Framing pipe 4bytes LE + UTF-8 JSON?
- [ ] Tests: 79 pasan (sin integración NIM)?

### Código:
- [ ] Comentarios XML en APIs públicas
- [ ] Sin `#region` (excepto en tests)
- [ ] Sin `#if DEBUG` fuera de tests
- [ ] Manejo de `VBE == null`
- [ ] Errores propagan categoría + mensaje original
- [ ] Cancelación ≠ error (ADR-003)

---

## Métricas de Calidad (ISO/IEC 25010)

| Métrica                | Objetivo                 | Estado Actual |
|------------------------|--------------------------|---------------|
| Functional Suitability | 100% features operativas | ✅             |
| Performance Efficiency | < 2s respuesta UI        | ✅             |
| Reliability            | 0 crashes en arranque    | ✅             |
| Security               | API key solo DPAPI       | ✅             |
| Maintainability        | Score > 8/10             | ✅             |
| Portability            | x86-only (sin x64)       | ✅             |
| Compatibility          | Access 2010-365 x86      | ✅             |

---

## Próximos Issues por Prioridad

1. **Alta**: Migrar toda lógica `dynamic` a `BlsComHelper` (CS-001)
2. **Mediana**: Añadir tracking de duración de chunks (CS-002)
3. **Baja**: Mejorar contexto en logs de error (CS-003)
4. **Baja**: Actualizar checklist de PRs con nuevas reglas

---

## Referencias Relacionadas
- BUGS/known-bugs.md — Bugs conocidos y reportados
- AGENTS.md — Reglas duras del proyecto
- PRD.md — Historias de usuario y requisitos
- .opencode/skills/bls-com-late-binding/SKILL.md — Envoltorio COM
- .opencode/skills/bls-logging-errors/SKILL.md — Logging y formato
- .opencode/skills/bls-testing-strategy/SKILL.md — Tests y calidad
- ADR-001..004 — Decisiones arquitectónicas