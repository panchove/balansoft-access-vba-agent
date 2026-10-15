# Registro de Solicitudes de Cambio (Change Requests - CRs)
# Balansoft Access VBA Agent

**Versión:** 1.0.0  
**Fecha:** 2026-10-15  
**Estado:** Activo  

---

## 1. Política de Change Requests
- Cada requerimiento de mejora, adición funcional o ajuste arquitectónico no previsto originalmente se documenta como un Change Request (CR).
- Estructura: ID, Título, Justificación / Necesidad, Componentes Afectados, Estado, Detalle de Implementación.

---

## 2. Change Requests Registrados

### CR-001: Botón explícito "Enviar" en interfaz de conversación
* **ID:** `CR-001`
* **Título:** Botón de envío en UI además del atajo de teclado Enter
* **Prioridad:** Media
* **Área:** `UI / BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  Originalmente la interfaz solo enviaba mediante la pulsación de la tecla `Enter`. Muchos usuarios en entornos de escritorio de Office prefieren tener un control visual clicable ("Enviar") junto a "Detener".
* **Detalle de implementación:**  
  Se añadió `_btnEnviar` (Button WinForms) en el panel de control inferior, sincronizando su estado `Enabled` con el ciclo de vida del turno (`Enabled = false` mientras se ejecuta el turno, `true` al finalizar).

---

### CR-002: Auto-ajuste responsivo de ancho de columna en ListView de mensajes
* **ID:** `CR-002`
* **Título:** Ajuste dinámico de columna "Mensaje" al tamaño de ventana
* **Prioridad:** Media
* **Área:** `UI / BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  La columna "Mensaje" con ancho fijo o `-2` estático generaba barras de desplazamiento horizontal o espacios en blanco excesivos al redimensionar la ventana.
* **Detalle de implementación:**  
  Se suscribió al evento `Resize` de `_conversation` para recalcular el ancho de `Columns[1]` restando el ancho de `Columns[0]` ("Rol") y la barra de desplazamiento vertical.

---

### CR-003: Logging enriquecido con InnerException y StackTrace detallado
* **ID:** `CR-003`
* **Título:** Logging completo de excepciones encadenadas en BlsLogger
* **Prioridad:** Alta
* **Área:** `Helpers / BlsLogger`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  Excepciones compuestas como `HttpRequestException` o `TargetInvocationException` no exponían la causa real en el archivo de registro diario, dificultando el soporte de incidencias de conectividad y COM.
* **Detalle de implementación:**  
  Se actualizó `BlsLogger.Write` para emitir `ex.ToString()` completo (que incluye la cadena íntegra de `InnerException` y trazas de pila).

---

### CR-004: Habilitación explícita de protocolos TLS 1.2 / TLS 1.3
* **ID:** `CR-004`
* **Título:** Forzar TLS 1.2 / TLS 1.3 en ServicePointManager
* **Prioridad:** Crítica
* **Área:** `UI / BlsNimClient` y `Core`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  En procesos Office 32-bit (Access 2010/2013/2016), el host puede inicializar el CLR con perfiles TLS heredados (SSL3 o TLS 1.0). El endpoint de NVIDIA NIM rechaza conexiones sin TLS 1.2+.
* **Detalle de implementación:**  
  Se aseguró la asignación `ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288` antes de cualquier llamada a `HttpClient`.

---

### CR-005: Tratamiento seguro y reporting descriptivo en fallos de red NIM
* **ID:** `CR-005`
* **Título:** Exponer detalle de error de red sin enmascaramiento opaco
* **Prioridad:** Media
* **Área:** `UI / BlsNimClient` y `BlsNimTerminal`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  El mensaje `"Error interno del cliente NIM"` ocultaba al usuario si el fallo se debía a falta de internet, proxy corporativo, timeout o rechazo de clave.
* **Detalle de implementación:**  
  Se formateó el mensaje de error incorporando el mensaje original y el de la excepción interna, mostrándolo en la columna de error de la terminal.

---

### CR-006: Adopción obligatoria de Diseño por Contrato (Design by Contract - DbC)
* **ID:** `CR-006`
* **Título:** Estandarización de contratos formales (Precondiciones, Postcondiciones e Invariantes)
* **Prioridad:** Crítica
* **Área:** `Core / Helpers / Architecture`
* **Fecha:** 2026-10-15
* **Estado:** `Implementado en v0.1.8`
* **Justificación:**  
  La arquitectura de COM Add-in dentro de Microsoft Access exige máxima robustez y prevención temprana de fallos. La validación ad-hoc dispersa se sustituye por contratos canónicos estrictos.
* **Detalle de implementación:**  
  - Creación de `Balansoft.AIAddin.Helpers.BlsContract`.
  - Creación de `docs/design/ADR-005-DESIGN-BY-CONTRACT.md` y `specs/010-design-by-contract.md`.
  - Incorporación a `docs/CONSTITUTION.md` (P13) y `AGENTS.md` (Regla 12).
  - Aplicación en todos los servicios y componentes del add-in.
  - Creación de suite `BlsContractTests` (17 pruebas unitarias).

---

## 3. Próximo CR ID disponible: `CR-007`
