# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors
#
# The Sorcha Reader (Feature 185) — the verifier's half of an in-person check.
#
# Two-stage build: dotnet publish produces the static WASM bundle, then nginx serves it on :8080.
# Gateway routes /reader/* here (PathRemovePrefix), mirroring the wallet PWA.
#
# NOTE the app itself verifies OFFLINE, on-device. This image only DELIVERS it — once loaded, the reader
# needs no network at all, which is the entire point.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Packages.props", "."]
COPY ["Directory.Build.props", "."]
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

COPY ["src/Apps/Sorcha.Verifier.Pwa/Sorcha.Verifier.Pwa.csproj", "src/Apps/Sorcha.Verifier.Pwa/"]
COPY ["src/Common/Sorcha.Mdoc/Sorcha.Mdoc.csproj", "src/Common/Sorcha.Mdoc/"]
COPY ["src/Common/Sorcha.Proximity.Abstractions/Sorcha.Proximity.Abstractions.csproj", "src/Common/Sorcha.Proximity.Abstractions/"]
COPY ["src/Common/Sorcha.Proximity.Capacitor/Sorcha.Proximity.Capacitor.csproj", "src/Common/Sorcha.Proximity.Capacitor/"]

RUN dotnet restore "src/Apps/Sorcha.Verifier.Pwa/Sorcha.Verifier.Pwa.csproj"

COPY src/ ./src/

ARG GITHUB_RUN_NUMBER
ARG GITHUB_RUN_ATTEMPT
ENV GITHUB_RUN_NUMBER=${GITHUB_RUN_NUMBER}
ENV GITHUB_RUN_ATTEMPT=${GITHUB_RUN_ATTEMPT}

WORKDIR "/src/src/Apps/Sorcha.Verifier.Pwa"
RUN dotnet publish "Sorcha.Verifier.Pwa.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/Apps/Sorcha.Verifier.Pwa/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
