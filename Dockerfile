# Uma compilação, duas imagens: "api" (que também roda --migrar) e "worker".
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/AvaliaSales.Domain/AvaliaSales.Domain.csproj src/AvaliaSales.Domain/
COPY src/AvaliaSales.Application/AvaliaSales.Application.csproj src/AvaliaSales.Application/
COPY src/AvaliaSales.Infrastructure/AvaliaSales.Infrastructure.csproj src/AvaliaSales.Infrastructure/
COPY src/AvaliaSales.Api/AvaliaSales.Api.csproj src/AvaliaSales.Api/
COPY src/AvaliaSales.Worker/AvaliaSales.Worker.csproj src/AvaliaSales.Worker/
RUN dotnet restore src/AvaliaSales.Api/AvaliaSales.Api.csproj \
 && dotnet restore src/AvaliaSales.Worker/AvaliaSales.Worker.csproj
COPY src/ src/
RUN dotnet publish src/AvaliaSales.Api/AvaliaSales.Api.csproj -c Release -o /app/api --no-restore \
 && dotnet publish src/AvaliaSales.Worker/AvaliaSales.Worker.csproj -c Release -o /app/worker --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
COPY --from=build /app/api .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "AvaliaSales.Api.dll"]

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS worker
WORKDIR /app
COPY --from=build /app/worker .
USER $APP_UID
ENTRYPOINT ["dotnet", "AvaliaSales.Worker.dll"]
