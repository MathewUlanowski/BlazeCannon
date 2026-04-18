FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Layer 1: Copy only csproj files + sln (changes rarely)
COPY BlazeCannon.sln .
COPY BlazeCannon.Core/BlazeCannon.Core.csproj BlazeCannon.Core/
COPY BlazeCannon.Protocol/BlazeCannon.Protocol.csproj BlazeCannon.Protocol/
COPY BlazeCannon.Proxy/BlazeCannon.Proxy.csproj BlazeCannon.Proxy/
COPY BlazeCannon.Scanner/BlazeCannon.Scanner.csproj BlazeCannon.Scanner/
COPY BlazeCannon.Browser/BlazeCannon.Browser.csproj BlazeCannon.Browser/
COPY BlazeCannon.Api/BlazeCannon.Api.csproj BlazeCannon.Api/

# Layer 2: Restore — cached until a csproj changes
RUN dotnet restore BlazeCannon.Api/BlazeCannon.Api.csproj

# Layer 3: Install Playwright — cached until Playwright package version changes
# BlazeCannon.Browser still ships Playwright; Scanner uses it through Browser.
RUN dotnet publish BlazeCannon.Browser/BlazeCannon.Browser.csproj -c Release -o /tmp/browser-shim || true
RUN pwsh /tmp/browser-shim/playwright.ps1 install --with-deps chromium

# Layer 4: Copy source and build (this is what changes on every code edit)
COPY . .
RUN dotnet publish BlazeCannon.Api/BlazeCannon.Api.csproj -c Release -o /app

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0

RUN apt-get update && apt-get install -y --no-install-recommends \
    libglib2.0-0 libnss3 libnspr4 libdbus-1-3 libatk1.0-0 \
    libatk-bridge2.0-0 libcups2 libdrm2 libxkbcommon0 \
    libxcomposite1 libxdamage1 libxfixes3 libxrandr2 libgbm1 \
    libpango-1.0-0 libcairo2 libasound2 libatspi2.0-0 \
    libxshmfence1 libx11-6 libxext6 libxcb1 libx11-xcb1 \
    fonts-liberation libfontconfig1 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .
COPY --from=build /root/.cache/ms-playwright /root/.cache/ms-playwright

EXPOSE 8080 5001
ENV BLAZECANNON_UI_PORT=8080
ENV BLAZECANNON_PROXY_PORT=5001
ENTRYPOINT ["dotnet", "BlazeCannon.Api.dll"]
