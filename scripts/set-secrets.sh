#!/bin/bash

# AccedeSimple User Secrets Configuration Script
# This script reads secrets from a file and sets them using dotnet user-secrets

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Default secrets file location
SECRETS_FILE="${1:-secrets.txt}"
PROJECT_PATH="src/AccedeSimple.AppHost"

echo -e "${GREEN}AccedeSimple User Secrets Setup${NC}"
echo "================================"
echo ""

# Check if secrets file exists
if [ ! -f "$SECRETS_FILE" ]; then
    echo -e "${RED}Error: Secrets file '$SECRETS_FILE' not found!${NC}"
    echo ""
    echo "Creating template file: $SECRETS_FILE"
    cat > "$SECRETS_FILE" << 'EOF'
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
EOF
    echo -e "${YELLOW}Template created. Please edit '$SECRETS_FILE' with your actual values and run again.${NC}"
    exit 1
fi

# Check if project exists
if [ ! -d "$PROJECT_PATH" ]; then
    echo -e "${RED}Error: Project path '$PROJECT_PATH' not found!${NC}"
    echo "Please run this script from the repository root."
    exit 1
fi

echo "Reading secrets from: $SECRETS_FILE"
echo "Setting secrets for project: $PROJECT_PATH"
echo ""

# Navigate to project directory
cd "$PROJECT_PATH"

# Counter for tracking
success_count=0
skip_count=0
error_count=0

# Read and process each line
while IFS= read -r line || [ -n "$line" ]; do
    # Skip empty lines and comments
    if [[ -z "$line" ]] || [[ "$line" =~ ^[[:space:]]*# ]]; then
        continue
    fi
    
    # Parse KEY=VALUE
    if [[ "$line" =~ ^([^=]+)=(.*)$ ]]; then
        key="${BASH_REMATCH[1]}"
        value="${BASH_REMATCH[2]}"
        
        # Trim whitespace
        key=$(echo "$key" | xargs)
        value=$(echo "$value" | xargs)
        
        # Skip if value is a placeholder
        if [[ "$value" == "your-"* ]] || [[ -z "$value" ]]; then
            echo -e "${YELLOW}⊘ Skipping '$key' (placeholder value)${NC}"
            ((skip_count++))
            continue
        fi
        
        # Set the secret
        echo -n "Setting '$key'... "
        if dotnet user-secrets set "$key" "$value" > /dev/null 2>&1; then
            echo -e "${GREEN}✓${NC}"
            ((success_count++))
        else
            echo -e "${RED}✗ Failed${NC}"
            ((error_count++))
        fi
    else
        echo -e "${YELLOW}⊘ Skipping invalid line: $line${NC}"
        ((skip_count++))
    fi
done < "../../$SECRETS_FILE"

# Summary
echo ""
echo "================================"
echo -e "${GREEN}✓ Successfully set: $success_count secrets${NC}"
if [ $skip_count -gt 0 ]; then
    echo -e "${YELLOW}⊘ Skipped: $skip_count entries${NC}"
fi
if [ $error_count -gt 0 ]; then
    echo -e "${RED}✗ Failed: $error_count entries${NC}"
fi
echo ""

if [ $success_count -gt 0 ]; then
    echo -e "${GREEN}User secrets configured successfully!${NC}"
    echo "You can now run: dotnet run"
else
    echo -e "${RED}No secrets were set. Please check your secrets file.${NC}"
    exit 1
fi
