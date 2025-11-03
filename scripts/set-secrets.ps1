# AccedeSimple User Secrets Configuration Script
# This script reads secrets from a file and sets them using dotnet user-secrets

param(
    [string]$SecretsFile = "secrets.txt"
)

$ErrorActionPreference = "Stop"

# Default locations
$ProjectPath = "src\AccedeSimple.AppHost"

# Colors for output
function Write-ColorOutput {
    param(
        [string]$Message,
        [string]$Color = "White"
    )
    Write-Host $Message -ForegroundColor $Color
}

Write-ColorOutput "AccedeSimple User Secrets Setup" "Green"
Write-ColorOutput "================================" "Green"
Write-Host ""

# Check if secrets file exists
if (-not (Test-Path $SecretsFile)) {
    Write-ColorOutput "Error: Secrets file '$SecretsFile' not found!" "Red"
    Write-Host ""
    Write-Host "Creating template file: $SecretsFile"
    
    $template = @"
# AccedeSimple User Secrets Configuration
# Format: SECRET_NAME=SECRET_VALUE
# Lines starting with # are ignored

# Azure OpenAI Configuration
AzureOpenAI:ResourceGroup=your-resource-group
AzureOpenAI:ResourceName=your-openai-resource-name
AzureOpenAI:Endpoint=https://your-openai.openai.azure.com/

# Azure Subscription Configuration
Azure:SubscriptionId=your-subscription-id
Azure:ResourceGroup=your-resource-group
Azure:Location=eastus
Azure:AllowResourceGroupCreation=false

# Azure AI Foundry Configuration
AzureAIFoundry:Project=your-ai-foundry-project
"@
    
    Set-Content -Path $SecretsFile -Value $template
    Write-ColorOutput "Template created. Please edit '$SecretsFile' with your actual values and run again." "Yellow"
    exit 1
}

# Check if project exists
if (-not (Test-Path $ProjectPath)) {
    Write-ColorOutput "Error: Project path '$ProjectPath' not found!" "Red"
    Write-Host "Please run this script from the repository root."
    exit 1
}

Write-Host "Reading secrets from: $SecretsFile"
Write-Host "Setting secrets for project: $ProjectPath"
Write-Host ""

# Save current location and navigate to project
Push-Location $ProjectPath

# Counters
$successCount = 0
$skipCount = 0
$errorCount = 0

# Read and process each line
Get-Content $SecretsFile | ForEach-Object {
    $line = $_.Trim()
    
    # Skip empty lines and comments
    if ([string]::IsNullOrWhiteSpace($line) -or $line.StartsWith("#")) {
        return
    }
    
    # Parse KEY=VALUE
    if ($line -match '^([^=]+)=(.*)$') {
        $key = $matches[1].Trim()
        $value = $matches[2].Trim()
        
        # Skip if value is a placeholder or empty
        if ([string]::IsNullOrWhiteSpace($value) -or $value.StartsWith("your-")) {
            Write-ColorOutput "⊘ Skipping '$key' (placeholder value)" "Yellow"
            $script:skipCount++
            return
        }
        
        # Set the secret
        Write-Host "Setting '$key'... " -NoNewline
        try {
            $output = dotnet user-secrets set $key $value 2>&1
            if ($LASTEXITCODE -eq 0) {
                Write-ColorOutput "✓" "Green"
                $script:successCount++
            } else {
                Write-ColorOutput "✗ Failed" "Red"
                $script:errorCount++
            }
        } catch {
            Write-ColorOutput "✗ Failed" "Red"
            $script:errorCount++
        }
    } else {
        Write-ColorOutput "⊘ Skipping invalid line: $line" "Yellow"
        $script:skipCount++
    }
}

# Return to original location
Pop-Location

# Summary
Write-Host ""
Write-Host "================================"
Write-ColorOutput "✓ Successfully set: $successCount secrets" "Green"
if ($skipCount -gt 0) {
    Write-ColorOutput "⊘ Skipped: $skipCount entries" "Yellow"
}
if ($errorCount -gt 0) {
    Write-ColorOutput "✗ Failed: $errorCount entries" "Red"
}
Write-Host ""

if ($successCount -gt 0) {
    Write-ColorOutput "User secrets configured successfully!" "Green"
    Write-Host "You can now run: dotnet run --project $ProjectPath"
    exit 0
} else {
    Write-ColorOutput "No secrets were set. Please check your secrets file." "Red"
    exit 1
}
