# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

COPY ["src/Apps/Sorcha.Verifier/Sorcha.Verifier.csproj", "src/Apps/Sorcha.Verifier/"]
COPY ["src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj", "src/Common/Sorcha.CitizenWallet.Abstractions/"]
COPY ["src/Common/Sorcha.ServiceDefaults/Sorcha.ServiceDefaults.csproj", "src/Common/Sorcha.ServiceDefaults/"]

RUN dotnet restore "src/Apps/Sorcha.Verifier/Sorcha.Verifier.csproj"

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

WORKDIR "/src/src/Apps/Sorcha.Verifier"
RUN dotnet publish "Sorcha.Verifier.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app

EXPOSE 8080

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_HTTP_PORTS=8080

USER $APP_UID

ENTRYPOINT ["dotnet", "Sorcha.Verifier.dll"]
