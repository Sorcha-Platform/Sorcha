# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/Directory.Build.props", "src/Services/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

# Copy project files
COPY ["src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj", "src/Services/Sorcha.Blueprint.Service/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "src/Common/Sorcha.ServiceDefaults/"]
COPY ["src/Common/Sorcha.Blueprint.Models/Sorcha.Blueprint.Models.csproj", "src/Common/Sorcha.Blueprint.Models/"]

# Restore dependencies
RUN dotnet restore "src/Services/Sorcha.Blueprint.Service/Sorcha.Blueprint.Service.csproj"

# Copy source code
COPY src/ ./src/

# Build the application
WORKDIR "/src/src/Services/Sorcha.Blueprint.Service"
RUN dotnet build "Sorcha.Blueprint.Service.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Sorcha.Blueprint.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Permissions stage - Create directory structure and install health check binary for non-root user
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS permissions
RUN apt-get update && apt-get install -y --no-install-recommends busybox-static && rm -rf /var/lib/apt/lists/* && \
    mkdir -p /home/app/.aspnet/DataProtection-Keys && \
    chown -R 1654:1654 /home/app/.aspnet

# Final stage - Ubuntu Chiseled (distroless)
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

# Copy health check binary (busybox-static for chiseled image health probes)
COPY --from=permissions /bin/busybox /bin/busybox

# Copy directory structure with permissions from permissions stage
COPY --from=permissions --chown=$APP_UID:$APP_UID /home/app/.aspnet /home/app/.aspnet

# Expose ports
EXPOSE 8080
EXPOSE 8081

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

# Run as non-root user (chiseled images are minimal and secure)
USER $APP_UID

ENTRYPOINT ["dotnet", "Sorcha.Blueprint.Service.dll"]
