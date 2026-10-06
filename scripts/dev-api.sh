#!/usr/bin/env bash
# Runs the API on http://localhost:5080 against the docker compose infrastructure (mssql, kafka).
# Usage: scripts/dev-api.sh [migrate]
set -euo pipefail
cd "$(dirname "$0")/.."
set -a; source .env; set +a
export ConnectionStrings__Shop="Server=127.0.0.1,1433;Database=Shop;User Id=sa;Password=${MSSQL_SA_PASSWORD};Encrypt=True;TrustServerCertificate=True"
export Kafka__BootstrapServers="127.0.0.1:9094"
export APP_DB_PASSWORD=""   # local dev connects as sa; the shop_app login is created only in compose/k8s
exec dotnet run --project backend/src/Shop.Api -- "$@"
