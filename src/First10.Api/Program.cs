using System.Reflection;
using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Guidance;
using First10.Infrastructure.Modules.Recognition;
using First10.Infrastructure.Modules.Operations;
using First10.Api.Auth;
using First10.Api.Webhooks;
using First10.Api.Endpoints.Dispatch;
using First10.Api.Endpoints.Guidance;
using First10.Api.Endpoints.Incidents;
using First10.Api.Endpoints.Media;
using First10.Api.Endpoints.Operations;
using First10.Api.Endpoints.Recognition;
using Wolverine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
var requireSecureCookies = !builder.Environment.IsDevelopment();
var requireWrappedKeys = !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing");
builder.Services.AddFirst10Identity(requireSecureCookies, builder.Configuration, requireWrappedKeys);
builder.Services.AddFirst10Intake(builder.Configuration, requireWrappedKeys);
builder.Services.AddFirst10Incidents();
builder.Services.AddFirst10DispatchAndGuidance();
builder.Services.AddFirst10Recognition();
builder.Services.AddFirst10Operations();
builder.Services.AddFirst10ReadinessChecks();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "First10.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = requireSecureCookies
        ? CookieSecurePolicy.Always
        : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSignalR();
builder.Services.AddProblemDetails();
var developmentOrigins = builder.Configuration.GetSection("Cors:DevelopmentOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "https://localhost:5173"];
builder.Services.AddCors(options => options.AddPolicy("DevelopmentConsole", policy =>
    policy.WithOrigins(developmentOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Host.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    databaseConnection,
    First10RuntimeRole.Api,
    typeof(Program).Assembly,
    builder.Configuration.GetValue("Infrastructure:AutoProvision", builder.Environment.IsDevelopment())));
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["First10Session"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Cookie,
                Name = "First10.Session",
                Description = "MFA-backed First10 operator session cookie."
            },
            ["First10Antiforgery"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = "X-CSRF-TOKEN",
                Description = "Token returned by GET /api/auth/antiforgery for state-changing requests."
            }
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var requirement = new OpenApiSecurityRequirement();
        if (metadata.OfType<IAuthorizeData>().Any())
        {
            requirement[new OpenApiSecuritySchemeReference("First10Session", context.Document)] = [];
        }

        if (metadata.OfType<RequireAntiforgeryTokenAttribute>().Any())
        {
            requirement[new OpenApiSecuritySchemeReference("First10Antiforgery", context.Document)] = [];
        }

        if (requirement.Count > 0)
        {
            operation.Security ??= [];
            operation.Security.Add(requirement);
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();
string[] productionTopology = ["api", "worker"];

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentConsole");
}

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy =
        "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
        "img-src 'self' data:; media-src 'self'; connect-src 'self' ws: wss:; " +
        "script-src 'self'; style-src 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapFirst10Authentication();
app.MapIncidentEndpoints();
app.MapMediaEndpoints();
app.MapDispatchEndpoints();
app.MapGuidanceEndpoints();
app.MapRecognitionEndpoints();
app.MapOperationsEndpoints();
app.MapTelegramWebhook();
app.MapWhatsAppWebhook();
app.MapHub<OperationsHub>("/hubs/operations");
app.MapGet("/api/dispatch/probe", () => Results.Ok(new { access = "dispatcher" }))
    .RequireAuthorization(IdentityConfiguration.DispatcherPolicy);
app.MapGet("/api/admin/probe", () => Results.Ok(new { access = "administrator" }))
    .RequireAuthorization(IdentityConfiguration.AdministratorPolicy);
app.MapGet("/api/clinical/probe", () => Results.Ok(new { access = "clinical-approver" }))
    .RequireAuthorization(IdentityConfiguration.ClinicalApproverPolicy);

app.MapGet("/api/system", () => Results.Ok(new
{
    service = "First10.Api",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
    environment = app.Environment.EnvironmentName,
    topology = productionTopology
}))
.WithName("GetSystemMetadata");

if (!app.Environment.IsDevelopment())
{
    app.MapFallbackToFile("index.html");
}

app.Run();

public partial class Program;
