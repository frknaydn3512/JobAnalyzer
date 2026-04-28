# Use the .NET 10.0 SDK for building the application
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["JobAnalyzer.Web/JobAnalyzer.Web.csproj", "JobAnalyzer.Web/"]
COPY ["JobAnalyzer.Data/JobAnalyzer.Data.csproj", "JobAnalyzer.Data/"]
COPY ["JobAnalyzer.Scraper/JobAnalyzer.Scraper.csproj", "JobAnalyzer.Scraper/"]

RUN dotnet restore "JobAnalyzer.Web/JobAnalyzer.Web.csproj"

COPY . .

WORKDIR "/src/JobAnalyzer.Web"
RUN dotnet publish "JobAnalyzer.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
  CMD curl -f http://localhost:8080 || exit 1

ENTRYPOINT ["dotnet", "JobAnalyzer.Web.dll"]
