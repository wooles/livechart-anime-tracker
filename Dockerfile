# Build Stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["LiveChartTracker.csproj", "./"]
RUN dotnet restore "LiveChartTracker.csproj"

COPY . .
RUN dotnet publish "LiveChartTracker.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime Stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV DOTNET_gcServer=0
ENV DOTNET_EnableDiagnostics=0
ENV ASPNETCORE_URLS=http://+:10000;http://+:5000;http://+:8080

EXPOSE 10000 5000 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LiveChartTracker.dll", "--server"]
