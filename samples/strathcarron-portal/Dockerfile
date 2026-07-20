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
COPY ["Directory.Build.props", "."]
COPY ["samples/Directory.Build.props", "samples/"]

# The sample's ProjectReferences (and their transitive ProjectReferences)
# fan out across src/Apps/Sorcha.UI/, src/Common/, src/Core/. Rather than
# enumerate every transitively-referenced csproj (which is a moving target
# as the platform evolves), COPY the relevant src/ subtrees in their
# entirety before restore. Slightly worse layer caching; far more
# robust against transitive-reference changes.
COPY src/ src/

# Unified versioning: the root Directory.Build.props derives the version from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT (else <Major>.0.0-dev). MSBuild reads env
# vars as properties, so surfacing the build-args as ENV here makes the published
# assembly carry 2.<run>.<attempt> — matching the image tag. Declared AFTER
# restore so the (cacheable) restore layer isn't invalidated every run.
ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}
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
