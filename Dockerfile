# syntax=docker/dockerfile:1

FROM node:24-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
# Manifests first so the restore layer is cached until a csproj or package version changes.
COPY global.json Directory.Build.props Directory.Packages.props PlantOps.slnx ./
COPY src/Host/PlantOps.Api/PlantOps.Api.csproj src/Host/PlantOps.Api/
COPY src/BuildingBlocks/PlantOps.SharedKernel/PlantOps.SharedKernel.csproj src/BuildingBlocks/PlantOps.SharedKernel/
COPY src/Modules/Assets/PlantOps.Modules.Assets/PlantOps.Modules.Assets.csproj src/Modules/Assets/PlantOps.Modules.Assets/
COPY src/Modules/Assets/PlantOps.Modules.Assets.Contracts/PlantOps.Modules.Assets.Contracts.csproj src/Modules/Assets/PlantOps.Modules.Assets.Contracts/
COPY src/Modules/WorkOrders/PlantOps.Modules.WorkOrders/PlantOps.Modules.WorkOrders.csproj src/Modules/WorkOrders/PlantOps.Modules.WorkOrders/
COPY src/Modules/WorkOrders/PlantOps.Modules.WorkOrders.Contracts/PlantOps.Modules.WorkOrders.Contracts.csproj src/Modules/WorkOrders/PlantOps.Modules.WorkOrders.Contracts/
COPY src/Modules/Inventory/PlantOps.Modules.Inventory/PlantOps.Modules.Inventory.csproj src/Modules/Inventory/PlantOps.Modules.Inventory/
COPY src/Modules/Inventory/PlantOps.Modules.Inventory.Contracts/PlantOps.Modules.Inventory.Contracts.csproj src/Modules/Inventory/PlantOps.Modules.Inventory.Contracts/
COPY src/Modules/Identity/PlantOps.Modules.Identity/PlantOps.Modules.Identity.csproj src/Modules/Identity/PlantOps.Modules.Identity/
COPY src/Modules/Identity/PlantOps.Modules.Identity.Contracts/PlantOps.Modules.Identity.Contracts.csproj src/Modules/Identity/PlantOps.Modules.Identity.Contracts/
RUN dotnet restore src/Host/PlantOps.Api/PlantOps.Api.csproj
COPY src/ src/
RUN dotnet publish src/Host/PlantOps.Api/PlantOps.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./
COPY --from=web /web/dist/plantops-web/browser ./wwwroot
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "PlantOps.Api.dll"]
