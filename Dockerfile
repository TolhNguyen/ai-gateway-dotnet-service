# ===========================================================
# Build stage
# ===========================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/AiGateway.Api/AiGateway.Api.csproj ./AiGateway.Api/
RUN dotnet restore ./AiGateway.Api/AiGateway.Api.csproj

COPY src/AiGateway.Api ./AiGateway.Api
RUN dotnet publish ./AiGateway.Api/AiGateway.Api.csproj \
        -c Release \
        -o /app/publish \
        /p:UseAppHost=false

# ===========================================================
# Runtime stage: bundles Postgres + Redis + .NET + supervisord
# ===========================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0

ENV DEBIAN_FRONTEND=noninteractive \
    PG_VERSION=15 \
    PGDATA=/var/lib/postgresql/data \
    POSTGRES_DB=ai_gateway \
    POSTGRES_USER=ai_gateway \
    POSTGRES_PASSWORD=ai_gateway_local \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

RUN set -eux; \
    apt-get update; \
    apt-get install -y --no-install-recommends \
        postgresql-15 \
        postgresql-contrib \
        redis-server \
        supervisor \
        gosu \
        tini \
        ca-certificates \
        locales \
    ; \
    sed -i -e 's/# en_US.UTF-8 UTF-8/en_US.UTF-8 UTF-8/' /etc/locale.gen; \
    locale-gen; \
    rm -rf /var/lib/apt/lists/*; \
    mkdir -p /var/log/supervisor /var/run/postgresql /var/lib/redis; \
    chown -R postgres:postgres /var/run/postgresql; \
    chown -R redis:redis /var/lib/redis

ENV LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8

WORKDIR /app
COPY --from=build /app/publish ./
COPY migrations ./migrations

COPY docker/supervisord.conf  /etc/supervisor/supervisord.conf
COPY docker/redis.conf        /etc/redis/redis.conf
COPY docker/entrypoint.sh     /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

# Persistent data
VOLUME ["/var/lib/postgresql/data", "/var/lib/redis"]

EXPOSE 8080

ENTRYPOINT ["/usr/bin/tini", "--", "/usr/local/bin/entrypoint.sh"]
