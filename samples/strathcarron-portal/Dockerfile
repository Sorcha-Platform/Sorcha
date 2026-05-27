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

# Central package management + build-prop hierarchies.
COPY ["Directory.Packages.props", "."]
COPY ["samples/Directory.Build.props", "samples/"]

# The sample's ProjectReferences (and their transitive ProjectReferences)
# fan out across src/Apps/Sorcha.UI/, src/Common/, src/Core/. Rather than
# enumerate every transitively-referenced csproj (which is a moving target
# as the platform evolves), COPY the relevant src/ subtrees in their
# entirety before restore. Slightly worse layer caching; far more
# robust against transitive-reference changes.
COPY src/ src/
COPY samples/strathcarron-portal/ samples/strathcarron-portal/

RUN dotnet restore "samples/strathcarron-portal/Sorcha.Sample.StrathcarronPortal.csproj"

WORKDIR /src/samples/strathcarron-portal
RUN dotnet publish "Sorcha.Sample.StrathcarronPortal.csproj" -c Release -o /app/publish

# Runtime — nginx serving the published WASM bundle with SPA fallback.
FROM nginx:alpine AS final
COPY --from=build /app/publish/wwwroot/ /usr/share/nginx/html/
COPY samples/strathcarron-portal/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
