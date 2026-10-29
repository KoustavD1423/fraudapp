param(
    [string]$SqlConnectionString =
        $env:FRAUD_TEST_SQLSERVER,

    [string]$RedisConnectionString =
        $env:FRAUD_TEST_REDIS
)

# Use local development defaults when values aren't supplied.
if ([string]::IsNullOrWhiteSpace($SqlConnectionString)) {
    $SqlConnectionString =
        "Server=localhost\SQLEXPRESS;Database=FraudDb;Trusted_Connection=True;TrustServerCertificate=True"
}

if ([string]::IsNullOrWhiteSpace($RedisConnectionString)) {
    $RedisConnectionString = "localhost:6379"
}

# Integration-test settings.
$env:FRAUD_TEST_SQLSERVER = $SqlConnectionString
$env:FRAUD_TEST_REDIS = $RedisConnectionString

# Standard .NET application configuration settings.
$env:ConnectionStrings__DefaultConnection = $SqlConnectionString
$env:ConnectionStrings__Redis = $RedisConnectionString

Write-Host "`nRunning FraudPlatform tests..." -ForegroundColor Cyan
Write-Host "SQL Server connection: configured"
Write-Host "Redis connection: configured"

# Run the test project.
dotnet test .\Fraud.Tests\Fraud.Tests.csproj

# Preserve the test command's exit code.
exit $LASTEXITCODE