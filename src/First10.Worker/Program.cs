using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using First10.Infrastructure.Modules.Intake;
using First10.Infrastructure.Modules.Incidents;
using First10.Infrastructure.Modules.Guidance;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
builder.Services.AddFirst10Intake();
builder.Services.AddFirst10Incidents();
builder.Services.AddFirst10DispatchAndGuidance();
builder.Services.AddHostedService<PersistenceMigrationService>();
builder.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    databaseConnection,
    First10RuntimeRole.Worker));

var host = builder.Build();
await host.RunAsync();
