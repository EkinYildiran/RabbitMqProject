
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["DatabaseWorker/DatabaseWorker.csproj", "DatabaseWorker/"]
COPY ["RabbitMq.Shared/RabbitMq.Shared.csproj", "RabbitMq.Shared/"]

RUN dotnet restore "DatabaseWorker/DatabaseWorker.csproj"

COPY . .

RUN dotnet publish "DatabaseWorker/DatabaseWorker.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "DatabaseWorker.dll"]
