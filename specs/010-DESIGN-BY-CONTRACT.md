# 010 — Diseño por Contrato (Design by Contract - DbC)

**Estado:** `implementado`  
**PRD:** FR-01..FR-12, Calidad y Robustez ISO/IEC 25010  
**ADRs:** ADR-005  
**Spec de diseño:** `docs/design/ADR-005-DESIGN-BY-CONTRACT.md`  
**Tests:** 17 verdes (`BlsContractTests`)  

---

## Contexto

El add-in opera en el entorno no administrado y de un solo hilo de Microsoft Access. Toda API pública debe validar formalmente sus precondiciones, postcondiciones e invariantes para evitar fallos catastróficos o corrupción silenciosa del host.

---

## Alcance

**Entra:**
- Clase central `Balansoft.AIAddin.Helpers.BlsContract`.
- Tipos de excepciones formales: `BlsPreconditionException`, `BlsPostconditionException`, `BlsInvariantException`.
- Precondiciones: `Requires`, `RequiresNotNull`, `RequiresNotNullOrWhiteSpace`, `RequiresRange`, `RequiresState`.
- Postcondiciones: `Ensures`, `EnsuresNotNull`.
- Invariantes: `Invariant`.
- Aplicación de contratos en:
  - `BlsPipeFraming`
  - `BlsChatHistory`
  - `BlsSecureStore`
  - `BlsBridgeClient`
  - `BlsVbeBridgeServer`
  - `BlsNimClient`
  - `BlsNimTerminal`
  - `BlsVbeService`
  - `BlsAccessService`
- Cobertura de tests unitarios al 100% de los métodos de contrato.

**No entra:**
- Reescritura binaria de IL o plugins de compilador pesados fuera del estándar de Roslyn.

---

## Requisitos

### R001 — Precondiciones de Argumentos
Todo método público que reciba parámetros de referencia obligatorios o cadenas no vacías debe validarlos mediante `BlsContract.RequiresNotNull` o `BlsContract.RequiresNotNullOrWhiteSpace` antes de ejecutar cualquier lógica.

### R002 — Precondiciones de Rango y Estado
Las operaciones que dependan de rangos numéricos positivos o estados internos no dispuestos deben validarse con `BlsContract.RequiresRange` o `BlsContract.RequiresState`.

### R003 — Postcondiciones
Métodos generadores o normalizadores deben garantizar que su resultado no sea nulo mediante `BlsContract.EnsuresNotNull`.

### R004 — Invariantes de Clase
Componentes de estado permanente deben garantizar que sus invariantes estructurales se conserven mediante `BlsContract.Invariant`.

---

## Criterios de Aceptación

```gherkin
Dado un argumento nulo pasado a una API pública
Cuando se ejecuta la llamada
Entonces se lanza ArgumentNullException indicando el nombre exacto del parámetro

Dado un parámetro de cadena vacío o con espacios
Cuando se ejecuta la llamada
Entonces se lanza ArgumentException indicando el parámetro

Dada una llamada sobre un componente dispuesto (disposed)
Cuando se invoca una operación pública
Entonces se lanza InvalidOperationException o ObjectDisposedException por violación de estado

Dado un método que promete retornar una referencia no nula
Cuando completa su ejecución
Entonces el valor devuelto no es nulo y satisface EnsuresNotNull
```

---

## Tests Obligatorios

| Test | Cubre |
|---|---|
| `Requires_TrueCondition_DoesNotThrow` | R001 |
| `Requires_FalseCondition_ThrowsBlsPreconditionException` | R001 |
| `RequiresNotNull_NonNullArgument_ReturnsValue` | R001 |
| `RequiresNotNull_NullArgument_ThrowsArgumentNullException` | R001 |
| `RequiresNotNullOrWhiteSpace_ValidString_ReturnsString` | R001 |
| `RequiresNotNullOrWhiteSpace_InvalidString_ThrowsArgumentException` | R001 |
| `RequiresRange_ConditionFalse_ThrowsArgumentOutOfRangeException` | R002 |
| `RequiresRange_ConditionTrue_DoesNotThrow` | R002 |
| `RequiresState_ConditionFalse_ThrowsInvalidOperationException` | R002 |
| `Ensures_ConditionTrue_DoesNotThrow` | R003 |
| `Ensures_ConditionFalse_ThrowsBlsPostconditionException` | R003 |
| `EnsuresNotNull_NonNull_ReturnsValue` | R003 |
| `EnsuresNotNull_Null_ThrowsBlsPostconditionException` | R003 |
| `Invariant_ConditionTrue_DoesNotThrow` | R004 |
| `Invariant_ConditionFalse_ThrowsBlsInvariantException` | R004 |
