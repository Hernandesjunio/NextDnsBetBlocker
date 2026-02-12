FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/NextDnsBetBlocker.Core/NextDnsBetBlocker.Core.csproj", "NextDnsBetBlocker.Core/"]
COPY ["src/NextDnsBetBlocker.Worker/NextDnsBetBlocker.Worker.csproj", "NextDnsBetBlocker.Worker/"]

RUN dotnet restore "NextDnsBetBlocker.Worker/NextDnsBetBlocker.Worker.csproj"

COPY src/ .

RUN dotnet build "NextDnsBetBlocker.Worker/NextDnsBetBlocker.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NextDnsBetBlocker.Worker/NextDnsBetBlocker.Worker.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=publish /app/publish .

RUN mkdir -p /app/data

ENTRYPOINT ["dotnet", "NextDnsBetBlocker.Worker.dll"]
