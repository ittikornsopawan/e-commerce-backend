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
using Ecommerce.Infrastructure.Persistence.Ef;
using Microsoft.EntityFrameworkCore;

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

        services.AddSingleton(jwt);
        services.AddSingleton(security);
        services.TryAddSingleton(TimeProvider.System);

        AddSecurity(services, security);
        AddPersistence(services, database);
        AddAuthenRepositories(services);
        AddAuthenSettings(services, security);
        return services;
    }

    private static void AddSecurity(IServiceCollection services, SecurityOptions security)
    {
        services.AddSingleton(new FieldProtector(security.DecodeFieldKey()));
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
    }

    private static void AddPersistence(IServiceCollection services, DatabaseOptions database)
    {
        // Every session runs in UTC (EcommerceDbContext.UtcSession): the schema stores UTC without a time zone.
        services.AddDbContext<EcommerceDbContext>(o =>
            EcommerceDbContext.Configure((DbContextOptionsBuilder<EcommerceDbContext>)o, database.ConnectionString, database.CommandTimeoutSeconds));
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
    }

    private static void AddAuthenRepositories(IServiceCollection services)
    {
        services.AddScoped<IRegistrationRepository, RegistrationRepository>();
        services.AddScoped<ILoginRepository, LoginRepository>();
        services.AddScoped<IPasswordResetRequestRepository, PasswordResetRequestRepository>();
        services.AddScoped<ICreateNewPasswordRepository, CreateNewPasswordRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ILogoutRepository, LogoutRepository>();
        services.AddScoped<ICallerCredentialRepository, CallerCredentialRepository>();
        services.AddScoped<IUserProfileRepository, UserProfileRepository>();
        services.AddScoped<IUserProfileQueryRepository, UserProfileQueryRepository>();
        services.AddScoped<IUserAddressQueryRepository, UserAddressQueryRepository>();
        services.AddScoped<IUserAddressRepository, UserAddressRepository>();
    }

    private static void AddAuthenSettings(IServiceCollection services, SecurityOptions security)
    {
        services.AddSingleton(new LoginSettings(TimeSpan.FromMinutes(security.LoginFailureWindowMinutes)));
        services.AddSingleton(new PasswordResetSettings(
            security.PasswordResetMaxRequests,
            TimeSpan.FromMinutes(security.PasswordResetWindowMinutes),
            TimeSpan.FromMinutes(security.PasswordResetMinutes),
            security.PasswordResetUrl));
        services.AddSingleton(new RefreshSettings(TimeSpan.FromSeconds(security.RefreshReuseGraceSeconds)));
        services.AddSingleton(new AddressSettings(security.MaxAddressesPerUser));
    }
}
