# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Services/Directory.Build.props", "src/Services/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

# Copy project files for restore
COPY ["src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj", "src/Services/Sorcha.ApiGateway/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "src/Common/Sorcha.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/Services/Sorcha.ApiGateway/Sorcha.ApiGateway.csproj"

# Copy source code
COPY src/ ./src/

# Unified versioning: the root Directory.Build.props derives the version from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT (else <Major>.0.0-dev). MSBuild reads env
# vars as properties, so surfacing the build-args as ENV here makes the published
# assembly carry 2.<run>.<attempt> — matching the image tag. Declared AFTER
# restore so the (cacheable) restore layer isn't invalidated every run.
ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}

# AI-discoverability (spec 117): the gateway embeds the canonical root llms.txt + docs/llms-full.txt
# (see Sorcha.ApiGateway.csproj <EmbeddedResource>) to serve them at /llms.txt and /llms-full.txt.
# They live outside src/, so copy them into the build context before the build resolves the embed.
COPY llms.txt ./llms.txt
COPY docs/llms-full.txt ./docs/llms-full.txt

# Build the application
WORKDIR "/src/src/Services/Sorcha.ApiGateway"
RUN dotnet build "Sorcha.ApiGateway.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Sorcha.ApiGateway.csproj" -c Release -o /app/publish /p:UseAppHost=false

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

ENTRYPOINT ["dotnet", "Sorcha.ApiGateway.dll"]
