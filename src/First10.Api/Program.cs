using System.Reflection;
using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using First10.Infrastructure.Modules.IdentityAudit;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Guidance;
using First10.Api.Auth;
using First10.Api.Webhooks;
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
builder.Host.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    databaseConnection,
    First10RuntimeRole.Api));
builder.Services.AddOpenApi();

var app = builder.Build();
string[] productionTopology = ["api", "worker"];

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapOpenApi();
app.MapDefaultEndpoints();
app.MapFirst10Authentication();
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
