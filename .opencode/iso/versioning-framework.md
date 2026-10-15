# ISO/IEC Documentation Versioning & Alignment Framework

## Versión del esquema: 1.0.0 (2025-10-15)

### Estándares aplicables
- **ISO/IEC 29148**: Systems and software engineering — Life cycle processes — Requirements engineering
- **ISO/IEC 25010**: Systems and software quality model
- **ISO/IEC 12207**: Systems and software life cycle processes
- **ISO/IEC 42001**: Artificial intelligence management system (aplicable a habilidades de IA)

### Schema de versionado documental

Cada documento debe llevar metadatos en formato YAML al inicio:

```yaml
--- 
version: major.minor.patch
date: YYYY-MM-DD
status: draft|review|approved|deprecated
iso_standard: [29148, 25010, 12207]
aligns_to: [ADR-001, ADR-002, ...]
tags: [com, vim, nim, dpapi, ...]
dependencies: [skill-00, skill-01, ...]
conflicts_with: []
replaces: []
reviewed_by: []
---
```

### Niveles de status ISO/IEC
- `draft` — En redacción, sujeto a cambios
- `review` — En revisión por pares
- `approved` — Aprobado y base para implementation
- `deprecated` — Obsoleto, removido del flujo activo

### Convenios de versionado

| Componente          | Esquema        | Ejemplo         |
|---------------------|----------------|-----------------|
| Documento principal | vX.Y.Z         | v1.2.0          |
| Skill individual    | vX.Y.Z-skill-N | v1.0.0-skill-03 |
| ADR                 | v1.X-ADR-N     | v1.3-ADR-002    |
| Configuración       | vX.Y           | 0.1             |

### Alineación cruzada

Cada documento debe declarar qué otros documentos afecta y por los cuales es responsable:

```yaml
aligns_to:
  - ADR-001-project-identity
  - AGENTS.md-regla-06-x86
  - PRD.md-FR-03
  - bls-vbe-automation
  
conflicts_with: []
replaces: [v1.0.0-old-skill-name]
```

### Metadatos requeridos por tipo de documento

#### Skills (bls-*/SKILL.md)
```yaml
version: 1.0.0
iso_standard: [29148, 12207]
tags: [bls-com-late-binding, com, late-binding, x86]
aligns_to: [ADR-001, ADR-002, AGENTS.md-regla-06]
dependencies: []
conflicts_with: []
reviewed_by: [bls-architect, bls-reviewer]
status: approved
date: 2025-10-15
```

#### Agentes (.opencode/agents/*.md)
```yaml
version: 1.0.0
iso_standard: [12207]
tags: [bls-architect, workflow, management]
aligns_to: [AGENTS.md, PRD.md]
dependencies: [bls-planner, bls-implementer, bls-reviewer]
conflicts_with: []
replaces: []
reviewed_by: [bls-documentador]
status: approved
date: 2025-10-15
```

#### PRD y AGENTS (raíz)
```yaml
version: 1.0.0
iso_standard: [25010, 12207]
tags: [product, requirements, access-addin]
aligns_to: [all ADRs, all skills]
dependencies: []
conflicts_with: []
reviewed_by: [bls-documentador, bls-architect]
status: approved
date: 2025-10-15
```

### Sistema de tracking de cambios

Cada cambio documentado debe seguir:

```
# CHANGE LOG ENTRY FORMATO ISO/IEC

- [YYYY-MM-DD] vX.Y.Z - <tipo de cambio>
  - Author: <nombre>
  - Summary: <descripción breve>
  - Impact: [bug|feature|doc|security|breaking]
  - Related: [ADR-N, skill-N, FR-N]
  - Testing: [test-nombre|manual|none]
  - Deprecates: [vA.B.C]
  - Related to: [issue-|PR-]
```

### Generación automática de índice

El documento `opencode.json` debe incluir un bloque `iso_versioning`:

```json
{
  "iso_versioning": {
    "schema_version": "1.0.0",
    "last_generated": "2025-10-15T00:00:00Z",
    "total_documents": 16,
    "approved_documents": 14,
    "draft_documents": 2,
    "skills_indexed": 11,
    "adrs_aligned": 4,
    "last_audit": "2025-10-15"
  }
}
```

### Auditoría de alineación

Ejecutar periódicamente:

```bash
# Verificar que todos los skills referencien sus ADRs
$ skills = Get-ChildItem ".opencode/bls-*" -Directory
foreach ($skill in $skills) {
    $content = Get-Content "$($skill)/SKILL.md"
    if (-not ($content -match "ADR-")) {
        Write-Host "⚠️ Skill $($skill.Name) no referencia ADRs"
    }
}
# Verificar consistencia de versiones
# Verificar que opencode.json refleje la realidad
```

### Plantillas ISO/IEC recomendadas

#### Para Skills:
```
# SKILL N - nombre-del-skill

> **Referencia ISO/IEC**: 29148 (Engineering of requirements)
> **Área**: com | vim | nim | bridge | dpapi | ui | registry | tests | workflow
> **Estado**: approved | draft | deprecated
> **Última revisión**: YYYY-MM-DD

> **Cuándo cargar este skill:** ...
```

#### Para Agentes:
```
# Agente: bls-nombre

> **Referencia ISO/IEC**: 12207 (Life cycle processes)
> **Rol**: coordinator | planner | implementer | reviewer | documentador
> **Estado**: approved
> **Fecha creación**: YYYY-MM-DD

> **Responsabilidades**:
> - ...
```

#### Para Decisiones (ADRs):
```
# ADR-N - título

> **Estado**: accepted | superseded | deprecated
> **Fecha**: YYYY-MM-DD
> **Autor**: <nombre>
> **Impacto**: [obligatorio|recomendado|opcional]
> **Consecuencias positivas**: [...]
> **Consecuencias negativas**: [...]
> **Alternativas consideradas**: [...]
> **Referencias**: [PRD-FR-N, skill-N, código-afectado]
> **Revisiones**: 
> - v1.0.0 (YYYY-MM-DD): creación por <nombre>
> - v1.1.0 (YYYY-MM-DD): actualización por <nombre>
```

### Implementación en el proyecto

1. **Añadir metadatos YAML** a todos los documentos existentes
2. **Actualizar opencode.json** con bloque `iso_versioning`
3. **Crear scripts de validación** para verificar alineación
4. **Establecer flujo de trabajo** para nuevo versionado en PRs
5. **Capacitar al equipo** en formato y procedimientos

### Próximos pasos

1. **Fase 1** (inmediatez): Añadir metadatos YAML a todos los .md existentes
2. **Fase 2** (1 semana): Crear scripts de validación de alineación ISO/IEC
3. **Fase 3** (2 semanas): Implementar flujo de versionado en PRs
4. **Fase 4** (continuo): Mantener y auditar el esquema

---
*Framework generado el 2025-10-15 para Balansoft Access VBA Agent (NIM Direct)*
*Compatible con ISO/IEC 29148, 25010, 12207 y 42001:2023*