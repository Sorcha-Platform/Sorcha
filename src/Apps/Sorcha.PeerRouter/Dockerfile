# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]

# Copy project files (PeerRouter references Peer Service protos)
COPY ["src/Apps/Sorcha.PeerRouter/Sorcha.PeerRouter.csproj", "src/Apps/Sorcha.PeerRouter/"]

# Restore dependencies
RUN dotnet restore "src/Apps/Sorcha.PeerRouter/Sorcha.PeerRouter.csproj"

# Copy source code (including proto files from Peer Service)
COPY src/ ./src/

# Build the application
WORKDIR "/src/src/Apps/Sorcha.PeerRouter"
RUN dotnet build "Sorcha.PeerRouter.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Sorcha.PeerRouter.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:ErrorOnDuplicatePublishOutputFiles=false

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

# Expose ports (gRPC and HTTP)
EXPOSE 5000
EXPOSE 8080

# Copy published application
COPY --from=publish /app/publish .

# Set environment variables
ENV PEERROUTER__PORT=5000
ENV PEERROUTER__HTTP_PORT=8080

# Run as non-root user (chiseled images are minimal and secure)
USER $APP_UID

ENTRYPOINT ["dotnet", "Sorcha.PeerRouter.dll"]
