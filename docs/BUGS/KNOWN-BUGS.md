# Registro de Bugs Conocidos - Balansoft Access VBA Agent

## Versión: 1.0.0
## Fecha: 2025-10-15
## Estado: abierto

## Política de documentación de bugs
- Todos los bugs deben registrarse en este archivo antes de crear un issue
- Cada bug debe tener: ID, descripción, gravedad, área afectada, workaround si existe
- Los bugs críticos bloquean el release; los menores se acumulan para la próxima versión
- Los bugs duplicados deben marcarse como tale

---

## Bugs Registrados

### Bug #1: BalanceSoft-001
**Título**: Add-in no carga en Access modo runtime (.accde)
**Gravedad**: Critical
**Área**: Registration | COM loading
**Fecha**: 2025-10-10
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
El add-in falla al cargarse cuando Access está en modo runtime (.accde) porque `Application.VBE` es nulo. El código actual asume que VBE siempre está disponible después de `OnConnection`.

**Impacto**: El usuario no puede usar la terminal NIM en archivos .accde.

**Workaround**: 
- Verificar `IsAvailable` property antes de cualquier operación VBE
- Mostrar mensaje informativo: "Terminal NIM no disponible en modo runtime"

**Fecha de corrección estimada**: Próxima versión v1.1.0

**Referencias**: 
- ADR-001 (Identidad de proyecto)
- .opencode/skills/bls-vbe-automation/SKILL.md
- AGENTS.md regla 4

---

### Bug #2: BalanceSoft-002
**Título**: Memory leak en streaming largo de NIM
**Gravedad**: Medium
**Área**: UI | NimClient | streaming
**Fecha**: 2025-10-12
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
Durante streams de NIM prolongados (>30 minutos), el consumo de memoria del add-in aumenta progresivamente sin liberarse adecuadamente. Se sospecha que chunks SSE no se están liberando de memoria después de procesarlos.

**Impacto**: El add-in puede consumir toda la memoria disponible y causar lentitud o cierre de Access.

**Workaround**: 
- Reiniciar el add-in periódicamente
- Limitar la duración de los prompts a 20 minutos

**Fecha de corrección estimada**: v1.2.0

**Referencias**: 
- .opencode/skills/bls-nim-client-streaming/SKILL.md
- AGENTS.md regla 5 (errores reales)

---

### Bug #3: BalanceSoft-003
**Título**: Timeout incorrecto en chunks de tool_call
**Gravedad**: High
**Área**: NIM | streaming | tool_calls
**Fecha**: 2025-10-13
**Reporte por**: Sistema de documentación ISO/IEC
**Estado**: Abierto

**Descripción**:
El timeout de 120 segundos por chunk se reinicia incorrectamente cuando hay múltiples tool_calls fragmentados. Si el modelo envía tool_calls en 5 chunks separados, el timeout se reinicia 5 veces en lugar de ser global para toda la operación.

**Impacto**: Los tool_calls pueden timeout injustificadamente o no timeout cuando deberían.

**Workaround**: 
- Monitorear los logs en busca de códigos -32002
- Reenviar el prompt si se detecta timeout indebido

**Fecha de corrección estimada**: v1.1.1

**Referencias**: 
- .opencode/skills/bls-nim-client-streaming/SKILL.md
- ADR-003 (Abort model)
- AGENTS.md regla 5

---

## Próximo Bug ID disponible: BalanceSoft-004

### Política de cierre de bugs
1. Cuando se corrige un bug, se añade la fecha de corrección
2. Se marca el estado como "Fixed in vX.Y.Z"
3. Se añade a la sección "Bugs Resueltos" del CHANGELOG.md
4. El bug queda en el registro histórico pero ya no es "abierto"

### Próxima revisión de bugs
- Fecha: 2025-11-15
- Responsable: bls-documentador
- Verificar: todos los bugs abiertos desde v1.0.0