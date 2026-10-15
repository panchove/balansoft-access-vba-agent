# ADR-005: Adopción de Diseño por Contrato (Design by Contract - DbC)

**Estado:** Aceptado  
**Fecha:** 2026-10-15  
**Decisores:** bls-architect, bls-implementer  
**Consulta:** Constitución P13, PRD §1, ISO/IEC 25010 (fiabilidad y robustez)  

---

## 1. Contexto

El add-in `Balansoft.AIAddin.Access` se ejecuta dentro del espacio de direcciones de Microsoft Access (`MSACCESS.EXE`), un host nativo x86 sin tolerancia a fallos ni capacidad de reinicio transparente sin pérdida de datos del usuario.

Hasta la versión 0.1.7, la validación de argumentos dependía de comprobaciones ad-hoc dispersas (algunas usando `ArgumentNullException`, otras validaciones tardías, o sin comprobación formal de rangos y estados). Esto provocó fallos en runtime difíciles de aislar (como serializaciones con propiedades faltantes, streams no aptos para lectura o llamadas a componentes ya dispuestos).

Para garantizar la máxima fiabilidad, se requiere una política estricta de **Diseño por Contrato (Design by Contract - DbC)** en todo el código base.

---

## 2. Decisión

Se adopta **Diseño por Contrato (DbC)** formal como directriz no negociable del proyecto:

1. **Infraestructura Unificada (`BlsContract`):**
   Toda comprobación contractual se canaliza a través de `Balansoft.AIAddin.Helpers.BlsContract`, optimizada con `[MethodImpl(MethodImplOptions.AggressiveInlining)]`.

2. **Precondiciones (`Requires`):**
   - Todo método o constructor público debe validar sus entradas inmediatamente al inicio.
   - `BlsContract.RequiresNotNull`: garantiza que referencias obligatorias no sean nulas (`ArgumentNullException`).
   - `BlsContract.RequiresNotNullOrWhiteSpace`: garantiza cadenas válidas y no vacías (`ArgumentException`).
   - `BlsContract.RequiresRange`: garantiza que enteros, timeouts y tamaños estén dentro de los límites válidos (`ArgumentOutOfRangeException`).
   - `BlsContract.RequiresState`: valida que el estado interno del objeto admita la operación (`InvalidOperationException`).
   - `BlsContract.Requires`: para condiciones booleanas generales del llamador (`BlsPreconditionException`).

3. **Postcondiciones (`Ensures`):**
   - Métodos constructores o de transformación garantizan que su resultado satisfaga el contrato prometido (`EnsuresNotNull`, `Ensures`).
   - Ante una falla de postcondición, se lanza `BlsPostconditionException`, atribuyendo el error a la implementación y no al cliente.

4. **Invariantes (`Invariant`):**
   - Métodos públicos garantizan que el estado interno fundamental de la instancia sea coherente antes y después de su ejecución mediante `BlsContract.Invariant` (`BlsInvariantException`).

5. **Exclusión de dependencias externas:**
   - No se utiliza Code Contracts de .NET heredado (que requiere reescritura de IL / MSBuild post-processing), sino aserciones directas de alto rendimiento compatibles con .NET Framework 4.8 y C# 10.

---

## 3. Consecuencias

### Positivas
- **Detección temprana de bugs:** Cualquier violación de contrato falla de inmediato en el punto de entrada con el nombre exacto del parámetro.
- **Claridad de responsabilidades:** Separa de forma inequívoca si la falla es del llamador (precondición) o del proveedor (postcondición).
- **Compatibilidad total con CI y tests existentes:** Al lanzar excepciones estándar de la BCL (`ArgumentNullException`, `ArgumentException`), las suites de pruebas unitarias existentes no se rompen.
- **Auditoría y cobertura:** Se incorpora una suite de tests dedicada (`BlsContractTests`) con 17 pruebas verificando cada contrato.

### Negativas / Mitigaciones
- Ligeramente más líneas de código al inicio de cada método (mitigado por métodos fluídos y concisos de `BlsContract`).
