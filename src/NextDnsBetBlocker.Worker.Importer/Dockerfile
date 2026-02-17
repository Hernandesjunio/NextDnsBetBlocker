FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj", "NextDnsBetBlocker.Core/"]
COPY ["src/NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj", "NextDnsBetBlocker.Worker.Importer/"]

RUN dotnet restore "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj"

COPY src/ .

RUN dotnet build "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NextDnsBetBlocker.Worker.Importer/NextDnsBetBlocker.Worker.Importer.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=publish /app/publish .

RUN mkdir -p /app/data

ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.Importer.dll"]


# Labels for documentation
LABEL maintainer="NextDnsBetBlocker"
LABEL description="On-demand importer for DNS blocklists. Runs via ACI weekly."
LABEL version="1.0.0"
