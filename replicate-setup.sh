#!/bin/bash

# SETUP
# Modify the values of these environment variables if you wish to use different names for the resources 
# Or different target and source cloud environments.
TargetAzureEnvironmentName=AzureChinaCloud
TargetResourceGroupName=chinaResourceGroup
TargetRegion=chinanorth2
TargetACRName=chinaNorth2Registry
ServicePrincipalName=acr-publish-artifacts-service-principal

SourceAzureEnvironmentName=AzureCloud
SourceResourceGroupName=publicResourceGroup
SourceRegion=westus
SourceACRName=publicWestUsRegistry
FunctionAppStorageName=replicatefunctionstorage
FunctionAppName=replicateFunction
ConfigKeyVaultName=replicateAppConfigVault

# EXECUTION

# switch to the target cloud
az cloud set --name $TargetAzureEnvironmentName

# Login to the target Cloud
az login

# Create a resource group in the target cloud
az group create --name $TargetResourceGroupName --location $TargetRegion

# Create a container registry in the target cloud
az acr create  \
 --resource-group $TargetResourceGroupName \ 
 --name $TargetACRName \
 --sku Premium \
 --location $TargetRegion

 # Create a custom role
 az role definition create --role-definition @replicate-role.json

 # Create a service principal
# Obtain the full registry ID for subsequent command args
TargetACRResourceId=$(az acr show --name $TargetACRName --query id --output tsv)

# Create the service principal with AcrReplicate role on the registry.
TargetAzureServicePrincipalClientKey=$(az ad sp create-for-rbac --name http://$ServicePrincipalName --scopes $TargetACRResourceId --role AcrReplicate --query password --output tsv)

# Obtain the service principal app ID for subsequent steps
TargetAzureServicePrincipalClientId=$(az ad sp show --id http://$ServicePrincipalName --query appId --output tsv)

# Obtain the service principal tenant ID for subsequent steps
TargetAzureServicePrincipalTenantId=$(az ad sp show --id http://$ServicePrincipalName --query appOwnerTenantId --output tsv)

# Switch to the Source cloud
az cloud set --name $SourceAzureEnvironmentName

# Login to the Source Cloud
az login

# Create a resource group in the source cloud
az group create --name $SourceResourceGroupName --location $SourceRegion

# Create a container registry in the source cloud
az acr create \
    --resource-group $SourceResourceGroupName \
    --name $SourceACRName \
    --sku Premium \
    --location $SourceRegion

# Create a pull scoped token
SourceACRPullTokenName=pulltoken
SourceACRPullTokenPassword=$(az acr token create --name $SourceACRPullTokenName --registry $SourceACRName --scope-map _repositories_pull --query credentials.passwords[0].value -o tsv)

# Create an Azure Function app
az storage account create --name $FunctionAppStorageName --location $SourceRegion --resource-group $SourceResourceGroupName --sku Standard_LRS

FunctionAppObjectId=$(az functionapp create -n $FunctionAppName --storage-account $storageName --consumption-plan-location $SourceRegion --runtime dotnet -g $SourceResourceGroupName --functions-version 3 --assign-identity [system] --query identity.principalId -o tsv)

# Build and Publish the function app project to a Zip file
dotnet publish -c Release -o publishFolder

cd publishFolder/

zip -r ../acr-replicate-app.zip ./*

# Deploy the function project to Azure Function app
az functionapp deployment source config-zip \ 
   -g $SourceResourceGroupName  \
   -n $FunctionAppName \
   --src acr-replicate-app.zip

# Create a keyvault
az keyvault create \
   --name $ConfigKeyVaultName \
   -g $SourceResourceGroupName \
    --location $SourceRegion

# Assign the access policy
az keyvault set-policy \
   -n $ConfigKeyVaultName \
   -g $SourceResourceGroupName \
   --object-id $FunctionAppObjectId \
   --secret-permissions get list

# Create secrets for app settings
az keyvault secret set -n TargetAzureServicePrincipalClientKey \
    --vault-name$ConfigKeyVaultName \
    --value $TargetAzureServicePrincipalClientKey

az keyvault secret set -n SourceACRPullTokenPassword \
   --vault-name $ConfigKeyVaultName \
   --value $SourceACRPullTokenPassword

# Set Application Settings for Function App
ConfigKeyVaultUri=$(az keyvault show -n $ConfigKeyVaultName -g $SourceResourceGroupName --query properties.vaultUri -o tsv)

az functionapp config appsettings set -n $FunctionAppName \
   -g $SourceResourceGroupName \
   --settings "TargetAzureEnvironmentName=$TargetAzureEnvironmentName" "TargetAzureServicePrincipalClientId=$TargetAzureServicePrincipalClientId" "TargetAzureServicePrincipalClientKey=@Microsoft.KeyVault(SecretUri=${ConfigKeyVaultUri%?}/secrets/TargetAzureServicePrincipalClientKey/)" "TargetAzureServicePrincipalTenantId=$TargetAzureServicePrincipalTenantId" "TargetACRResourceId=$TargetACRResourceId" "SourceACRPullTokenName=$SourceACRPullTokenName" "SourceACRPullTokenPassword=@Microsoft.KeyVault(SecretUri=${ConfigKeyVaultUri%?}/secrets/SourceACRPullTokenPassword/)"

# Subscribe to registry events
# Obtain the source registry Id to set as the source for event grid
SourceACRResourceId=$(az acr show --name $SourceACRName --query id --outputtsv)

# Obtain the resource Id of the function app to set as the endpoint for the event grid.
FunctionAppResourceId=$(az functionapp show --name $FunctionAppName --queryid -g $SourceResourceGroupName --output tsv)

# Create an event grid subscription.
az eventgrid event-subscription create \
    --name replicateAppSub \
    --source-resource-id $SourceACRResourceId \
    --endpoint $FunctionAppResourceId/functions/Function1 \
    --endpoint-type azurefunction

# Trigger registry events

az acr build --registry $SourceACRName \
  --image myimage:v1 \
  -f Dockerfile https://github.com/Azure-Samples/acr-build-helloworld-node.git#main