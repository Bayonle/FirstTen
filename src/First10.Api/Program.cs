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

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
var requireSecureCookies = !builder.Environment.IsDevelopment();
builder.Services.AddFirst10Identity(requireSecureCookies);
builder.Services.AddFirst10Intake();
builder.Services.AddFirst10Incidents();
builder.Services.AddFirst10DispatchAndGuidance();
builder.Services.AddFirst10Recognition();
builder.Services.AddFirst10Operations();
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
    typeof(Program).Assembly));
builder.Services.AddOpenApi();

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
