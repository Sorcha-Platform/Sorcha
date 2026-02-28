# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["src/Apps/Directory.Build.props", "Apps/"]
COPY ["src/Common/Directory.Build.props", "Common/"]
COPY ["src/Core/Directory.Build.props", "Core/"]

# Copy project files for restoration
COPY ["src/Apps/Sorcha.UI/Sorcha.UI.Web/Sorcha.UI.Web.csproj", "Apps/Sorcha.UI/Sorcha.UI.Web/"]
COPY ["src/Apps/Sorcha.UI/Sorcha.UI.Web.Client/Sorcha.UI.Web.Client.csproj", "Apps/Sorcha.UI/Sorcha.UI.Web.Client/"]
COPY ["src/Apps/Sorcha.UI/Sorcha.UI.Core/Sorcha.UI.Core.csproj", "Apps/Sorcha.UI/Sorcha.UI.Core/"]
COPY ["src/Common/Sorcha.Blueprint.Models/Sorcha.Blueprint.Models.csproj", "Common/Sorcha.Blueprint.Models/"]
COPY ["src/Core/Sorcha.Blueprint.Fluent/Sorcha.Blueprint.Fluent.csproj", "Core/Sorcha.Blueprint.Fluent/"]
COPY ["src/Core/Sorcha.Blueprint.Schemas/Sorcha.Blueprint.Schemas.csproj", "Core/Sorcha.Blueprint.Schemas/"]

# Restore dependencies
RUN dotnet restore "Apps/Sorcha.UI/Sorcha.UI.Web/Sorcha.UI.Web.csproj"

# Copy source code
COPY src/ .

# Build and publish the application
WORKDIR "/src/Apps/Sorcha.UI/Sorcha.UI.Web"
RUN dotnet publish "Sorcha.UI.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Permissions stage - Create directory structure for non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS permissions
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && \
    chown -R 1654:1654 /home/app/.aspnet

# Runtime stage - use ASP.NET Core runtime for Blazor Web App
# Note: UI Web uses non-chiseled image because Blazor WASM requires full ICU and globalization support
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

# Copy directory structure with permissions from permissions stage
COPY --from=permissions --chown=1654:1654 /home/app/.aspnet /home/app/.aspnet

# Expose ports
EXPOSE 8080
EXPOSE 8443

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# Run as non-root user (UID 1654 matches chiseled image $APP_UID)
USER 1654

ENTRYPOINT ["dotnet", "Sorcha.UI.Web.dll"]
