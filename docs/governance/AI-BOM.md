---
version: 1.0.0
date: 2026-11-19
status: approved
iso_standard: [42001, 27001, 25010]
aligns_to: [AGENTS.md, AI-POLICY.md]
tags: [ai-bom, sbom, supply-chain, model-inventory]
---

# AI-BOM — Inventario de componentes de IA

> Pilar 10: modelos, prompts, adaptadores, servidores MCP y dependencias inventariados y versionados.
> Toda cambio en esta tabla debe entregarse en el mismo PR que el cambio (Pilar 17).

## 1. Modelo

| Campo | Valor |
|-------|-------|
| Proveedor | NVIDIA (integrate.api.nvidia.com) |
| Modelo | `nvidia/nemotron-3.5-lightning-30b-a3b` |
| Override | Variable de entorno `NIM_MODEL` |
| Endpoint | `https://integrate.api.nvidia.com/v1/chat/completions` (`NIM_ENDPOINT`) |
| Protocolo | HTTP/1.1 con streaming SSE |
| Modalidad | Texto + function calling |
| Región/datos | Sin configuración de residencia de datos declarada ⚠️ |
| Uso | Asistente de edición VBA en el VBE de Access/Excel |

## 2. Prompt del sistema

| Campo | Valor |
|-------|-------|
| Identificador | `PROMPT-v1.0` |
| Fuente | `src/Balansoft.AIAddin.Access/UI/SystemPromptBuilder.cs` |
| SHA-256 | `3A8CD21FAF891BB0B5D4B74EDAEFEA361C44726336B0C17935BF90EAB4F73E37` |
| Commit de referencia | `7cc9b30` |
| Idioma de respuesta | Español (constante embebida) |
| Contexto inyectado | Nombre/ProjectKey del proyecto, conteos de objetos y selección VBE (≤32 KB) ⚠️ |
| Versionado | Sin control de versión en runtime — **brecha F5** |

## 3. Tools expuestas al modelo

Whitelist en `JsonRpcMethods.All`. Operaciones de escritura marcadas con ✍️ y destructivas con 🗑️.

| Grupo | Tools |
|-------|-------|
| VBE | `list_modules`, `read_module`, `write_module` ✍️, `create_module` ✍️, `delete_module` 🗑️, `get_selection`, `get_active_module` |
| Acceso (lectura) | `project_summary`, `list_objects`, `object_detail`, `list_commandbars`, `inspect_adp` |
| Acceso (escritura) | `create_form` ✍️, `create_table` ✍️, `create_query` ✍️, `execute_sql` 🗑️, `create_ribbon` ✍️, `migrate_commandbar_to_ribbon` 🗑️, `migrate_adp_to_accdb` 🗑️ |

## 4. Dependencias y runtime

| Componente | Versión | Notas |
|------------|---------|-------|
| .NET Framework | 4.8 (`net48`) | CPU-agnóstico vía `-p:PlatformTarget=x86\|x64` |
| Lenguaje | C# 10 | `Nullable=annotations` |
| `System.Text.Json` | 8.0.5 | Sin `System.Net.Http.Json` (restricción net48) |
| WinForms | Nativo | Marshaling explícito por `IUiScheduler` |
| DPAPI | `DataProtectionScope.CurrentUser` | Entropía fija ⚠️ |
| Servidores MCP | **Ninguno** | Eliminados en el diseño v1.1.0 |
| CLI de agentes externos | **Ninguno en runtime** | Uso solo en desarrollo |

## 5. Secretos

La API key se almacena cifrada con DPAPI (`BlsSecureStore`) y nunca se escribe en logs,
excepciones, JSON, registry ni disco en claro. `NIM_API_KEY` solo se usa en tests de integración.

## 6. Brechas conocidas

1. El modelo y el endpoint están duplicados en `AGENTS.md`, `.opencode/opencode.json`, `MEMORY.md`,
   `SystemPromptBuilder.cs` y documentación: requieren una única fuente de verdad (F5).
2. No hay verificación criptográfica del prompt en runtime (solo este hash de inventario).
3. Sin SBOM de dependencias NuGet generado automáticamente en CI.
