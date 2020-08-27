param
(
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $SubscriptionId,

    # eastasia, southeastasia, centralus, eastus, eastus2, westus, northcentralus, southcentralus, northeurope, westeurope, japanwest, japaneast, brazilsouth, australiaeast, australiasoutheast
    # southindia, centralindia, westindia, canadacentral, canadaeast, uksouth, ukwest, westcentralus, westus2, koreacentral, koreasouth, francecentral, francesouth, australiacentral, 
    # australiacentral2, uaecentral, uaenorth, southafricanorth, southafricawest, switzerlandnorth, switzerlandwest, germanynorth, germanywestcentral, norwaywest, norwayeast
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [AllowEmptyString()]
    [String] $Region, 
  
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [AllowEmptyString()]
    [String] $ResourceGroup,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $SourcePath,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [AllowEmptyString()]
    [String] $ContainerRegistry
)

$Env:AZURE_CORE_ONLY_SHOW_ERRORS = $True
$Env:AZURE_CORE_OUTPUT = "tsv"

$RandomName = -join ((48..57) + (97..122) | Get-Random -Count 24 | % {[char]$_})

# Set defaults in case not provided
if ($SourcePath -eq $null -or $SourcePath -eq "") { $SourcePath = "./src/AzDataMaker/" }
if ($Region -eq $null -or $Region -eq "") { $Region = "westeurope" }
if ($ResourceGroup -eq $null -or $ResourceGroup -eq "") { $ResourceGroup = $RandomName }
if ($ContainerRegistry -eq $null -or $ContainerRegistry -eq "") { $ContainerRegistry = $RandomName }

# Authenticate to Azure and target the appropriate subscription
az login

# Target the appropriate subscription
az account set `
    --subscription $SubscriptionId

# Set Region and ResourceGroup as script default
az configure `
    --defaults `
        location=$Region `
        group=$ResourceGroup 

# Create Resource Group
az group create `
    --name $ResourceGroup

# Create Container Registry
az acr create `
    --name $ContainerRegistry `
    --admin-enabled true `
    --sku Standard

# Build Container
az acr build $SourcePath `
    --registry $ContainerRegistry `
    --file "$($SourcePath)/AzDataMaker/Dockerfile" `
    --image azdatamaker:latest    

# Output used variables
Write-Output ""
Write-Output "SubscriptionId: $($SubscriptionId)"
Write-Output "Region: $($Region)"
Write-Output "ResourceGroup: $($ResourceGroup)"
Write-Output "ContainerRegistry: $($ContainerRegistry)"