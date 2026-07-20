FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/First10.Worker/First10.Worker.csproj src/First10.Worker/
COPY src/First10.Infrastructure/First10.Infrastructure.csproj src/First10.Infrastructure/
COPY src/First10.Modules/First10.Modules.csproj src/First10.Modules/
COPY src/First10.ServiceDefaults/First10.ServiceDefaults.csproj src/First10.ServiceDefaults/
RUN dotnet restore src/First10.Worker/First10.Worker.csproj
COPY src/ src/
RUN dotnet publish src/First10.Worker/First10.Worker.csproj --no-restore -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "First10.Worker.dll"]
