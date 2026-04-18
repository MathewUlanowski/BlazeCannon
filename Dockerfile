FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish BlazeCannon.App/BlazeCannon.App.csproj -c Release -o /app

# Install Playwright browsers during build (using the SDK image which has pwsh)
RUN pwsh /app/playwright.ps1 install --with-deps chromium

FROM mcr.microsoft.com/dotnet/aspnet:8.0

# Install Playwright's runtime dependencies
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
# Copy Playwright browser binaries from the build stage
COPY --from=build /root/.cache/ms-playwright /root/.cache/ms-playwright

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "BlazeCannon.App.dll"]
