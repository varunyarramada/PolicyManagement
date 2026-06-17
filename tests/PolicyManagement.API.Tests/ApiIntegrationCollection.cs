using Xunit;

namespace PolicyManagement.API.Tests;

/// <summary>
/// xUnit test collection for all API integration tests.
/// Integration tests use <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/>
/// which re-runs the application entry point via <c>HostFactoryResolver</c>.
/// Concurrent factory startups can conflict when they share the same assembly.
/// Placing all integration tests in a single non-parallel collection ensures each factory
/// starts and stops sequentially, preventing "entry point exited" startup failures.
/// </summary>
[CollectionDefinition("ApiIntegration", DisableParallelization = true)]
public sealed class ApiIntegrationCollection;
