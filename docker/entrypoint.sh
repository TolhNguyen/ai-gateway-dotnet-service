#!/usr/bin/env bash
set -euo pipefail

# ---------------------------------------------------------
# Postgres init (idempotent)
# ---------------------------------------------------------
init_postgres() {
    if [ -s "${PGDATA}/PG_VERSION" ]; then
        echo "[entrypoint] Postgres data dir already initialized"
        return 0
    fi

    echo "[entrypoint] Initializing Postgres data dir at ${PGDATA}"
    mkdir -p "${PGDATA}"
    chown -R postgres:postgres "${PGDATA}"
    chmod 700 "${PGDATA}"

    gosu postgres /usr/lib/postgresql/15/bin/initdb \
        -D "${PGDATA}" \
        --auth-local=trust \
        --auth-host=scram-sha-256 \
        --no-locale \
        --encoding=UTF8 \
        --username=postgres \
        > /dev/null

    # Listen only on loopback (single-container in-process database)
    {
        echo "listen_addresses = '127.0.0.1'"
        echo "unix_socket_directories = '/var/run/postgresql'"
        echo "max_connections = 100"
        echo "shared_buffers = 128MB"
        echo "log_timezone = 'UTC'"
        echo "timezone = 'UTC'"
    } >> "${PGDATA}/postgresql.conf"

    # Bootstrap app user + database via temporary in-process server
    gosu postgres /usr/lib/postgresql/15/bin/pg_ctl \
        -D "${PGDATA}" \
        -o "-c listen_addresses=''" \
        -w start > /dev/null

    gosu postgres psql --no-psqlrc -v ON_ERROR_STOP=1 <<-EOSQL
        CREATE USER ${POSTGRES_USER} WITH PASSWORD '${POSTGRES_PASSWORD}';
        CREATE DATABASE ${POSTGRES_DB} OWNER ${POSTGRES_USER};
        GRANT ALL PRIVILEGES ON DATABASE ${POSTGRES_DB} TO ${POSTGRES_USER};
EOSQL

    gosu postgres /usr/lib/postgresql/15/bin/pg_ctl \
        -D "${PGDATA}" \
        -m fast \
        -w stop > /dev/null

    # Loopback-only trust auth (single container, no external Postgres access)
    cat > "${PGDATA}/pg_hba.conf" <<-'EOF'
        local   all             all                                     trust
        host    all             all             127.0.0.1/32            trust
        host    all             all             ::1/128                 trust
EOF

    echo "[entrypoint] Postgres bootstrap complete"
}

# ---------------------------------------------------------
# Redis dir prep
# ---------------------------------------------------------
prep_redis() {
    mkdir -p /var/lib/redis
    chown redis:redis /var/lib/redis
}

# ---------------------------------------------------------
# Hand env to .NET app
# ---------------------------------------------------------
export_connection_strings() {
    export ConnectionStrings__Postgres="Host=127.0.0.1;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};Pooling=true;Maximum Pool Size=20"
    export Redis__ConnectionString="127.0.0.1:6379"
}

main() {
    init_postgres
    prep_redis
    export_connection_strings

    echo "[entrypoint] Starting supervisord..."
    exec /usr/bin/supervisord -c /etc/supervisor/supervisord.conf
}

main "$@"
