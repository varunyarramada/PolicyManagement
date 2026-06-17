using PolicyManagement.Application.Extensions;
using PolicyManagement.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// ---- Application layer ----
builder.Services.AddApplication();

// ---- Infrastructure layer ----
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name: "sql-server",
        tags: ["ready"]);

var app = builder.Build();

// ---- Migrate and seed on startup ----
await InfrastructureServiceExtensions.ApplyMigrationsAndSeedAsync(app.Services);

app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

// Exposed for WebApplicationFactory in integration tests
public partial class Program { }
