# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish ZamzamWaterDelivery.Web/ZamzamWaterDelivery.Web.csproj -c Release -o /app/publish

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
# fontconfig is needed by QuestPDF (PDF reports) on Linux
RUN apt-get update && apt-get install -y --no-install-recommends libfontconfig1 && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
# Demo accounts password (override with a Railway variable Seed__DefaultPassword)
ENV Seed__DefaultPassword=Zamzam@2026
# Railway provides the port in $PORT
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} exec dotnet ZamzamWaterDelivery.Web.dll"]
