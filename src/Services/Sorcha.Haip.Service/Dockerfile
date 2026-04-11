# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["src/Services/Directory.Build.props", "src/Services/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

# Copy project files for restore
COPY ["src/Services/Sorcha.Haip.Service/Sorcha.Haip.Service.csproj", "src/Services/Sorcha.Haip.Service/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "src/Common/Sorcha.ServiceDefaults/"]
COPY ["src/Common/Sorcha.ServiceClients/Sorcha.ServiceClients.csproj", "src/Common/Sorcha.ServiceClients/"]
COPY ["src/Common/Sorcha.Cryptography/Sorcha.Cryptography.csproj", "src/Common/Sorcha.Cryptography/"]
COPY ["src/Common/Sorcha.Blueprint.Models/Sorcha.Blueprint.Models.csproj", "src/Common/Sorcha.Blueprint.Models/"]
COPY ["src/Common/Sorcha.Tenant.Models/Sorcha.Tenant.Models.csproj", "src/Common/Sorcha.Tenant.Models/"]

# Restore dependencies
RUN dotnet restore "src/Services/Sorcha.Haip.Service/Sorcha.Haip.Service.csproj"

# Copy source code
COPY src/ ./src/

# Build the application
WORKDIR "/src/src/Services/Sorcha.Haip.Service"
RUN dotnet build "Sorcha.Haip.Service.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Sorcha.Haip.Service.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Dependencies stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS dependencies
RUN apt-get update && apt-get install -y --no-install-recommends \
    libgssapi-krb5-2 \
    busybox-static \
    && rm -rf /var/lib/apt/lists/*

RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && \
    chown -R 1654:1654 /home/app/.aspnet

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app

COPY --from=dependencies /bin/busybox /bin/busybox
COPY --from=dependencies --chown=$APP_UID:$APP_UID /home/app/.aspnet /home/app/.aspnet

EXPOSE 8080

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Sorcha.Haip.Service.dll"]
