# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

COPY ["src/Apps/Sorcha.Citizen.Verifier/Sorcha.Citizen.Verifier.csproj", "src/Apps/Sorcha.Citizen.Verifier/"]
COPY ["src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj", "src/Common/Sorcha.CitizenWallet.Abstractions/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "src/Common/Sorcha.ServiceDefaults/"]

RUN dotnet restore "src/Apps/Sorcha.Citizen.Verifier/Sorcha.Citizen.Verifier.csproj"

COPY src/ ./src/

WORKDIR "/src/src/Apps/Sorcha.Citizen.Verifier"
RUN dotnet publish "Sorcha.Citizen.Verifier.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Sorcha.Citizen.Verifier.dll"]
