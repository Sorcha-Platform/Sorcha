# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# Two-stage build: dotnet publish produces the static WASM bundle,
# then nginx:alpine serves wwwroot/ on :8080. PWA-friendly cache headers
# (long TTL on hashed asset filenames, no-cache on index.html and
# service-worker.js so wallet updates land on next visit).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

COPY ["src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj", "src/Apps/Sorcha.Wallet.Pwa/"]
COPY ["src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj", "src/Common/Sorcha.CitizenWallet.Abstractions/"]
COPY ["src/Common/Sorcha.ServiceClients.Http/Sorcha.ServiceClients.Http.csproj", "src/Common/Sorcha.ServiceClients.Http/"]
COPY ["src/Common/Sorcha.Register.Models/Sorcha.Register.Models.csproj", "src/Common/Sorcha.Register.Models/"]

RUN dotnet restore "src/Apps/Sorcha.Wallet.Pwa/Sorcha.Wallet.Pwa.csproj"

COPY src/ ./src/

# Unified versioning: the root Directory.Build.props derives the version from
# GITHUB_RUN_NUMBER / GITHUB_RUN_ATTEMPT (else 2.0.0-dev). MSBuild reads env vars
# as properties, so surfacing the build-args as ENV here makes the published
# assembly carry 2.<run>.<attempt> — matching the image tag. Declared AFTER
# restore so the (cacheable) restore layer isn't invalidated every run.
ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}

WORKDIR "/src/src/Apps/Sorcha.Wallet.Pwa"
RUN dotnet publish "Sorcha.Wallet.Pwa.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# Custom nginx config — PWA cache headers + base href fallback to index.html.
COPY src/Apps/Sorcha.Wallet.Pwa/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
