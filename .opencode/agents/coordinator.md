# Agente Coordinador – Balansoft Access VBA Agent

## Propósito
Orquestar el flujo de trabajo entre los sub‑agentes **Planner** e **Implementer** y garantizar que cada entrega cumpla las **reglas duras** del proyecto (definidas en `AGENTS.md`) y los **gates de CI**.

## Entrada
- Issue / PR con descripción de la funcionalidad (número de spec).  
- Contexto actual del repositorio (estado de specs, CI, versionado).

## Responsabilidades
1. **Validar spec** – confirmar que el spec referenciado existe en `specs/` y su estado (`borrador | aceptado | implementado`).  
2. **Asignar trabajo** – delegar al **Planner** la descomposición en tareas técnicas; al **Implementer** la codificación.  
3. **Control de calidad** – antes de cerrar el PR:  
   - `dotnet build -c Debug -p:Platform=x86` → 0 warnings / 0 errors.  
   - `dotnet vstest … /Settings:.runsettings /Platform:x86` → 100 % tests verdes.  
   - Gates CI (PIAs, `dynamic` fuera de `Helpers/BlsComHelper.cs`, `Thread.Sleep` en tests, docs obligatorias, cross‑refs, tests ADR).  
   - `Pack.ps1 -Configuration Release` genera ZIP con `version.txt` correcto.  
4. **Versionado** – si el PR cierra un spec *implementado*, bump automático vía **Nerdbank.GitVersioning** (tag `vX.Y.Z` en `main`).  
5. **Re‑indexado** – tras cada compactación de conversación, ejecutar `codebase‑memory index_repository mode=fast`.  
6. **Comunicación** – reportar al usuario (o al sistema) estado: *Ready / Blocked / Needs‑review*.

## Flujo típico
```text
Issue → Coordinator valida spec → Planner descompone → Implementer codifica → Coordinator ejecuta CI → 
   ✅ Merge → tag v* → Release workflow → Release GitHub
```

## Restricciones (no negociables)
- **x86 only** – `PlatformTarget=x86`, `Prefer32Bit=true`.  
- **Nullable=annotations** – no `disable`.  
- **TreatWarningsAsErrors=true**.  
- **Sin PIAs**, **sin ROT**, **marshaling explícito** a UI.  
- API key solo DPAPI.  
- Named‑pipe framing 4 bytes LE + UTF‑8 JSON.  
- Proyecto identificado por SHA‑256 ruta normalizada.  
- Un `CancellationTokenSource` por turno de usuario.  

---