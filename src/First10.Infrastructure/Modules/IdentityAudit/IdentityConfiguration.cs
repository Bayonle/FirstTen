using First10.Infrastructure.Persistence;
using First10.Modules.IdentityAudit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace First10.Infrastructure.Modules.IdentityAudit;

public static class IdentityConfiguration
{
    public const string DispatcherPolicy = "Dispatcher";
    public const string AdministratorPolicy = "Administrator";
    public const string ClinicalApproverPolicy = "ClinicalApprover";

    public static IServiceCollection AddFirst10Identity(
        this IServiceCollection services,
        bool requireSecureCookies = true)
    {
        services.AddDataProtection().SetApplicationName("First10.Identity");
        services
            .AddIdentity<First10User, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequiredLength = 14;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Tokens.AuthenticatorTokenProvider = TokenOptions.DefaultAuthenticatorProvider;
            })
            .AddEntityFrameworkStores<First10DbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "First10.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict;
            options.Cookie.SecurePolicy = requireSecureCookies
                ? Microsoft.AspNetCore.Http.CookieSecurePolicy.Always
                : Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = false;
            options.LoginPath = "/api/auth/login";
            options.AccessDeniedPath = "/api/auth/forbidden";
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = Microsoft.AspNetCore.Http.StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(DispatcherPolicy, policy => RequireCurrentRole(policy, First10Roles.Dispatcher))
            .AddPolicy(AdministratorPolicy, policy => RequireCurrentRole(policy, First10Roles.Administrator))
            .AddPolicy(ClinicalApproverPolicy, policy => RequireCurrentRole(policy, First10Roles.ClinicalApprover));
        services.AddScoped<IAuthorizationHandler, CurrentSessionHandler>();
        services.AddScoped<IUserClaimsPrincipalFactory<First10User>, First10ClaimsPrincipalFactory>();
        services.AddScoped<InvitationService>();
        services.AddScoped<BootstrapService>();
        services.AddScoped<MfaRecoveryService>();

        return services;
    }

    private static void RequireCurrentRole(AuthorizationPolicyBuilder policy, string role) =>
        policy.RequireAuthenticatedUser().RequireRole(role).AddRequirements(new CurrentSessionRequirement());
}
