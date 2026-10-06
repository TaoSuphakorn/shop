# Runs the API on http://localhost:5080 against the docker compose infrastructure (mssql, kafka).
# Usage: scripts/dev-api.ps1 [migrate]
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

# Load .env (KEY=VALUE per line) into this process only.
Get-Content .env | Where-Object { $_ -match '^\s*[^#\s][^=]*=' } | ForEach-Object {
    $name, $value = $_ -split '=', 2
    Set-Item -Path "Env:$($name.Trim())" -Value $value
}

$env:ConnectionStrings__Shop = "Server=127.0.0.1,1433;Database=Shop;User Id=sa;Password=$($env:MSSQL_SA_PASSWORD);Encrypt=True;TrustServerCertificate=True"
$env:Kafka__BootstrapServers = '127.0.0.1:9094'
$env:APP_DB_PASSWORD = ''   # local dev connects as sa; the shop_app login is created only in compose/k8s

dotnet run --project backend/src/Shop.Api -- @args
