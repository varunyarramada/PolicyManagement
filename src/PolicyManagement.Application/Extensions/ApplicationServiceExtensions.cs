using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PolicyManagement.Application.Behaviours;

namespace PolicyManagement.Application.Extensions;

/// <summary>
/// Extension methods for registering Application layer services into the DI container.
/// Called from <c>PolicyManagement.API/Program.cs</c> as part of the DI composition root.
/// </summary>
public static class ApplicationServiceExtensions
{
    /// <summary>
    /// Registers all Application layer services:
    /// <list type="bullet">
    ///   <item><description>MediatR with all handlers in this assembly.</description></item>
    ///   <item>
    ///     <description>
    ///       <see cref="ValidationPipelineBehavior{TRequest,TResponse}"/> — runs FluentValidation
    ///       before every handler.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       All <c>IValidator&lt;T&gt;</c> implementations discovered in this assembly via
    ///       FluentValidation's assembly scanner.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServiceExtensions).Assembly;

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }
}
