# syntax=docker/dockerfile:1

# Production images of the API. Development doesn't use them: it runs the API from its launch profile, with only
# MySQL and Mailpit in Docker (docker-compose.yml). See docs/deployment.md.
#
#   docker build --target runtime    -t id-indetails-api .           the API (the default target)
#   docker build --target migrations -t id-indetails-migrations .    applies EF migrations once per deployment

ARG DOTNET_VERSION=10.0

# --- build ---------------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src
COPY ID-Indetails.api/ID-Indetails.api.csproj ID-Indetails.api/
RUN dotnet restore ID-Indetails.api/ID-Indetails.api.csproj
COPY ID-Indetails.api/ ID-Indetails.api/
RUN dotnet publish ID-Indetails.api/ID-Indetails.api.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

# The migrations as one executable. It needs the model only, so the connection string here is a placeholder. When
# it runs, the bundle builds the API's services, so it reads the real one from ConnectionStrings__Default.
FROM build AS migrations-build
ARG EF_VERSION=9.0.20
RUN dotnet tool install --tool-path /tools dotnet-ef --version ${EF_VERSION}
RUN ConnectionStrings__Default="Server=placeholder;Database=placeholder" \
    /tools/dotnet-ef migrations bundle --project ID-Indetails.api/ID-Indetails.api.csproj --configuration Release \
        --output /out/efbundle --verbose

# --- migrations: run once, before the new API starts -----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS migrations
# Ubuntu's security fixes, without waiting for the base image to be rebuilt with them.
RUN apt-get update && apt-get upgrade -y --no-install-recommends && rm -rf /var/lib/apt/lists/*
COPY --from=migrations-build --chown=root:root /out/efbundle /efbundle
USER $APP_UID
ENTRYPOINT ["/efbundle"]

# --- runtime: the API, as the image's unprivileged user, plain HTTP on 8080 behind the client's nginx -------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app
# Ubuntu's security fixes, without waiting for the base image to be rebuilt with them.
RUN apt-get update && apt-get upgrade -y --no-install-recommends && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=root:root /app/publish .
# Uploads (FileStorage:RootPath) go on a volume mounted here; the app may write only there.
RUN mkdir -p /app/App_Data/files && chown -R $APP_UID /app/App_Data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -n 1 <&3 | grep -q " 200 "'
ENTRYPOINT ["dotnet", "ID-Indetails.api.dll"]
