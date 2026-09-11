# AI DevTools Toolkit

El **AI DevTools Toolkit** es un catálogo independiente, desacoplado y portable de **Prompts**, **Skills** y **Reglas de Calidad** diseñado para integrarse con cualquier motor de agentes de software (como la suite privada `DevTools`).

Este repositorio/directorio está diseñado para:
1. Ser **versionado independientemente** de la aplicación principal.
2. Poder ser clonado como un submódulo Git (`git submodule add <url> toolkit`) o paquete NuGet/NPM.
3. Permitir a equipos y desarrolladores personalizar y extender prompts y skills sin tocar el código fuente del orquestador.

## Estructura del Toolkit

- `toolkit.manifest.json`: Manifiesto principal que declara versión, autor, compatibilidad y registro de recursos.
- `prompts/`: Plantillas de prompts estructuradas por dominio con directivas de sistema, variables y formatos de salida.
  - `code-review/`: Auditoría de código, principios SOLID, seguridad y Clean Code.
  - `architecture/`: Generación de ADRs (Architecture Decision Records) y validación de arquitectura.
  - `testing/`: Pruebas unitarias, TDD y pruebas de mutación.
  - `refactoring/`: Modernización de código a C# 13 y patrones idiomáticos.
- `skills/`: Definiciones declarativas de herramientas (Tools/Plugins) con esquemas JSON Schema para llamadas a funciones (Tool Calling).
  - `roslyn-analyzer/`: Análisis sintáctico, semántico y diagnósticos de compilación.
  - `git-inspector/`: Análisis de diferencias (diffs), historial y ramas.
  - `test-runner/`: Ejecución de tests (`dotnet test`) y recolección de fallos.
