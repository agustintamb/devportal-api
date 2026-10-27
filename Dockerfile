# Build stage: full SDK, compiles and publishes the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project file first: the restore layer is reused while the package list does not change.
COPY global.json .editorconfig ./
COPY src/DevportalApi/DevportalApi.csproj src/DevportalApi/
RUN dotnet restore src/DevportalApi/DevportalApi.csproj

COPY src/DevportalApi/ src/DevportalApi/
# The OpenAPI file is generated and committed by `dotnet build` (and checked in CI), not inside the image.
RUN dotnet publish src/DevportalApi/DevportalApi.csproj -c Release -o /app/publish --no-restore \
    -p:OpenApiGenerateDocuments=false

# Runtime stage: full Debian ASP.NET image (with ICU, needed by the accent-insensitive search).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .

# The SQLite file (db/portal.db, relative to /app) is created on startup by the non-root user.
RUN mkdir db && chown "$APP_UID" db
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DevportalApi.dll"]
