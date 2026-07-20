FROM node:22-alpine AS web-build
WORKDIR /source/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/First10.Api/First10.Api.csproj src/First10.Api/
COPY src/First10.Infrastructure/First10.Infrastructure.csproj src/First10.Infrastructure/
COPY src/First10.Modules/First10.Modules.csproj src/First10.Modules/
COPY src/First10.ServiceDefaults/First10.ServiceDefaults.csproj src/First10.ServiceDefaults/
RUN dotnet restore src/First10.Api/First10.Api.csproj
COPY src/ src/
RUN dotnet publish src/First10.Api/First10.Api.csproj --no-restore -c Release -o /app/publish
COPY --from=web-build /source/web/dist /app/publish/wwwroot

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish ./
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "First10.Api.dll"]
