# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Strathcarron Council sample portal (F127). A standalone Blazor WASM
# application served by nginx — built from outside src/ to honour the
# platform-vs-consumer boundary contract.

# Build stage — needs the platform's csproj files for ProjectReference
# restoration. Build context is the repo root (set in docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Central package management + build-prop hierarchies that the
# referenced platform projects participate in.
COPY ["Directory.Packages.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]
COPY ["src/Core/Directory.Build.props", "src/Core/"]

# Sample csproj.
COPY ["samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj", "samples/strathcarron-portal/"]
# Allowed platform-side csprojs the sample references (per boundary rule).
COPY ["src/Apps/Sorcha.UI/Sorcha.UI.Components.User/Sorcha.UI.Components.User.csproj", "src/Apps/Sorcha.UI/Sorcha.UI.Components.User/"]
COPY ["src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj", "src/Common/Sorcha.CitizenWallet.Abstractions/"]
COPY ["src/Common/Sorcha.ServiceClients.Http/Sorcha.ServiceClients.Http.csproj", "src/Common/Sorcha.ServiceClients.Http/"]
# Any transitive csprojs Sorcha.UI.Components.User or the ServiceClients
# bring along are restored implicitly via `dotnet restore`.

RUN dotnet restore "samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj"

# Copy full source for build (samples/ + the src/ subtrees the sample
# touches).
COPY samples/ samples/
COPY src/ src/

WORKDIR /src/samples/strathcarron-portal
RUN dotnet publish "Sorcha.Sample.StrathcarronPortal.csproj" -c Release -o /app/publish

# Runtime — nginx serving the published WASM bundle.
FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot/ /usr/share/nginx/html/

EXPOSE 80
