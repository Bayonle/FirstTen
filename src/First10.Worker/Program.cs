using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Modules.Guidance;
using First10.Infrastructure.Modules.Recognition;
using First10.Infrastructure.Modules.Operations;
using Wolverine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
builder.Services.AddFirst10Intake(
    builder.Configuration,
    !builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"));
builder.Services.AddFirst10Incidents();
builder.Services.AddFirst10DispatchAndGuidance();
builder.Services.AddFirst10Recognition();
builder.Services.AddFirst10Operations();
builder.Services.AddFirst10ReadinessChecks();
builder.Services.AddHostedService<RetentionScheduler>();
if (builder.Configuration.GetValue<bool>("Infrastructure:ApplyMigrations"))
{
    builder.Services.AddHostedService<PersistenceMigrationService>();
}
builder.Host.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    databaseConnection,
    First10RuntimeRole.Worker,
    autoProvision: builder.Configuration.GetValue(
        "Infrastructure:AutoProvision",
        builder.Environment.IsDevelopment())));

var app = builder.Build();
app.MapDefaultEndpoints();
app.MapGet("/worker/status", () => Results.Ok(new { service = "First10.Worker", state = "running" }));
await app.RunAsync();
