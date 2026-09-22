using System.Text;
using DevTools.Core.Models;

namespace DevTools.Orchestrator.Services;

public static class SpecKitDocumentationGenerator
{
    public static SpecKitSpec GenerateSpecKit(ProjectPlanBlueprint blueprint)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = blueprint.ProjectName,
            Description = blueprint.ProjectName,
            ArchitecturalStyle = blueprint.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = blueprint.TechStack.GetValueOrDefault("Frontend", "React + Minimalist Design System"),
            DatabaseType = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9")
        };
        return GenerateSpecKit(answers, blueprint);
    }

    public static string GeneratePrd(ProjectPlanBlueprint blueprint)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = blueprint.ProjectName,
            Description = blueprint.ProjectName,
            ArchitecturalStyle = blueprint.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = blueprint.TechStack.GetValueOrDefault("Frontend", "React + Minimalist Design System"),
            DatabaseType = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9")
        };
        return GeneratePrd(answers, blueprint);
    }

    public static string GenerateSuggestionsAndRoadmap(ProjectPlanBlueprint blueprint)
    {
        var answers = new ProjectInterviewAnswers
        {
            ProjectName = blueprint.ProjectName,
            Description = blueprint.ProjectName,
            ArchitecturalStyle = blueprint.ArchitecturalStyle ?? "Clean Architecture",
            FrontendStack = blueprint.TechStack.GetValueOrDefault("Frontend", "React + Minimalist Design System"),
            DatabaseType = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9")
        };
        return GenerateSuggestionsAndRoadmap(answers, blueprint);
    }

    public static SpecKitSpec GenerateSpecKit(ProjectInterviewAnswers answers, ProjectPlanBlueprint blueprint)
    {
        var projectName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? answers.ProjectName : blueprint.ProjectName;
        var safeName = projectName.Replace(" ", "").Replace("-", "_");
        var arch = blueprint.ArchitecturalStyle ?? answers.ArchitecturalStyle;
        var db = blueprint.TechStack.GetValueOrDefault("Database", answers.DatabaseType);
        var front = blueprint.TechStack.GetValueOrDefault("Frontend", answers.FrontendStack);

        return new SpecKitSpec
        {
            ConstitutionMarkdown = GenerateConstitution(projectName, arch, db, front, blueprint),
            SpecMarkdown = GenerateSpec(projectName, answers, blueprint),
            PlanMarkdown = GeneratePlan(projectName, arch, blueprint),
            TasksMarkdown = GenerateTasks(projectName, blueprint),
            Version = "1.0.0",
            CreatedAt = DateTime.UtcNow
        };
    }

    public static string GenerateConstitution(
        string projectName,
        string arch,
        string db,
        string front,
        ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Constitución del Proyecto: {projectName}");
        sb.AppendLine();
        sb.AppendLine("> **Estándar**: GitHub Spec Kit (Spec-Driven Development)");
        sb.AppendLine($"> **Fecha de Emisión**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine("> **Autoridad**: Arquitectura de Software & Tech Lead");
        sb.AppendLine();
        sb.AppendLine("## 1. Declaración de Principios Inmutables");
        sb.AppendLine();
        sb.AppendLine("Los siguientes principios rigen todo el ciclo de desarrollo asistido por agentes de inteligencia artificial y desarrolladores humanos:");
        sb.AppendLine();
        sb.AppendLine($"1. **Aislamiento de Dominio ({arch})**:");
        sb.AppendLine("   - La capa de Dominio (`Domain`) no debe tener ninguna dependencia hacia bibliotecas externas, frameworks de acceso a datos ni detalles de infraestructura.");
        sb.AppendLine("   - Las entidades deben proteger sus invariantes mediante encapsulación e inmutabilidad en sus identificadores.");
        sb.AppendLine();
        sb.AppendLine("2. **Especificación Previa al Código (Spec-Driven)**:");
        sb.AppendLine("   - Ninguna funcionalidad de negocio se implementa sin contar previamente con un requerimiento especificado en `spec.md` y una tarea registrada en `tasks.md`.");
        sb.AppendLine("   - Los contratos de entrada y salida deben definirse formalmente antes de generar código ejecutable.");
        sb.AppendLine();
        sb.AppendLine("3. **Verificación Automatizada Continua**:");
        sb.AppendLine("   - Todo cambio de código debe compilar con 0 advertencias y 0 errores (`dotnet build`).");
        sb.AppendLine("   - Las pruebas unitarias deben superar el 100% de éxito en cada entrega (`dotnet test --nologo`).");
        sb.AppendLine();
        sb.AppendLine("4. **Cero Tolerancia a Emojis e Informalismos**:");
        sb.AppendLine("   - Los archivos de código, mensajes de log, comentarios, documentación técnica y respuestas del sistema deben ser estrictamente técnicos y profesionales, sin emojis.");
        sb.AppendLine();
        sb.AppendLine("## 2. Compuertas de Decisión (Decision Gates)");
        sb.AppendLine();
        sb.AppendLine("| Compuerta | Criterio de Aprobación | Responsable |");
        sb.AppendLine("|---|---|---|");
        sb.AppendLine("| **Gate 1: Requerimientos** | PRD y `spec.md` revisados con criterios de aceptación Given/When/Then | Product Owner / Agente |");
        sb.AppendLine("| **Gate 2: Arquitectura** | ADR generado y alineado con la matriz técnica (.NET 9 + EF Core) | Arquitecto de Software |");
        sb.AppendLine("| **Gate 3: Implementación** | Cobertura de pruebas unitarias y validación con FluentValidation | Desarrollador / Agente |");
        sb.AppendLine("| **Gate 4: Seguridad & Auditoría** | Análisis estático sin vulnerabilidades críticas ni secretos en código | Auditor de Código |");
        sb.AppendLine();
        sb.AppendLine("## 3. Matriz Tecnológica Inmutable");
        sb.AppendLine();
        sb.AppendLine($"- **Estilo Arquitectónico**: {arch}");
        sb.AppendLine($"- **Plataforma Backend**: .NET 9 LTS / C# 13");
        sb.AppendLine($"- **Persistencia Principal**: {db}");
        sb.AppendLine($"- **Frontend / Presentación**: {front}");
        sb.AppendLine();
        sb.AppendLine("## 4. Convenciones Operativas");
        sb.AppendLine();
        if (blueprint.KeyConventions.Count > 0)
        {
            foreach (var conv in blueprint.KeyConventions)
            {
                sb.AppendLine($"- {conv}");
            }
        }
        else
        {
            sb.AppendLine("- Clean Code con nombres intencionales y expresivos.");
            sb.AppendLine("- Uso estricto de tipos de referencia anulables (`#nullable enable`).");
            sb.AppendLine("- File-scoped namespaces en todos los archivos C#.");
            sb.AppendLine("- Métodos asíncronos con propagación de `CancellationToken`.");
        }

        return sb.ToString();
    }

    public static string GenerateSpec(
        string projectName,
        ProjectInterviewAnswers answers,
        ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Especificación Técnica Formal: {projectName}");
        sb.AppendLine();
        sb.AppendLine($"> **Versión**: 1.0.0 | **Estado**: Aprobado | **Fecha**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## 1. Resumen Ejecutivo");
        sb.AppendLine();
        sb.AppendLine(blueprint.ExecutiveSummary ?? answers.Description);
        sb.AppendLine();
        sb.AppendLine("## 2. Actores y Roles del Sistema");
        sb.AppendLine();
        sb.AppendLine("- **Usuario Final**: Interactúa con la aplicación para ejecutar operaciones del dominio.");
        sb.AppendLine("- **Administrador del Sistema**: Gestiona configuraciones, auditoría y catálogos globales.");
        sb.AppendLine("- **Servicio Automatizado / Worker**: Procesa eventos en segundo plano y sincroniza tareas.");
        sb.AppendLine();
        sb.AppendLine("## 3. Requerimientos Funcionales (FR)");
        sb.AppendLine();
        sb.AppendLine("### REQ-FR-001: Gestión de Entidades Principales");
        sb.AppendLine("- **Descripción**: El sistema debe proveer operaciones seguras de creación, consulta, actualización y desactivación lógica de las entidades del dominio.");
        sb.AppendLine("- **Entradas**: DTO validado con FluentValidation.");
        sb.AppendLine("- **Salidas**: Identificador único (GUID) y estado resultante.");
        sb.AppendLine("- **Criterio de Aceptación**: Si los datos requeridos no cumplen con las reglas de negocio, se retorna un error 400 Bad Request con detalles legibles.");
        sb.AppendLine();
        sb.AppendLine("### REQ-FR-002: Consulta y Paginación Optimizada");
        sb.AppendLine("- **Descripción**: Las consultas deben soportar filtrado por criterios clave, ordenamiento y paginación con tamaño de página predeterminado.");
        sb.AppendLine("- **Entradas**: Parámetros de consulta (`page`, `pageSize`, `filter`, `sortBy`).");
        sb.AppendLine("- **Salidas**: Lista paginada con metadatos (`totalRecords`, `totalPages`, `currentPage`).");
        sb.AppendLine();
        sb.AppendLine("### REQ-FR-003: Auditoría y Trazabilidad");
        sb.AppendLine("- **Descripción**: Cada mutación en las entidades principales debe registrar marca de tiempo UTC y usuario que originó el cambio.");
        sb.AppendLine();
        sb.AppendLine("## 4. Requerimientos No Funcionales (NFR)");
        sb.AppendLine();
        sb.AppendLine("| ID | Categoría | Métrica Objetivo | Criterio de Medición |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine("| **NFR-001** | Rendimiento | Latencia p95 < 200ms en lecturas | Benchmark en ambiente de pruebas |");
        sb.AppendLine("| **NFR-002** | Confiabilidad | Disponibilidad >= 99.9% | Health checks (`/health`) activos |");
        sb.AppendLine("| **NFR-003** | Mantenibilidad | Índice de Mantenibilidad >= 80% | Auditoría continua ISO/IEC 25010 |");
        sb.AppendLine("| **NFR-004** | Seguridad | OWASP Top 10 mitigado | Validación de entradas y sanitización |");
        sb.AppendLine();
        sb.AppendLine("## 5. Invariantes del Dominio");
        sb.AppendLine();
        sb.AppendLine("1. Todo identificador de entidad debe ser un GUID inmutable no vacío.");
        sb.AppendLine("2. Las fechas de creación y actualización deben ser siempre en formato UTC.");
        sb.AppendLine("3. Ningún registro persistido debe contener referencias a cadenas nulas cuando el campo sea requerido.");

        return sb.ToString();
    }

    public static string GeneratePlan(
        string projectName,
        string arch,
        ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan Técnico de Arquitectura: {projectName}");
        sb.AppendLine();
        sb.AppendLine($"> **Estilo**: {arch} | **LTS**: .NET 9 | **Fecha**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## 1. Justificación Arquitectónica");
        sb.AppendLine();
        sb.AppendLine(blueprint.ArchitecturalRationale ?? $"Implementación basada en {arch} para garantizar desacoplamiento y alta capacidad de prueba.");
        sb.AppendLine();
        sb.AppendLine("## 2. Mapa de Capas y Dependencias");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("     [ Presentation / API ]");
        sb.AppendLine("              │");
        sb.AppendLine("              ▼");
        sb.AppendLine("     [ Application (Use Cases & DTOs) ]");
        sb.AppendLine("         │              │");
        sb.AppendLine("         ▼              ▼");
        sb.AppendLine("     [ Domain ] ◄─── [ Infrastructure (EF Core & Adapters) ]");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("1. **Domain**: Entidades núcleo, invariantes y contratos de repositorio.");
        sb.AppendLine("2. **Application**: Casos de uso, DTOs, validaciones y mapeos.");
        sb.AppendLine("3. **Infrastructure**: Implementaciones concretas de DbContext, conectores a bases de datos y servicios externos.");
        sb.AppendLine("4. **Presentation / Web API**: Endpoints HTTP mínimos, configuración de inyección de dependencias y middlewares.");
        sb.AppendLine();
        sb.AppendLine("## 3. Diagrama de Componentes C4");
        sb.AppendLine();
        sb.AppendLine("```mermaid");
        sb.AppendLine(blueprint.C4DiagramMermaid ?? "graph TD; Client-->Api; Api-->Database;");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 4. Estrategia de Persistencia");
        sb.AppendLine();
        var db = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9");
        sb.AppendLine($"- **Motor**: {db}");
        sb.AppendLine("- **Patrón**: Unit of Work y Repository Pattern desacoplado.");
        sb.AppendLine("- **Migraciones**: Entity Framework Core Code-First con soporte para rollback.");

        return sb.ToString();
    }

    public static string GenerateTasks(string projectName, ProjectPlanBlueprint blueprint)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Plan de Tareas de Implementación: {projectName}");
        sb.AppendLine();
        sb.AppendLine($"> **Total Fases**: 5 | **Metodología**: Spec-Driven Development (SDD)");
        sb.AppendLine();
        sb.AppendLine("## Fase 1: Fundamentos y Capa de Dominio");
        sb.AppendLine("- [ ] **TASK-001**: Crear solución .NET 9 y proyectos por capas (`Domain`, `Application`, `Infrastructure`, `Api`, `UnitTests`).");
        sb.AppendLine("- [ ] **TASK-002**: Definir abstracción `BaseEntity` con propiedades `Id`, `CreatedAt` y `UpdatedAt`.");
        sb.AppendLine("- [ ] **TASK-003**: Implementar entidades agregadas del dominio con constructores protegidos y métodos mutadores validados.");
        sb.AppendLine("- [ ] **TASK-004**: Definir interfaces de repositorio de dominio en `Domain/Interfaces/`.");
        sb.AppendLine();
        sb.AppendLine("## Fase 2: Capa de Aplicación");
        sb.AppendLine("- [ ] **TASK-005**: Crear DTOs de entrada y salida con tipado estricto.");
        sb.AppendLine("- [ ] **TASK-006**: Implementar validadores FluentValidation para solicitudes de creación y actualización.");
        sb.AppendLine("- [ ] **TASK-007**: Implementar servicios de aplicación o handlers de caso de uso con manejo de transacciones.");
        sb.AppendLine();
        sb.AppendLine("## Fase 3: Capa de Infraestructura");
        sb.AppendLine("- [ ] **TASK-008**: Configurar `ApplicationDbContext` con asignación de tipos y llaves primarias en Fluent API.");
        sb.AppendLine("- [ ] **TASK-009**: Implementar repositorios concretos con consultas asíncronas no bloqueantes (`AsNoTracking`).");
        sb.AppendLine("- [ ] **TASK-010**: Configurar migraciones iniciales y script de siembra de datos de prueba.");
        sb.AppendLine();
        sb.AppendLine("## Fase 4: Capa de Presentación y Web API");
        sb.AppendLine("- [ ] **TASK-011**: Configurar `Program.cs` con inyección de dependencias, OpenAPI / Swagger y middlewares de manejo global de excepciones.");
        sb.AppendLine("- [ ] **TASK-012**: Exponer endpoints RESTful con códigos de estado HTTP correctos (200, 201, 400, 404, 500).");
        sb.AppendLine("- [ ] **TASK-013**: Configurar Health Checks (`/health`) para monitoreo de base de datos y memoria.");
        sb.AppendLine();
        sb.AppendLine("## Fase 5: Aseguramiento de Calidad y Pruebas");
        sb.AppendLine("- [ ] **TASK-014**: Desarrollar pruebas unitarias de dominio y aplicación con `xUnit` y `FluentAssertions`.");
        sb.AppendLine("- [ ] **TASK-015**: Ejecutar pruebas de arquitectura para validar que `Domain` no dependa de `Infrastructure`.");
        sb.AppendLine("- [ ] **TASK-016**: Ejecutar auditoría continua de código y validar compilación limpia.");

        return sb.ToString();
    }

    public static string GeneratePrd(ProjectInterviewAnswers answers, ProjectPlanBlueprint blueprint)
    {
        var projectName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? answers.ProjectName : blueprint.ProjectName;
        var sb = new StringBuilder();
        sb.AppendLine($"# Documento de Requerimientos de Producto (PRD)");
        sb.AppendLine();
        sb.AppendLine($"## Producto: {projectName}");
        sb.AppendLine();
        sb.AppendLine($"- **Versión del Documento**: 1.0.0");
        sb.AppendLine($"- **Fecha de Aprobación**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"- **Product Owner / Lead**: AI Software Architect & System Planner");
        sb.AppendLine($"- **Audiencia Objetivo**: {answers.TargetUsers}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("## 1. Declaración del Problema y Visión");
        sb.AppendLine();
        sb.AppendLine("### 1.1 El Problema");
        sb.AppendLine(answers.Description);
        sb.AppendLine();
        sb.AppendLine("### 1.2 La Solución Propuesta");
        sb.AppendLine(blueprint.ExecutiveSummary ?? $"Un sistema empresarial moderno y escalable diseñado para resolver los desafíos operativos mediante una arquitectura limpia en .NET 9.");
        sb.AppendLine();
        sb.AppendLine("### 1.3 Propuesta de Valor");
        sb.AppendLine("- Alta mantenibilidad con costos reducidos de evolución técnica.");
        sb.AppendLine("- Desacoplamiento total entre lógica de negocio y detalles de infraestructura.");
        sb.AppendLine("- Confiabilidad empresarial respaldada por pruebas automatizadas y contratos formales.");
        sb.AppendLine();
        sb.AppendLine("## 2. Personas y Usuarios Clave");
        sb.AppendLine();
        sb.AppendLine("### Persona 1: Usuario Operativo (Primary User)");
        sb.AppendLine("- **Rol**: Empleado o cliente final que ejecuta transacciones cotidianas.");
        sb.AppendLine("- **Necesidad**: Interfaz intuitiva, respuesta instantánea y retroalimentación clara de errores.");
        sb.AppendLine("- **Frustraciones**: Sistemas lentos, interfaces saturadas y caídas del servicio.");
        sb.AppendLine();
        sb.AppendLine("### Persona 2: Administrador Técnico (System Admin)");
        sb.AppendLine("- **Rol**: Responsable de configuración, auditoría de seguridad y monitoreo.");
        sb.AppendLine("- **Necesidad**: Trazabilidad completa de operaciones, logs estructurados y métricas de salud del sistema.");
        sb.AppendLine();
        sb.AppendLine("## 3. User Journeys (Flujos de Usuario)");
        sb.AppendLine();
        sb.AppendLine("### Journey 1: Creación y Gestión de Registro");
        sb.AppendLine("1. El usuario accede al módulo principal y completa los datos requeridos.");
        sb.AppendLine("2. El sistema valida las entradas en el cliente y envía la solicitud.");
        sb.AppendLine("3. El backend ejecuta la validación de negocio y persiste los cambios de forma atómica.");
        sb.AppendLine("4. El sistema notifica la confirmación con el identificador generado.");
        sb.AppendLine();
        sb.AppendLine("### Journey 2: Auditoría y Recuperación de Historial");
        sb.AppendLine("1. El administrador consulta el registro filtrando por fecha o palabra clave.");
        sb.AppendLine("2. El sistema recupera la vista optimizada sin bloqueos en base de datos.");
        sb.AppendLine("3. Se muestra el registro completo con sus marcas de tiempo y auditoría.");
        sb.AppendLine();
        sb.AppendLine("## 4. Requerimientos Funcionales Detallados");
        sb.AppendLine();
        sb.AppendLine("### Épica 1: Núcleo de Operaciones de Negocio");
        sb.AppendLine("- **FR-01**: El sistema debe proveer endpoints para creación, lectura, actualización y eliminación lógica de registros.");
        sb.AppendLine("- **FR-02**: Todas las operaciones de mutación deben estar protegidas contra transacciones duplicadas mediante identificadores idempotentes.");
        sb.AppendLine("- **FR-03**: La capa de presentación debe ofrecer validaciones inmediatas al usuario.");
        sb.AppendLine();
        sb.AppendLine("### Épica 2: Configuración y Seguridad");
        sb.AppendLine("- **FR-04**: Autenticación y autorización basada en roles (RBAC) con tokens seguros.");
        sb.AppendLine("- **FR-05**: Auditoría de cambios con usuario, acción, entidad y fecha UTC.");
        sb.AppendLine();
        sb.AppendLine("## 5. Requerimientos No Funcionales y SLAs");
        sb.AppendLine();
        sb.AppendLine("- **Rendimiento**: Tiempo de respuesta en endpoints de consulta menor a 200 ms (p95).");
        sb.AppendLine("- **Carga Esperada**: " + answers.ExpectedLoad);
        sb.AppendLine("- **Disponibilidad**: 99.9% de uptime mensual.");
        sb.AppendLine("- **Seguridad**: Sanitización de entradas contra inyecciones SQL y XSS. Almacenamiento seguro de secretos sin credenciales en código.");
        sb.AppendLine();
        sb.AppendLine("## 6. Criterios de Aceptación y Definición de Terminado (DoD)");
        sb.AppendLine();
        sb.AppendLine("1. **Pruebas de Unidad**: Cobertura comprobada con xUnit y FluentAssertions.");
        sb.AppendLine("2. **Compilación Limpia**: Cero advertencias y cero errores en compilación .NET 9.");
        sb.AppendLine("3. **Documentación Actualizada**: PRD, ADRs y contratos de API sincronizados.");
        sb.AppendLine();
        sb.AppendLine("## 7. Métricas de Éxito (KPIs)");
        sb.AppendLine();
        sb.AppendLine("1. **Tasa de Error HTTP 5xx**: < 0.05% del total de solicitudes.");
        sb.AppendLine("2. **Cobertura de Pruebas**: >= 80% en lógica de aplicación y dominio.");
        sb.AppendLine("3. **Tiempo Medio de Recuperación (MTTR)**: < 15 minutos ante incidentes.");
        sb.AppendLine("4. **Satisfacción del Usuario**: Net Promoter Score (NPS) >= 60.");

        return sb.ToString();
    }

    public static string GenerateSuggestionsAndRoadmap(ProjectInterviewAnswers answers, ProjectPlanBlueprint blueprint)
    {
        var projectName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? answers.ProjectName : blueprint.ProjectName;
        var sb = new StringBuilder();
        sb.AppendLine($"# Sugerencias Arquitectónicas y Hoja de Ruta: {projectName}");
        sb.AppendLine();
        sb.AppendLine($"> **Evaluación Técnica**: ISO/IEC 25010 | **Fecha**: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine();
        sb.AppendLine("## 1. Sugerencias Arquitectónicas de Alto Impacto");
        sb.AppendLine();
        sb.AppendLine("### SUG-001: Implementación del Patrón Outbox Transaccional");
        sb.AppendLine("- **Categoría**: Confiabilidad / Event-Driven");
        sb.AppendLine("- **Impacto**: Alto");
        sb.AppendLine("- **Justificación**: Al publicar eventos o mensajes hacia brokers (como Kafka o RabbitMQ), la persistencia en base de datos y el envío del evento deben ser atómicos para evitar estados inconsistentes.");
        sb.AppendLine("- **Recomendación**: Guardar los eventos en una tabla `OutboxMessages` en la misma transacción de la base de datos y utilizar un `BackgroundService` en .NET 9 para procesarlos con reintentos exponenciales.");
        sb.AppendLine();
        sb.AppendLine("### SUG-002: Estrategia de Caché Multinivel (L1 In-Memory + L2 Redis)");
        sb.AppendLine("- **Categoría**: Rendimiento");
        sb.AppendLine("- **Impacto**: Medio");
        sb.AppendLine("- **Justificación**: Para catálogos o consultas de alta lectura y baja mutación, una caché L1 local reduce la latencia a menos de 5 ms, mientras que Redis mantiene la sincronización en arquitecturas con múltiples instancias.");
        sb.AppendLine();
        sb.AppendLine("### SUG-003: Observabilidad Distribuida con OpenTelemetry");
        sb.AppendLine("- **Categoría**: Mantenibilidad / Operabilidad");
        sb.AppendLine("- **Impacto**: Alto");
        sb.AppendLine("- **Justificación**: Incorporar trazas distribuidas, métricas de runtime de .NET 9 y logs estructurados en formato OpenTelemetry estándar permite detectar cuellos de botella con precisión milimétrica.");
        sb.AppendLine();
        sb.AppendLine("### SUG-004: Llaves de Idempotencia para Solicitudes POST");
        sb.AppendLine("- **Categoría**: Integridad de Datos");
        sb.AppendLine("- **Impacto**: Crítico");
        sb.AppendLine("- **Justificación**: Evita duplicación de cobros o registros ante reintentos automáticos de clientes con conexiones inestables.");
        sb.AppendLine();
        sb.AppendLine("## 2. Hoja de Ruta de Desarrollo (Roadmap por Fases)");
        sb.AppendLine();
        sb.AppendLine("| Fase | Objetivo Principal | Entregables Clave | Duración Estimada |");
        sb.AppendLine("|---|---|---|---|");
        sb.AppendLine("| **Fase 1: MVP Core** | Dominio y Persistencia Básica | Entidades, DbContext, Migraciones, CRUD API básico | Sprint 1-2 |");
        sb.AppendLine("| **Fase 2: Seguridad & Validaciones** | Autenticación y Reglas de Negocio | JWT/OAuth2, FluentValidation, Middlewares | Sprint 3 |");
        sb.AppendLine("| **Fase 3: Optimización & Caché** | Rendimiento y Resiliencia | Redis, Polly Circuit Breaker, Health Checks | Sprint 4 |");
        sb.AppendLine("| **Fase 4: Observabilidad & CI/CD** | Preparación para Producción | OpenTelemetry, Docker Compose, GitHub Actions | Sprint 5 |");

        return sb.ToString();
    }

    public static string GenerateAdvancedAgentsMarkdown(ProjectPlanBlueprint blueprint)
    {
        var safeName = string.IsNullOrWhiteSpace(blueprint.ProjectName) ? "MyApp" : blueprint.ProjectName.Replace(" ", "");
        var arch = blueprint.ArchitecturalStyle ?? "Clean Architecture";
        var db = blueprint.TechStack.GetValueOrDefault("Database", "PostgreSQL + EF Core 9");

        var sb = new StringBuilder();
        sb.AppendLine($"# AGENTS.md: Directivas de Desarrollo para Agentes de IA");
        sb.AppendLine();
        sb.AppendLine($"## Proyecto: {safeName}");
        sb.AppendLine();
        sb.AppendLine("> **Agentes Soportados**: Google Antigravity, Cursor, GitHub Copilot, Claude Code, Cline.");
        sb.AppendLine($"> **Arquitectura**: {arch} | **Plataforma**: .NET 9 LTS");
        sb.AppendLine();
        sb.AppendLine("## 1. Reglas Absolutas (Inviolables)");
        sb.AppendLine();
        sb.AppendLine("1. **CERO EMOJIS**: Está terminantemente prohibido incluir emojis en código fuente, comentarios, pruebas, documentación o respuestas.");
        sb.AppendLine("2. **Aislamiento Estricto de Capas**:");
        sb.AppendLine("   - `src/{safeName}.Domain` NUNCA debe referenciar a `Infrastructure`, `Api` o paquetes NuGet externos.");
        sb.AppendLine("   - `src/{safeName}.Application` únicamente depende de `Domain`.");
        sb.AppendLine("   - `src/{safeName}.Infrastructure` implementa las interfaces definidas en `Application` o `Domain`.");
        sb.AppendLine("   - `src/{safeName}.Api` es el punto de entrada y ensamblaje de dependencias.");
        sb.AppendLine("3. **Compilación y Pruebas Limpias**:");
        sb.AppendLine("   - Antes de dar por terminada una tarea, ejecuta `dotnet build` y `dotnet test --nologo`.");
        sb.AppendLine("   - Cero advertencias (`TreatWarningsAsErrors` activo).");
        sb.AppendLine();
        sb.AppendLine("## 2. Comandos Clave para Agentes");
        sb.AppendLine();
        sb.AppendLine("```bash");
        sb.AppendLine("# Compilar toda la solución");
        sb.AppendLine("dotnet build --nologo");
        sb.AppendLine();
        sb.AppendLine("# Ejecutar pruebas unitarias");
        sb.AppendLine("dotnet test --nologo");
        sb.AppendLine();
        sb.AppendLine("# Iniciar servicios de infraestructura en Docker");
        sb.AppendLine("docker compose up -d");
        sb.AppendLine();
        sb.AppendLine("# Ejecutar la API en desarrollo");
        sb.AppendLine($"dotnet run --project src/{safeName}.Api/{safeName}.Api.csproj");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## 3. Estándares de Código C# 13");
        sb.AppendLine();
        sb.AppendLine("- Usa `record` para DTOs, comandos y respuestas inmutables.");
        sb.AppendLine("- Usa `sealed class` por defecto a menos que se diseñe explícitamente para herencia.");
        sb.AppendLine("- Usa file-scoped namespaces (`namespace MiProyecto.Capa;`).");
        sb.AppendLine("- Habilita `#nullable enable` en todos los archivos.");
        sb.AppendLine("- Inyecta `CancellationToken` en todas las operaciones asíncronas.");

        return sb.ToString();
    }
}
