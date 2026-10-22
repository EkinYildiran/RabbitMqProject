
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["SyntheticDataWorker/SyntheticDataWorker.csproj", "SyntheticDataWorker/"]
COPY ["RabbitMq.Shared/RabbitMq.Shared.csproj", "RabbitMq.Shared/"]

RUN dotnet restore "SyntheticDataWorker/SyntheticDataWorker.csproj"

COPY . .

RUN dotnet publish "SyntheticDataWorker/SyntheticDataWorker.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "SyntheticDataWorker.dll"]
