# Pull Request

## Descripción

<!--
Qué cambia y por qué. 2-4 líneas.
No repitas el título. Explica el "por qué", no el "qué".
-->

## Tipo de cambio

- [ ] `feat:` Nueva funcionalidad
- [ ] `fix:` Corrección de bug
- [ ] `docs:` Solo documentación (ADR, skill, diseño)
- [ ] `test:` Solo tests
- [ ] `chore:` Build, CI, dependencias
- [ ] `refactor:` Refactor sin cambio de comportamiento

## Doc de diseño

<!--
Obligatorio para feat y fix.
Enlace al doc que rige esta feature. Si no aplica, marca N/A.
-->

- Doc: `docs/design/YYYYMMDD-<nombre>.md`
- ADRs afectados: `ADR-XXX`, `ADR-YYY`

- [ ] N/A (aplica solo a docs, chore, o refactor sin cambio funcional)

---

## Checklist de rechazo (skill 10)

> Cualquier ítem marcado en **negativo** ⇒ rechazo automático.
> El revisor verifica estos puntos; el autor los revisa antes de abrir el PR.

### COM / VBE

- [ ] No hay `using Microsoft.Office.Interop.*` (sin PIAs)
- [ ] No se usa `Marshal.GetActiveObject` ni ROT
- [ ] `dynamic` vive solo en `Helpers/BlsComHelper.cs`
- [ ] Todo acceso a VBE/Access pasa por `IUiScheduler` (marshaling)
- [ ] No se usa `Marshal.ReleaseComObject`
- [ ] Se comprueba `VBE == null` antes de usarlo

### Seguridad

- [ ] La API key no aparece en logs, JSON, excepciones ni registro
- [ ] `DataProtectionScope.CurrentUser` (no LocalMachine)
- [ ] No se loguea el body completo de NIM ni el código VBA completo
- [ ] El Named Pipe usa `CurrentUserOnly`

### Protocolo / ADRs

- [ ] Framing del pipe: `[uint32 LE][UTF-8 JSON]` (ADR-002)
- [ ] Abort: un CTS por turno de usuario (ADR-003)
- [ ] Identidad de proyecto: SHA256(ruta normalizada) (ADR-001)
- [ ] `object_detail`: campos siempre, SQL opt-in (ADR-004)
- [ ] Los cambios a interfaces se reflejan en el doc de diseño

### Errores

- [ ] No hay `catch (Exception) { }` ni `catch { return true; }`
- [ ] Los handlers del bridge devuelven error (no éxito falso) si fallan
- [ ] La cancelación de usuario NO se reporta como `ERROR`
- [ ] Los errores incluyen categoría + mensaje original

### Build / plataforma

- [ ] `PlatformTarget == x86`
- [ ] `TreatWarningsAsErrors == true` (no se ha desactivado)
- [ ] Sin nuevas dependencias NuGet sin ADR
- [ ] Sin cambios en `Directory.Build.props` sin ADR

### Tests

- [ ] Build `Release x86` verde: `scripts\dev.ps1 release`
- [ ] Tests verdes: `scripts\dev.ps1 test`
- [ ] Tests obligatorios del ADR afectado incluidos
- [ ] Sin `Thread.Sleep` en tests
- [ ] Tests sin Office (Category!=Integration) por defecto
- [ ] Cobertura de la capa no baja del mínimo (skill 09)

### Estilo / alcance

- [ ] Cambios dentro del alcance del doc de diseño
- [ ] Sin refactor oportunista
- [ ] UI y logs en español
- [ ] Commits con mensaje `tipo: descripción (refs design/...)`

---

## Cómo probar

<!--
Comandos exactos para que el revisor reproduzca.
Usa `dev.ps1` cuando sea posible.
-->

```powershell
# Build
scripts\dev.ps1 release

# Tests
scripts\dev.ps1 test

# Filtro específico del área tocada
scripts\dev.ps1 test -Filter "FullyQualifiedName~<área>"
```

## Cómo verificar manualmente

<!--
Checklist de pruebas manuales en Access real, si aplica.
Si no aplica, marca N/A.
Referencia: docs/manual-tests.md
-->

- [ ] Abrir Access
- [ ] Abrir VBE (Alt+F11)
- [ ] ...
- [ ] Cerrar Access y revisar el log

- [ ] N/A (no requiere verificación manual)

---

## Notas para el revisor

<!--
- Decisiones que quieras destacar.
- Trade-offs.
- Deuda técnica asumida (con issue asociado).
- Puntos donde quieres feedback específico.
- Si algo del checklist no se cumple y crees que hay razón,
  justifícalo aquí.
-->

## Issue relacionado

Closes #<n>
Refs #<n>

---

## Para el revisor (skill 10)

<!--
Esta sección la completa el revisor.
No la toca el autor.
-->

### Resultado de la revisión

- [ ] Aprobado (LGTM)
- [ ] Rechazado — ver comentarios

### Reglas violadas (si aplica)

| Regla | Skill / ADR | Comentario |
|-------|-------------|------------|
|       |             |            |

### Skills que el autor debe releer (si aplica)

- [ ] `.opencode/skills/bls-com-late-binding/SKILL.md`
- [ ] `.opencode/skills/bls-vbe-automation/SKILL.md`
- [ ] `.opencode/skills/bls-nim-client-streaming/SKILL.md`
- [ ] `.opencode/skills/bls-jsonrpc-pipe-bridge/SKILL.md`
- [ ] `.opencode/skills/bls-secure-store-dpapi/SKILL.md`
- [ ] `.opencode/skills/bls-winforms-threading/SKILL.md`
- [ ] `.opencode/skills/bls-registration-scripts/SKILL.md`
- [ ] `.opencode/skills/bls-logging-errors/SKILL.md`
- [ ] `.opencode/skills/bls-testing-strategy/SKILL.md`
- [ ] `skills/10-workflow-models.md`

### Comentarios

<!--
Feedback específico. Indicar archivo y línea cuando sea posible.
NO reescribir el código del autor: pedir cambios.
-->
```

---

## Notas sobre la plantilla

**Estructura en dos partes:**

1. **Parte del autor** (arriba): descripción, tipo, doc de diseño, checklist de rechazo, cómo probar, cómo verificar manualmente, notas.
2. **Parte del revisor** (abajo): resultado de la revisión, reglas violadas, skills a releer, comentarios.

Ambas viven en el mismo archivo. GitHub/GitLab las renderiza juntas al abrir el PR. El autor rellena la parte de arriba; el revisor, la de abajo. Es una convención, no un enforcement técnico.

**Por qué el checklist de rechazo vive aquí y no solo en `skills/10-workflow-models.md`:**

- El revisor lo tiene a la vista sin abrir otro archivo.
- El autor lo repasa antes de pedir revisión.
- El PR queda con evidencia de qué se verificó y cuándo.

**Cómo se integra con la CI:**

- La CI (`.github/workflows/ci.yml`) verifica automáticamente:
  - Build Debug + Release x86 verdes.
  - Tests verdes (sin integración).
  - Los 23 MDs presentes.
  - Sin PIAs de Office.
  - `dynamic` aislado (warning).
- El revisor humano verifica el resto:
  - Marshaling correcto.
  - API key nunca en claro.
  - Errores propagados.
  - Alcance acotado.

**Regla de uso:**

- Un PR sin la sección "Doc de diseño" (o sin marcar N/A) se rechaza.
- Un PR con ítems del checklist en negativo se rechaza, salvo justificación explícita en "Notas para el revisor".
- Un PR que no usa `dev.ps1` en "Cómo probar" no es bloqueante, pero se recomienda.

**Qué NO incluye esta plantilla (intencionadamente):**

- Tamaño máximo de diff (no hay límite rígido; se juzga caso a caso).
- Requerimiento de reviewer específico (lo decide el repo).
- Auto-asignación (lo gestiona GitHub/GitLab).
- Screenshots (no aplica: es un add-in sin UI visual relevante para PRs).
- Videos (no aplica).

Si más adelante quieres añadir secciones (por ejemplo, "Impacto en rendimiento" o "Compatibilidad con Access 2010/2016/365"), se añaden a la parte del autor.

**Cómo se ve un PR en la práctica:**

```
Autor abre PR
  ├── Rellena descripción, tipo, doc de diseño
  ├── Marca checklist (todos en positivo)
  ├── Añade comandos de prueba (dev.ps1)
  ├── Añade verificación manual (o N/A)
  └── Añade notas (trade-offs, deuda)

CI corre automáticamente
  ├── Build Debug ✓
  ├── Build Release ✓
  ├── Tests ✓
  ├── Docs presentes ✓
  ├── Sin PIAs ✓
  └── dynamic aislado ⚠ (warning)

Big Pickle revisa
  ├── Verifica checklist contra el diff
  ├── Marca "Aprobado" o "Rechazado"
  ├── Si rechazado: lista reglas violadas + skills a releer
  └── Comentarios específicos

Merge (squash)
```
