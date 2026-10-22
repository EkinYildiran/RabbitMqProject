
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["OrderProcessingWorker/OrderProcessingWorker.csproj", "OrderProcessingWorker/"]
COPY ["RabbitMq.Shared/RabbitMq.Shared.csproj", "RabbitMq.Shared/"]

RUN dotnet restore "OrderProcessingWorker/OrderProcessingWorker.csproj"

COPY . .

RUN dotnet publish "OrderProcessingWorker/OrderProcessingWorker.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OrderProcessingWorker.dll"]
