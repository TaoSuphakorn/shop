# Builds and starts the whole system with one command, ready to use at http://localhost:3000
# Usage (from the repository root):  powershell -ExecutionPolicy Bypass -File scripts/up.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function New-Secret([int]$bytes) {
    $buffer = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($buffer)
    return $buffer
}

# First run only: generate the secrets. .env is git-ignored and is never overwritten, because the
# SQL Server password is fixed when its data volume is first created.
if (-not (Test-Path .env)) {
    $hex = { param($b) -join ($b | ForEach-Object { $_.ToString('x2') }) }
    @(
        "MSSQL_SA_PASSWORD=Aa1!$(& $hex (New-Secret 16))"     # prefix satisfies SQL Server's complexity rule
        "APP_DB_PASSWORD=Bb2!$(& $hex (New-Secret 16))"
        "CART_SIGNING_KEY=$([Convert]::ToBase64String((New-Secret 32)))"
    ) | Set-Content -Path .env -Encoding ascii
    Write-Host 'Created .env with new random secrets.'
}

docker compose --profile app up --build -d
if ($LASTEXITCODE -ne 0) { throw 'docker compose failed. Is Docker Desktop running?' }

Write-Host 'Waiting for the shop to answer...'
$deadline = (Get-Date).AddMinutes(5)
while ($true) {
    try {
        $status = (Invoke-WebRequest -Uri http://localhost:3000/api/products -UseBasicParsing -TimeoutSec 5).StatusCode
        if ($status -eq 200) { break }
    } catch { }
    if ((Get-Date) -gt $deadline) { throw 'The shop did not start within 5 minutes. Check: docker compose --profile app logs' }
    Start-Sleep -Seconds 3
}

Write-Host ''
Write-Host 'Ready: http://localhost:3000'
Write-Host 'Stop:  docker compose --profile app down        (add -v to also delete the database)'
