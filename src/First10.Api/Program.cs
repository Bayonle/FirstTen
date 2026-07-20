using System.Reflection;
using First10.Infrastructure.Messaging;
using First10.Infrastructure.Persistence;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var databaseConnection = builder.Configuration.GetConnectionString("first10")
    ?? throw new InvalidOperationException("Connection string 'first10' is required.");
builder.Services.AddFirst10Persistence(databaseConnection);
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

app.MapOpenApi();
app.MapDefaultEndpoints();

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
