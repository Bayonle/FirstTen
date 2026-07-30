using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Wolverine;

var builder = Host.CreateApplicationBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(connectionString);
builder.UseWolverine(options => WolverineConfiguration.Configure(
    options,
    connectionString,
    First10RuntimeRole.Api,
    autoProvision: true));

using var host = builder.Build();
await using (var scope = host.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<First10DbContext>();
    await database.Database.MigrateAsync();
}

await host.StartAsync();
await host.StopAsync();
