using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
builder.Services.AddHostedService<PersistenceMigrationService>();
builder.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    databaseConnection,
    First10RuntimeRole.Worker));

var host = builder.Build();
await host.RunAsync();
