# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["LiveChartTracker.csproj", "./"]
RUN dotnet restore "LiveChartTracker.csproj"

COPY . .
RUN dotnet publish "LiveChartTracker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage (Debian-based official ASP.NET Core runtime with full ICU/globalization support)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

EXPOSE 5000
EXPOSE 10000
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LiveChartTracker.dll", "--server"]
