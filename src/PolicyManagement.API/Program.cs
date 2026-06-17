using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using PolicyManagement.API.Configuration;
using PolicyManagement.API.Middleware;
using PolicyManagement.API.Services;
using PolicyManagement.Application.Extensions;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Infrastructure.Extensions;
using Serilog;

// ---- Serilog bootstrap logger (captures startup errors) ----
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // ---- Serilog ----
    builder.Host.UseSerilog((ctx, services, lc) => lc
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .WriteTo.Console());

    // ---- JWT Options (validated at startup — missing config fails before first request) ----
    builder.Services.AddOptions<JwtOptions>()
        .BindConfiguration(JwtOptions.SectionName)
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ---- Authentication (JWT Bearer / Keycloak) ----
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(opts =>
        {
            var jwt = builder.Configuration
                .GetSection(JwtOptions.SectionName)
                .Get<JwtOptions>() ?? new JwtOptions();

            opts.Authority            = jwt.Authority;
            opts.Audience             = jwt.Audience;
            opts.RequireHttpsMetadata = jwt.RequireHttpsMetadata;

            // Return ProblemDetails on 401 (missing / invalid token) ----
            opts.Events = new JwtBearerEvents
            {
                OnChallenge = async ctx =>
                {
                    ctx.HandleResponse();
                    ctx.Response.StatusCode  = StatusCodes.Status401Unauthorized;
                    ctx.Response.ContentType = "application/problem+json";

                    var problem = new ProblemDetails
                    {
                        Status   = StatusCodes.Status401Unauthorized,
                        Title    = "Unauthorized",
                        Detail   = "A valid JWT Bearer token is required.",
                        Instance = ctx.Request.Path,
                    };

                    problem.Extensions["correlationId"] =
                        ctx.HttpContext.Items["CorrelationId"]?.ToString() ?? string.Empty;

                    await ctx.Response.WriteAsJsonAsync(problem, ctx.HttpContext.RequestAborted);
                },

                // Return ProblemDetails on 403 (authenticated but insufficient role) ----
                OnForbidden = async ctx =>
                {
                    ctx.Response.StatusCode  = StatusCodes.Status403Forbidden;
                    ctx.Response.ContentType = "application/problem+json";

                    var problem = new ProblemDetails
                    {
                        Status   = StatusCodes.Status403Forbidden,
                        Title    = "Forbidden",
                        Detail   = "You do not have permission to perform this action.",
                        Instance = ctx.Request.Path,
                    };

                    problem.Extensions["correlationId"] =
                        ctx.HttpContext.Items["CorrelationId"]?.ToString() ?? string.Empty;

                    await ctx.Response.WriteAsJsonAsync(problem, ctx.HttpContext.RequestAborted);
                }
            };
        });

    // ---- Authorization ----
    builder.Services.AddAuthorizationBuilder()
        .AddPolicy("PolicyWrite", policy =>
            policy.RequireRole("Policy.Write"));

    // ---- Application layer ----
    builder.Services.AddApplication();

    // ---- Infrastructure layer ----
    builder.Services.AddInfrastructure(builder.Configuration);

    // ---- API ----
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
    builder.Services.AddControllers();

    // ---- Health checks ----
    builder.Services.AddHealthChecks()
        .AddSqlServer(
            builder.Configuration.GetConnectionString("DefaultConnection")!,
            name: "sql-server",
            tags: ["ready"]);

    // ---- OpenAPI / Swagger (development only) ----
    builder.Services.AddOpenApi();

    var app = builder.Build();

    // ---- Migrate and seed on startup ----
    await InfrastructureServiceExtensions.ApplyMigrationsAndSeedAsync(app.Services);

    // ============================================================
    // Middleware pipeline — ORDER IS CRITICAL
    // 1. CorrelationId   — populates CorrelationId before any logging
    // 2. GlobalException — wraps ALL exceptions (including 401/403) as ProblemDetails
    // 3. Authentication  — validates JWT, populates HttpContext.User
    // 4. Authorization   — enforces [Authorize] policies
    // 5. Controllers     — routes to PoliciesController
    // 6. HealthChecks    — infrastructure endpoints (no auth required)
    // ============================================================
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseMiddleware<GlobalExceptionMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    if (app.Environment.IsDevelopment())
        app.MapOpenApi();

    app.MapControllers();

    // Health check endpoints must NOT require authentication — they are for container
    // orchestration (liveness/readiness probes) and must be reachable without a token.
    app.MapHealthChecks("/health/live");
    app.MapHealthChecks("/health/ready");

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed for WebApplicationFactory in integration tests
public partial class Program { }

