---
description: "Genera un plan detallado de todo lo que tocará un feature usando el agente plan."
agent: plan
# subtask: true   # Descomenta si quieres que se ejecute como subagente aislado.
---

# Plan de feature: $ARGUMENTS

Actúa como el agente **plan**. No modifiques ningún archivo. Tu única tarea es analizar y entregar un plan completo de implementación.

## Contexto del proyecto
- Archivos en la raíz:
  !`ls -la`

- Estado actual de Git:
  !`git status --short`

- Estructura resumida del proyecto:
  !`find . -maxdepth 2 -type f | head -100`

## Archivos clave de referencia
Incluye el contenido de los archivos más relevantes para entender la arquitectura actual. Puedes añadir o quitar según tu proyecto:

@package.json
@README.md
@src/index.ts

## Solicitud
El feature solicitado es: **$1**

Si se pasan más argumentos, úsalos como contexto adicional:
- Argumento 2: $2
- Argumento 3: $3
- Todos los argumentos: $ARGUMENTS

## Tarea
1. Analiza la solicitud y el código existente.
2. Identifica todo lo que el feature va a tocar.
3. Genera un plan en formato Markdown que incluya, como mínimo:
   - **Resumen del feature**: qué hace y por qué.
   - **Archivos a crear, modificar o eliminar**: lista completa con rutas.
   - **Cambios por archivo**: para cada archivo, qué funciones, clases, rutas, componentes o configuraciones se van a tocar.
   - **Dependencias**: nuevas librerías, versiones, cambios en `package.json` u otros gestores.
   - **Base de datos / migraciones**: si aplica, cambios en esquemas, migraciones o seeds.
   - **Tests**: pruebas unitarias, de integración o e2e que se deben añadir o modificar.
   - **Riesgos e impacto**: posibles efectos secundarios, compatibilidad, rendimiento, seguridad.
   - **Orden sugerido de implementación**: pasos recomendados para desarrollar el feature.
4. No escribas código final ni edites archivos. Solo entrega el plan.

## Formato de salida
Markdown claro y estructurado. Usa listas, tablas y bloques de código solo para mostrar rutas o ejemplos de estructura, no para implementar.