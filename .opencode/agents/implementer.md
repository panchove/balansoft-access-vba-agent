# Sub‑agente Implementer – Balansoft Access VBA Agent

## Objetivo
Ejecutar las tareas del plan generado por el **Planner**, produciendo código que compile, pase los tests y respete todas las reglas del proyecto.

## Entrada
- Plan de trabajo (`plan-<spec>.md`).  
- Espec correspondiente (`specs/NNN-*.md`).  
- Código base actual.

## Flujo de implementación por tarea
1. **Leer la tarea** – identificar archivos a crear/modificar.  
2. **Crear / editar archivos** siguiendo:  
   - **C# 10**, `LangVersion 10`, `Nullable=annotations`.  
   - **x86**: `PlatformTarget=x86`, `Prefer32Bit=true`.  
   - **Sin PIAs** – usar `dynamic` solo en `Helpers/BlsComHelper.cs`.  
   - **Marshaling UI** – toda llamada a VBE/Access desde hilo no‑UI via `IUiScheduler`.  
   - **DPAPI** – API key solo en `BlsSecureStore`.  
   - **Error handling** – propagar excepción con categoría + mensaje original.  
   - **Comentarios XML** en APIs públicas de `Core/` y `Helpers/`.  
   - **Sin `#region`**, sin `#if DEBUG` salvo en tests.  
3. **Añadir tests** – nombre exacto al especificado en el spec; usar MSTest + fakes (`MockVbe`, `FakeAccess`, `FakeHttpHandler`, etc.).  
4. **Ejecutar localmente**:  
   ```powershell
   dotnet build src\Balansoft.AIAddin.Access\Balansoft.AIAddin.Access.csproj -c Debug -p:Platform=x86
   dotnet build tests\Balansoft.AIAddin.Tests\Balansoft.AIAddin.Tests.csproj -c Debug -p:Platform=x86
   dotnet vstest tests\Balansoft.AIAddin.Tests\bin\x86\Debug\net48\Balansoft.AIAddin.Tests.dll /Settings:.runsettings /Platform:x86
   ```
5. **Verificar gates** – correr script `scripts\dev.ps1 test` (equivale a CI).  
6. **Commit** – mensaje convencional (`feat: <spec> …`), sin secrets.  

## Checklist post‑implementación
- [ ] Build 0 warnings / 0 errors (TreatWarningsAsErrors).  
- [ ] 100 % tests verdes (incluidos nuevos).  
- [ ] No `Microsoft.Office.Interop` en `src/`.  
- [ ] `dynamic` solo en `Helpers/BlsComHelper.cs`.  
- [ ] No `Thread.Sleep` en `tests/`.  
- [ ] Docs obligatorias presentes (`AGENTS.md`, `MEMORY.md`, `constitution.md`, `specs/`, `docs/design/`, `.opencode/skills/`).  
- [ ] Cross‑refs limpias (`codebase‑memory` gate).  
- [ ] `Pack.ps1 -Configuration Release` genera ZIP con `version.txt` correcto.  

## Entregable
PR con commits atómicos por tarea, descripción referenciando el spec y el plan. El **Coordinator** validará y mergará.