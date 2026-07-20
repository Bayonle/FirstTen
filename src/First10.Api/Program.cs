using System.Reflection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
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
