# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Sorcha CLI image (F191): lets hosts without the .NET SDK run `sorcha` — primarily
# `sorcha workload-ca init` from scripts/sorcha-setup.sh to provision the per-installation
# workload-identity certificates. Usage:
#   docker run --rm -v ./config/workload-certs:/certs sorchadev/cli \
#     workload-ca init --dir /certs --installation "$INSTALLATION_NAME"

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build property files for central package management
COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]

# The CLI references many sibling projects across src/Apps and src/Common, so restore is
# implicit in publish after the full source copy (no cacheable partial-restore layer).
COPY src/ ./src/

# Unified versioning: the root Directory.Build.props derives the version from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT (else <Major>.0.0-dev). MSBuild reads env
# vars as properties, so surfacing the build-args as ENV here makes the published
# assembly carry 2.<run>.<attempt> — matching the image tag. Declared AFTER the
# COPY src/ line per the §14 versioning rule.
ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}

RUN dotnet publish "src/Apps/Sorcha.Cli/Sorcha.Cli.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage. Deliberately NOT chiseled and running as root: this is an operator tool that
# writes certificate material onto bind-mounted host directories, where a fixed non-root UID
# would fight host filesystem ownership.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Sorcha.Cli.dll"]
