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
COPY ["src/Apps/Directory.Build.props", "src/Apps/"]
COPY ["src/Common/Directory.Build.props", "src/Common/"]

COPY ["src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj", "src/Apps/Sorcha.Citizen.Wallet/"]
COPY ["src/Common/Sorcha.CitizenWallet.Abstractions/Sorcha.CitizenWallet.Abstractions.csproj", "src/Common/Sorcha.CitizenWallet.Abstractions/"]
COPY ["src/Common/Sorcha.ServiceClients.Http/Sorcha.ServiceClients.Http.csproj", "src/Common/Sorcha.ServiceClients.Http/"]
COPY ["src/Common/Sorcha.Register.Models/Sorcha.Register.Models.csproj", "src/Common/Sorcha.Register.Models/"]

RUN dotnet restore "src/Apps/Sorcha.Citizen.Wallet/Sorcha.Citizen.Wallet.csproj"

COPY src/ ./src/

WORKDIR "/src/src/Apps/Sorcha.Citizen.Wallet"
RUN dotnet publish "Sorcha.Citizen.Wallet.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html

# Custom nginx config — PWA cache headers + base href fallback to index.html.
COPY src/Apps/Sorcha.Citizen.Wallet/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080

CMD ["nginx", "-g", "daemon off;"]
