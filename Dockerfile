# Stage 1: Build .NET app
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build

WORKDIR /src
COPY src/PokerPlanning/ ./PokerPlanning/
RUN dotnet publish PokerPlanning/PokerPlanning.csproj \
    -c Release \
    -o /app/publish

# Stage 2: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine

LABEL \
    io.hass.name="Planning Poker" \
    io.hass.description="Collaborative estimation tool" \
    io.hass.arch="amd64" \
    io.hass.type="addon" \
    io.hass.version="1.0.6"

RUN apk add --no-cache icu-libs
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENV ASPNETCORE_URLS="http://0.0.0.0:5000" \
    ASPNETCORE_ENVIRONMENT="Production"

WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 5000

ENTRYPOINT ["dotnet", "PokerPlanning.dll"]
