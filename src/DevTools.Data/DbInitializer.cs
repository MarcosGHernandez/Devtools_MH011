using Microsoft.EntityFrameworkCore;

namespace DevTools.Data;

public static class DbInitializer
{
    public static string ResolveDatabasePath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var dir = Path.GetDirectoryName(customPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            return Path.GetFullPath(customPath);
        }

        var localDir = Path.Combine(Directory.GetCurrentDirectory(), ".devtools");
        Directory.CreateDirectory(localDir);
        return Path.Combine(localDir, "devtools.db");
    }

    public static DevToolsDbContext CreateDbContext(string? customDbPath = null)
    {
        var dbPath = ResolveDatabasePath(customDbPath);
        var optionsBuilder = new DbContextOptionsBuilder<DevToolsDbContext>();
        optionsBuilder.UseSqlite($"Data Source={dbPath}");

        return new DevToolsDbContext(optionsBuilder.Options);
    }

    public static async Task InitializeAsync(DevToolsDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.EnsureCreatedAsync(cancellationToken);

        // Schema evolution for existing SQLite databases
        try
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "PlanningMessages" (
                    "Id" TEXT NOT NULL CONSTRAINT "PK_PlanningMessages" PRIMARY KEY,
                    "ProjectId" TEXT NOT NULL,
                    "Role" TEXT NOT NULL,
                    "Content" TEXT NOT NULL,
                    "SuggestedQuestionsJson" TEXT NULL,
                    "BlueprintJson" TEXT NULL,
                    "CreatedAt" TEXT NOT NULL,
                    CONSTRAINT "FK_PlanningMessages_Projects_ProjectId" FOREIGN KEY ("ProjectId") REFERENCES "Projects" ("Id") ON DELETE CASCADE
                );
            """, cancellationToken);

            await context.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS "IX_PlanningMessages_ProjectId" ON "PlanningMessages" ("ProjectId");
            """, cancellationToken);

            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN Description TEXT;", cancellationToken); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN ArchitecturalStyle TEXT;", cancellationToken); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN FrontendStack TEXT;", cancellationToken); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN DatabaseType TEXT;", cancellationToken); } catch { }
            try { await context.Database.ExecuteSqlRawAsync("ALTER TABLE Projects ADD COLUMN LatestBlueprintJson TEXT;", cancellationToken); } catch { }

            // Clean up any misplaced attendance messages that got attached to MyNewProject
            try
            {
                await context.Database.ExecuteSqlRawAsync("""
                    DELETE FROM "PlanningMessages" 
                    WHERE "ProjectId" = '3a7ef7c8-146b-44d0-abdf-a373effa3bfb' 
                      AND ("Content" LIKE '%asistencia%docente%' OR "Content" LIKE '%registro%asistencia%');
                """, cancellationToken);

                await context.Database.ExecuteSqlRawAsync("""
                    UPDATE "Projects" 
                    SET "Description" = 'SaaS de logística con Monolito Modular y PostgreSQL'
                    WHERE "Id" = '3a7ef7c8-146b-44d0-abdf-a373effa3bfb'
                      AND "Description" LIKE '%asistencia%';
                """, cancellationToken);
            }
            catch { }
        }
        catch
        {
            // Ignore if already present
        }
    }
}
