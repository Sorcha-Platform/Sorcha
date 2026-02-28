# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files for restoration
COPY ["src/Apps/Sorcha.McpServer/Sorcha.McpServer.csproj", "Apps/Sorcha.McpServer/"]
COPY ["src/Common/Sorcha.ServiceClients/Sorcha.ServiceClients.csproj", "Common/Sorcha.ServiceClients/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "Common/Sorcha.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "Apps/Sorcha.McpServer/Sorcha.McpServer.csproj"

# Copy source code
COPY src/ .

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
