<#
.SYNOPSIS
    One-shot Azure deployment for the MentorBooking demo (Blazor Server + Azure SQL).

.DESCRIPTION
    Provisions a minimal, demo-grade Azure stack and deploys the app:
      - Resource group
      - Azure SQL Server + Database (Basic tier)
      - App Service plan (B1) + Web App (.NET 8)
      - Sets the connection string, enables WebSockets + session affinity
        (both required for Blazor Server), and deploys a published build.

    The app's DbSeeder creates the schema and seeds demo data on first startup,
    so no manual migration step is needed.

    Requirements: Azure CLI (`az`) logged in (`az login`) and the .NET 8 SDK.
    This is intentionally simple for a DEMO — no Key Vault / Managed Identity.
    The SQL admin password is stored directly in the App Service connection string.

.EXAMPLE
    ./deploy.ps1
    ./deploy.ps1 -Location westus2 -SqlAdminPassword 'My$trongPass1!'

.EXAMPLE
    # Tear everything down when the demo is over:
    az group delete -n rg-mentorbooking-demo --yes --no-wait
#>

[CmdletBinding()]
param(
    [string]$ResourceGroup = "rg-mentorbooking-demo",
    [string]$Location      = "eastus",

    # A short suffix keeps globally-unique names (app + SQL server) from colliding.
    [string]$Suffix        = -join ((48..57) + (97..122) | Get-Random -Count 6 | ForEach-Object { [char]$_ }),

    [string]$AppName       = "mentorbooking-$Suffix",
    [string]$SqlServerName = "mentorbooking-sql-$Suffix",
    [string]$DatabaseName  = "MentorBooking",
    [string]$SqlAdminUser  = "sqladmin",

    # If not supplied, a random strong password is generated for the demo.
    [string]$SqlAdminPassword,

    [string]$PlanName      = "plan-mentorbooking-demo",
    [string]$AppServiceSku = "B1",

    # Seed demo data on startup. On (default) so the first deploy creates the admin,
    # topics and demo mentors. Pass -SeedDatabase:$false for a production-style deploy
    # where you don't want the database re-seeded every time the app restarts.
    [bool]$SeedDatabase    = $true,

    # Path to the web project (relative to this script).
    [string]$ProjectPath   = "MentorBooking\MentorBooking.csproj"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Write-Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- Preflight -------------------------------------------------------------
Write-Step "Checking prerequisites"
if (-not (Get-Command az -ErrorAction SilentlyContinue))     { throw "Azure CLI (az) not found. Install it and run 'az login'." }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw "'dotnet' SDK not found." }

$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) { throw "Not logged in to Azure. Run 'az login' first." }
Write-Host "Using subscription: $($account.name) ($($account.id))"

if ([string]::IsNullOrWhiteSpace($SqlAdminPassword)) {
    # Demo-only: generate a password that satisfies Azure SQL complexity rules.
    $SqlAdminPassword = (-join ((65..90) + (97..122) + (48..57) | Get-Random -Count 16 | ForEach-Object { [char]$_ })) + "aA1!"
    Write-Host "Generated SQL admin password (save it): $SqlAdminPassword" -ForegroundColor Yellow
}

# --- Resource group --------------------------------------------------------
Write-Step "Creating resource group '$ResourceGroup'"
az group create -n $ResourceGroup -l $Location --output none

# --- Azure SQL -------------------------------------------------------------
Write-Step "Creating Azure SQL server '$SqlServerName'"
az sql server create -g $ResourceGroup -n $SqlServerName -l $Location `
    --admin-user $SqlAdminUser --admin-password $SqlAdminPassword --output none

Write-Step "Creating database '$DatabaseName' (Basic tier)"
az sql db create -g $ResourceGroup -s $SqlServerName -n $DatabaseName `
    --service-objective Basic --output none

Write-Step "Allowing Azure services to reach the SQL server"
# 0.0.0.0/0.0.0.0 is the special rule that permits Azure-internal services (the Web App).
az sql server firewall-rule create -g $ResourceGroup -s $SqlServerName `
    -n AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 --output none

# --- App Service -----------------------------------------------------------
Write-Step "Creating App Service plan '$PlanName' ($AppServiceSku, Linux)"
az appservice plan create -g $ResourceGroup -n $PlanName --sku $AppServiceSku --is-linux --output none

Write-Step "Creating Web App '$AppName' (.NET 8)"
az webapp create -g $ResourceGroup -p $PlanName -n $AppName --runtime "DOTNETCORE:8.0" --output none

Write-Step "Configuring connection string + Blazor Server requirements"
$connString = "Server=tcp:$SqlServerName.database.windows.net,1433;Database=$DatabaseName;User ID=$SqlAdminUser;Password=$SqlAdminPassword;Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=true"
az webapp config connection-string set -g $ResourceGroup -n $AppName `
    --connection-string-type SQLAzure --settings DefaultConnection="$connString" --output none

# Blazor Server needs a persistent WebSocket circuit + sticky sessions (affinity).
az webapp config set -g $ResourceGroup -n $AppName --web-sockets-enabled true --output none
az webapp update -g $ResourceGroup -n $AppName --client-affinity-enabled true --output none

# Startup database behavior. Migrations stay on so the schema is created/updated;
# seeding is controlled by -SeedDatabase so restarts don't re-seed in production.
$seedValue = $SeedDatabase.ToString().ToLower()
az webapp config appsettings set -g $ResourceGroup -n $AppName --settings `
    Database__ApplyMigrationsOnStartup=true Database__SeedOnStartup=$seedValue --output none
Write-Host "Database__SeedOnStartup set to $seedValue"

# --- Build & deploy --------------------------------------------------------
Write-Step "Publishing the app"
$publishDir = Join-Path $scriptRoot "artifacts\publish"
$zipPath    = Join-Path $scriptRoot "artifacts\mentorbooking.zip"
if (Test-Path (Join-Path $scriptRoot "artifacts")) { Remove-Item (Join-Path $scriptRoot "artifacts") -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

dotnet publish (Join-Path $scriptRoot $ProjectPath) -c Release -o $publishDir

Write-Step "Packaging and deploying"
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
az webapp deploy -g $ResourceGroup -n $AppName --src-path $zipPath --type zip --output none

# --- Done ------------------------------------------------------------------
$url = "https://$AppName.azurewebsites.net"
Write-Step "Deployment complete"
Write-Host "App URL:        $url" -ForegroundColor Green
Write-Host "SQL server:     $SqlServerName.database.windows.net"
Write-Host "SQL admin user: $SqlAdminUser"
Write-Host "SQL password:   $SqlAdminPassword" -ForegroundColor Yellow
Write-Host "`nFirst request triggers migrations + demo-data seeding (may take ~30s)."
Write-Host "Tear down with: az group delete -n $ResourceGroup --yes --no-wait" -ForegroundColor DarkGray
