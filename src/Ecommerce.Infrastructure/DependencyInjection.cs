using Ecommerce.Application.Abstractions;
using Ecommerce.Domain.Authen.Repositories;
using Ecommerce.Infrastructure.Email;
using Ecommerce.Infrastructure.Options;
using Ecommerce.Infrastructure.Persistence;
using Ecommerce.Infrastructure.Persistence.Authen;
using Ecommerce.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Ecommerce.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence, security and email services. Throws at startup if configuration is missing or invalid.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var database = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
        var security = configuration.GetSection(SecurityOptions.Section).Get<SecurityOptions>() ?? new SecurityOptions();
        database.Validate();
        jwt.Validate();
        var fieldKey = security.DecodeFieldKey();

        services.AddSingleton(jwt);
        services.AddSingleton(security);
        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton(NpgsqlDataSource.Create(database.ConnectionString));
        services.AddScoped<DbSession>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddSingleton(new FieldProtector(fieldKey));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPasswordRepository, PasswordRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();

        return services;
    }
}
