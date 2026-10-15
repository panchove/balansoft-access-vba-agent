---
version: 1.0.0
date: 2026-10-15
status: approved
iso_standard: [25010, 12207]
aligns_to: [AGENTS.md, PRD.md, .opencode/skills/bls-testing-strategy/SKILL.md]
---

# Checklist de Pruebas Manuales de Integración Access (Pre-Release)

> **Referencia ISO/IEC**: 12207:2017 (Verification & Validation) + 25010:2023 (Quality Requirements)  
> **Área**: testing | manual | access-integration  
> **Estado**: approved  

---

## Propósito

Documentar el procedimiento y la lista de comprobación de pruebas de integración en entorno real con Microsoft Access (arquitectura x86 obligatoria) antes de publicar un tag de release `vX.Y.Z`. Estas pruebas cubren interacciones con el host COM que no pueden ser automatizadas en CI sin una instalación real de Office Access.

---

## Entornos de Prueba Requeridos

- Windows 10 / 11 (64-bit o 32-bit OS)
- Microsoft Access 32-bit (x86): versiones 2010, 2016, 2019 o Microsoft 365 x86
- Conexión a Internet activa y clave de API Nvidia NIM (`nvapi-...`)

---

## Procedimiento Previo

1. Compilar paquete Release en x86:
   ```powershell
   pwsh -ExecutionPolicy Bypass -File scripts\Pack.ps1 -Configuration Release
   ```
2. Registrar el add-in en HKCU (desde sesión PowerShell de 32-bit):
   ```powershell
   pwsh -ExecutionPolicy Bypass -File scripts\RegisterAddin.ps1 -Configuration Release
   ```
3. Verificar registro inicial en el Registro de Windows:
   - Clave: `HKCU\Software\Microsoft\Office\Access\Addins\Balansoft.AIAddin.Access`
   - `LoadBehavior`: `3` (DWORD)

---

## Lista de Comprobación Manual (Gate de Release)

| # | Escenario de Prueba | Resultado Esperado | Estado |
|---|---------------------|--------------------|--------|
| 1 | **Arranque de Access x86** | Access abre sin errores ni advertencias de seguridad. | [ ] |
| 2 | **Persistencia de LoadBehavior** | `LoadBehavior` permanece en `3` (no se degrada a `2`). | [ ] |
| 3 | **Menú Tools en VBE** | Al abrir el Editor VBA (Alt+F11), el menú *Tools* contiene el comando *"NIM Terminal"*. | [ ] |
| 4 | **Apertura de Ventana Terminal** | Al hacer clic en *"NIM Terminal"*, se abre la ventana WinForms con el VBE como ventana propietaria (owner). | [ ] |
| 5 | **Solicitud y Almacenamiento de API Key** | Si no existe clave, se solicita mediante diálogo y se almacena en DPAPI (`%APPDATA%\Balansoft\AIAddin\apikey.bin`). | [ ] |
| 6 | **Streaming en Tiempo Real** | Al enviar un prompt simple, los tokens SSE se renderizan de forma progresiva sin congelar el editor. | [ ] |
| 7 | **Tool Call: create_module** | El modelo invoca la herramienta y se crea un módulo real en el proyecto VBA visible en el Explorador de Proyectos. | [ ] |
| 8 | **Cancelación de Turno** | Al pulsar el botón *"Detener"*, se cancela el request inmediatamente y no se reporta como error en UI. | [ ] |
| 9 | **Persistencia de Historial** | Al cerrar la ventana terminal y volver a abrirla, la conversación previa se restaura desde `{projectKey}.json`. | [ ] |
| 10 | **Modo Runtime (.accde / /runtime)** | Al abrir una base en runtime donde `VBE == null`, el add-in degrada limpiamente y no genera crash. | [ ] |
| 11 | **Verificación de Logs** | El log en `%APPDATA%\Balansoft\AIAddin\logs\` no contiene claves API ni fragmentos `nvapi-...`. | [ ] |

---

## Procedimiento de Desregistro Posterior

```powershell
pwsh -ExecutionPolicy Bypass -File scripts\UnregisterAddin.ps1
```
