# SKILL 10 — Workflow Big Pickle + Nemotron 3 Ultra

> **Cuándo cargar este skill:** al planificar una feature nueva, al abrir
> un PR, al revisar un diff, al decidir qué modelo ejecuta qué tarea, o
> al resolver una contradicción entre skills y ADRs.

---

## Objetivo

Definir **quién hace qué** entre los dos modelos disponibles, **cómo se
pasa el testigo**, y **qué se rechaza automáticamente** en revisión para
que la calidad no dependa de la memoria del revisor.

- **Big Pickle** → arquitecto y revisor.
- **Nemotron 3 Ultra** → implementador y debugger puntual.

Regla base: **Big Pickle decide interfaces y contratos → Nemotron
implementa contra esos contratos → Big Pickle revisa el diff**.

---

## Reparto de responsabilidades

| Fase                  | Modelo           | Entregable                                                                             |
|-----------------------|------------------|----------------------------------------------------------------------------------------|
| Diseño de feature     | Big Pickle       | `docs/design/YYYYMMDD-feature.md` + interfaces + invariantes + criterios de aceptación |
| Nuevo ADR (si aplica) | Big Pickle       | `docs/design/ADR-00X-*.md`                                                             |
| Implementación        | Nemotron 3 Ultra | Diff C#, scripts PS1, tests                                                            |
| Tests obligatorios    | Nemotron 3 Ultra | Tests de ADR + transversales del skill 09                                              |
| Revisión de diff      | Big Pickle       | Aprobación o rechazo con checklist                                                     |
| Debug puntual         | Nemotron 3 Ultra | Parche mínimo + test de regresión                                                      |
| Cambio de interfaz    | Big Pickle       | Nuevo doc en `docs/design/` + actualización del skill afectado                         |

### Lo que **no** hace cada modelo
- Big Pickle **no** escribe código de implementación (solo interfaces y
  firmas si hace falta).
- Big Pickle **no** mergea su propio diseño sin implementación probada.
- Nemotron **no** cambia interfaces ni ADRs sin volver a Big Pickle.
- Nemotron **no** toca `PlatformTarget`, `LangVersion` ni
  `Directory.Build.props` sin un ADR aprobado.
- Ninguno de los dos **elimina o renombra** un ADR sin crear el ADR
  sucesor.

---

## Protocolo de handoff (5 pasos)

### Paso 1 — Big Pickle diseña
Escribe `docs/design/YYYYMMDD-feature.md` con:

```markdown
# Feature: <nombre>

## Contexto
Qué problema resuelve, qué parte del PRD cubre.

## Interfaces nuevas / modificadas
Firmas exactas (C#) de los contratos afectados.

## Invariantes afectados
Lista de ADRs y reglas de AGENTS.md que toca.
Referencia explícita: ADR-001..004, skills 01..09.

## Criterios de aceptación
Lista verificable (cada uno con su test asociado del skill 09).

## Skills que el implementador debe leer
Lista concreta: "00, 02, 04, 08, 09".

## Fuera de alcance
Lo que NO se hace en esta feature (evita scope creep).
```

### Paso 2 — Nemotron implementa
- Lee `AGENTS.md` + skill 00 + los skills citados en el doc + los ADRs
  referenciados.
- Implementa en C# .NET 4.8 x86, sin PIAs, con tests.
- No modifica interfaces sin volver al paso 1.
- Commit message: `feat: <nombre> (refs design/YYYYMMDD-feature.md)`.

### Paso 3 — Nemotron entrega PR
El PR debe incluir:
- Build `Release x86` verde.
- `dotnet test` verde (sin integración NIM).
- Tests nuevos (de ADR si aplica + transversales si aplica).
- Diff acotado al scope del doc.
- `CHANGELOG.md` actualizado si la feature es visible al usuario.

### Paso 4 — Big Pickle revisa
Aplica el **checklist de rechazo** (ver abajo). Si todo pasa:
- Aprueba con comentario `LGTM — skills 00, 02, 04, 08, 09`.
Si algo falla:
- Rechaza indicando la regla violada y el skill a releer.
- **No** reescribe el código él mismo; pide al implementador.

### Paso 5 — Merge
- Un solo commit por feature (squash si hay ruido).
- Actualizar ADRs si la implementación reveló una decisión nueva.
- Actualizar el skill afectado si el ADR cambió.

---

## Prompts base

### Big Pickle — diseño
```
Actúa como arquitecto del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/00-project-context.md
3. Los ADRs relevantes en docs/design/
4. Los skills que probablemente toque la feature (01-09)

Diseña: <feature>.

Entrega:
- docs/design/YYYYMMDD-feature.md con la estructura del protocolo de handoff
- Interfaces C# exactas (solo firmas)
- Lista de invariantes afectados (ADRs + reglas de AGENTS.md)
- Criterios de aceptación, cada uno con su test
- Skills que el implementador debe leer

NO escribas código de implementación.
NO inventes interfaces que ya existen en skills 01-09.
Si detectas una contradicción entre ADRs y skills, señálala.
```

### Nemotron 3 Ultra — implementación
```
Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Lee en este orden:
1. AGENTS.md
2. .opencode/skills/00-project-context.md
3. docs/design/YYYYMMDD-feature.md (el diseño)
4. Los skills citados en el diseño
5. Los ADRs referenciados en el diseño

Implementa: <feature>.

Restricciones duras (ver AGENTS.md):
- .NET Framework 4.8, LangVersion 10, Nullable disable, PlatformTarget=x86
- Sin PIAs de Office, COM late-bound vía dynamic aislado en Helpers/BlsComHelper.cs
- Marshaling al hilo UI para todo acceso VBE/Access
- API key solo DPAPI; nunca en log/JSON/registry
- Errores reales propagados; nunca "hecho" falso
- Framing pipe: uint32 LE + UTF-8 JSON (ADR-002)
- Abort: un CTS por turno (ADR-003)
- Identidad de proyecto: SHA256(ruta normalizada) (ADR-001)
- object_detail: campos siempre, SQL opt-in (ADR-004)

Entrega:
- Diff completo
- Tests nuevos (de ADR + transversales según skill 09)
- Comando exacto para verificar (build + test)
- Commit message: "feat: <nombre> (refs design/YYYYMMDD-feature.md)"

NO cambies interfaces sin volver a Big Pickle.
NO toques Directory.Build.props ni PlatformTarget.
```

### Nemotron 3 Ultra — debug puntual
```
Actúa como implementador del proyecto Balansoft Access VBA Agent (NIM Direct).

Bug: <descripción + traza del log>.

Lee:
- AGENTS.md
- .opencode/skills/00-project-context.md
- El skill del área afectada (01-09)
- El ADR relevante si aplica

Entrega:
- Parche mínimo (no refactor oportunista)
- Test de regresión que reproduce el bug
- Explicación de la causa raíz en una línea

NO cambies el comportamiento fuera del bug.
```

---

## Checklist de rechazo automático (Big Pickle revisa)

Cualquier ítem marcado ⇒ **rechazo inmediato**, sin discusión:

### COM / VBE
- [ ] ¿Hay `using Microsoft.Office.Interop.*`? → rechazar.
- [ ] ¿Se usa `Marshal.GetActiveObject` o ROT? → rechazar.
- [ ] ¿Hay `dynamic` fuera de `Helpers/BlsComHelper.cs`? → rechazar.
- [ ] ¿Se toca VBE sin pasar por `IUiScheduler`? → rechazar.
- [ ] ¿Se usa `Marshal.ReleaseComObject`? → rechazar.
- [ ] ¿Se asume `VBE != null` sin comprobar? → rechazar.

### Seguridad
- [ ] ¿La API key aparece en log, JSON, excepción o registry? → rechazar.
- [ ] ¿`DataProtectionScope.LocalMachine`? → rechazar.
- [ ] ¿Se loguea el body completo de NIM o el código VBA completo? → rechazar.

### Protocolo / ADRs
- [ ] ¿Framing del pipe distinto a ADR-002? → rechazar.
- [ ] ¿Abort distinto a ADR-003 (p. ej. CTS por request)? → rechazar.
- [ ] ¿Identidad de proyecto distinta a ADR-001? → rechazar.
- [ ] ¿`object_detail` distinto a ADR-004? → rechazar.

### Errores
- [ ] ¿`catch (Exception) { }` o `catch { return true; }`? → rechazar.
- [ ] ¿Un handler del bridge devuelve éxito cuando falló? → rechazar.
- [ ] ¿Cancelación de usuario reportada como `ERROR`? → rechazar.

### Build / plataforma
- [ ] ¿`PlatformTarget` distinto de `x86`? → rechazar.
- [ ] ¿`TreatWarningsAsErrors` desactivado? → rechazar.
- [ ] ¿Nueva dependencia NuGet sin ADR? → rechazar.
- [ ] ¿Cambio en `Directory.Build.props` sin ADR? → rechazar.

### Tests
- [ ] ¿Falta el test de ADR correspondiente? → rechazar.
- [ ] ¿Se bajó el número de tests sin justificar? → rechazar.
- [ ] ¿Test con `Thread.Sleep`? → rechazar.
- [ ] ¿Test que toca COM real fuera de `Category=Integration`? → rechazar.

### Estilo / alcance
- [ ] ¿Cambios fuera del scope del doc de diseño? → rechazar.
- [ ] ¿Refactor oportunista? → rechazar.
- [ ] ¿UI o logs en inglés? → rechazar.

---

## Manejo de contradicciones

### Skill ↔ ADR
Gana el **ADR**. El PR que detecta la contradicción debe:
1. Actualizar el skill para alinearlo con el ADR.
2. Referenciar el ADR en el commit.
3. No introducir un ADR nuevo salvo que el ADR vigente esté mal.

### Doc de diseño ↔ ADR
Gana el **ADR**. El doc de diseño se corrige antes de implementar.

### Dos ADRs en conflicto
- Big Pickle abre un **ADR sucesor** que supersede al anterior.
- El ADR anterior se marca `Superseded by ADR-00Y`.
- El skill afectado se actualiza en el mismo PR.

### Implementación ↔ diseño
- Si la implementación revela que el diseño es inviable, **parar** y
  volver al paso 1. No "arreglar sobre la marcha".

---

## Convenciones de commit

| Tipo    | Formato                                            | Ejemplo                                                  |
|---------|----------------------------------------------------|----------------------------------------------------------|
| Feature | `feat: <nombre> (refs design/YYYYMMDD-feature.md)` | `feat: bridge JSON-RPC (refs design/20250101-bridge.md)` |
| Fix     | `fix: <descripción> (closes #<issue>)`             | `fix: framing pipe con payload UTF-8 (closes #12)`       |
| ADR     | `docs: ADR-00X <título>`                           | `docs: ADR-005 rate limit NIM`                           |
| Skill   | `docs: skill NN <cambio>`                          | `docs: skill 04 añade -32004 busy`                       |
| Test    | `test: <descripción>`                              | `test: Turno_CanceladoDuranteToolVbe`                    |
| Build   | `chore: <descripción>`                             | `chore: bump version 0.1.1`                              |

Reglas:
- Un commit por unidad lógica.
- No mezclar feature + refactor.
- No mezclar feature + cambio de ADR (van en commits separados).

---

## Métricas de salud del flujo

Se revisan cada release:

| Métrica                              | Objetivo                                  |
|--------------------------------------|-------------------------------------------|
| PRs rechazados por checklist         | < 30% (si sube, falta doc en diseño)      |
| Tiempo diseño → implementación       | < 2 días por feature media                |
| Tests nuevos por feature             | ≥ tests de ADR afectados                  |
| ADRs superseded sin actualizar skill | 0                                         |
| Bugs que llegan a producción         | 0 en rutas críticas (COM, DPAPI, framing) |

---

## Anti-patrones de proceso (rechazo)

| Anti-patrón                                         | Por qué                        |
|-----------------------------------------------------|--------------------------------|
| Big Pickle escribe código de implementación         | Rompe el reparto               |
| Nemotron cambia interfaces "porque era obvio"       | Rompe el contrato              |
| PR sin doc de diseño previo                         | No hay criterios de aceptación |
| Merge de diseño sin implementación probada          | No se sabe si funciona         |
| Fix sin test de regresión                           | El bug vuelve                  |
| Feature sin actualizar el skill afectado            | Drift doc-código               |
| ADR nuevo sin superseder el anterior                | Ambigüedad                     |
| Ignorar el checklist de rechazo "porque es pequeño" | Erosiona calidad               |

---

## Checklist antes de abrir un PR

- [ ] ¿Existe `docs/design/YYYYMMDD-feature.md`?
- [ ] ¿El commit message referencia el doc?
- [ ] ¿Build `Release x86` verde?
- [ ] ¿`dotnet test` verde (sin integración)?
- [ ] ¿Tests de ADR afectados incluidos?
- [ ] ¿Cobertura de la capa no baja del mínimo (skill 09)?
- [ ] ¿Sin PIAs, sin `dynamic` fuera de `Helpers/`?
- [ ] ¿Sin API key en log/JSON/excepción?
- [ ] ¿Errores propagados con categoría + mensaje original?
- [ ] ¿Cancelación ≠ error?
- [ ] ¿Alcance acotado al doc (sin refactor oportunista)?
- [ ] ¿Skills afectados actualizados si aplica?
- [ ] ¿ADRs actualizados si aplica?

---

## Referencias

- `AGENTS.md` (reglas duras + flujo + comandos).
- `docs/design/ADR-001..004` (fuente de verdad).
- Todos los skills 00-09 (invariantes por área).
- PRD §5 (objetivos), §11 (criterios de aceptación MVP).
```

---

# ✅ Entrega completa: 16/16 MDs

```
AGENTS.md
docs/design/ADR-001-project-identity.md
docs/design/ADR-002-pipe-framing.md
docs/design/ADR-003-abort-model.md
docs/design/ADR-004-object-detail.md
.opencode/skills/00-project-context.md
.opencode/skills/01-com-late-binding.md
.opencode/skills/02-vbe-automation.md
.opencode/skills/03-nim-client-streaming.md
.opencode/skills/04-jsonrpc-pipe-bridge.md
.opencode/skills/05-secure-store-dpapi.md
.opencode/skills/06-winforms-threading.md
.opencode/skills/07-registration-scripts.md
.opencode/skills/08-logging-errors.md
.opencode/skills/09-testing-strategy.md
.opencode/skills/10-workflow-big-pickle-nemotron.md
```

## Cierre

- **Los 4 gaps del PRD quedan resueltos** en ADRs y propagados a los skills:
  - ADR-001 → identidad de proyecto (skills 02, 05).
  - ADR-002 → framing del pipe (skill 04).
  - ADR-003 → abort (skills 03, 04, 06, 08).
  - ADR-004 → `object_detail` (skills 04, 09).
- **Coherencia cruzada verificada**: cada skill referencia los ADRs que le aplican, y cada ADR lista los skills afectados.
- **Trazabilidad de tests**: los 36 tests obligatorios de ADR + 29 transversales + 6 de integración NIM = **79 tests del MVP**.
- **Rechazo automático**: el checklist de skill 10 cierra la puerta a los anti-patrones más probables de cada modelo.
