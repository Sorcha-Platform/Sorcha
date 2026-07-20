# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Apps/Directory.Build.props", "Apps/"]
COPY ["src/Common/Directory.Build.props", "Common/"]

# Copy project files for restoration
COPY ["src/Apps/Sorcha.McpServer/Sorcha.McpServer.csproj", "Apps/Sorcha.McpServer/"]
COPY ["src/Common/Sorcha.ServiceClients/Sorcha.ServiceClients.csproj", "Common/Sorcha.ServiceClients/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "Common/Sorcha.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "Apps/Sorcha.McpServer/Sorcha.McpServer.csproj"

# Copy source code
COPY src/ .

# Unified versioning: the root Directory.Build.props derives the version from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT (else <Major>.0.0-dev). MSBuild reads env
# vars as properties, so surfacing the build-args as ENV here makes the published
# assembly carry 2.<run>.<attempt> — matching the image tag. Declared AFTER
# restore so the (cacheable) restore layer isn't invalidated every run.
ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}

# Build and publish the application
WORKDIR "/src/Apps/Sorcha.McpServer"
RUN dotnet publish "Sorcha.McpServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage - use ASP.NET Core runtime (required for ServiceDefaults dependencies)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy published application
COPY --from=build /app/publish .

# Set environment variables
ENV DOTNET_ENVIRONMENT=Production

# Run the MCP server
# Note: JWT token must be provided via --jwt-token argument or SORCHA_JWT_TOKEN environment variable
ENTRYPOINT ["dotnet", "Sorcha.McpServer.dll"]
