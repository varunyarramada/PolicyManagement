# ── Stage 1: build & publish ──────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy .csproj files first so the restore layer is cached independently
# from source changes — dotnet restore only reruns when a .csproj changes.
COPY src/PolicyManagement.Domain/PolicyManagement.Domain.csproj                       src/PolicyManagement.Domain/
COPY src/PolicyManagement.Application/PolicyManagement.Application.csproj             src/PolicyManagement.Application/
COPY src/PolicyManagement.Infrastructure/PolicyManagement.Infrastructure.csproj       src/PolicyManagement.Infrastructure/
COPY src/PolicyManagement.API/PolicyManagement.API.csproj                             src/PolicyManagement.API/

RUN dotnet restore src/PolicyManagement.API/PolicyManagement.API.csproj

# Copy full source and publish in Release configuration
COPY src/ src/
RUN dotnet publish src/PolicyManagement.API/PolicyManagement.API.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create a non-root user (uid 1000 / gid 1000) for the container process.
# Running as root in a production container violates the principle of least privilege.
RUN groupadd --gid 1000 appgroup \
 && useradd --uid 1000 --gid 1000 --no-create-home --shell /bin/false appuser

COPY --from=build /app/publish .

# Switch to non-root user before the process starts
USER appuser

# Kestrel binds to port 8080 — the ASP.NET Core default for non-root containers.
ENV ASPNETCORE_URLS=http://+:8080
# Disable diagnostic sockets — not needed in production containers.
ENV DOTNET_EnableDiagnostics=0
# Default environment; overrideable at container runtime via -e.
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

# Liveness probe: if the process is alive and Kestrel is responding, this returns 200.
HEALTHCHECK --interval=30s --timeout=10s --start-period=15s --retries=3 \
    CMD curl --fail http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "PolicyManagement.API.dll"]
