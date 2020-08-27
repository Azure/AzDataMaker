param
(
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $SubscriptionId,

    # eastasia, southeastasia, centralus, eastus, eastus2, westus, northcentralus, southcentralus, northeurope, westeurope, japanwest, japaneast, brazilsouth, australiaeast, australiasoutheast
    # southindia, centralindia, westindia, canadacentral, canadaeast, uksouth, ukwest, westcentralus, westus2, koreacentral, koreasouth, francecentral, francesouth, australiacentral, 
    # australiacentral2, uaecentral, uaenorth, southafricanorth, southafricawest, switzerlandnorth, switzerlandwest, germanynorth, germanywestcentral, norwaywest, norwayeast
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $Region, 
  
    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $ResourceGroup,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $TargetStorageAccountName,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $FileCount,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $MaxFileSize,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $MinFileSize,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $ReportStatusIncrement,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $BlobContainers,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [bool] $RandomFileContents,    

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [String] $ContainerRegistry,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $InstanceCount,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $AmountOfCoresPerContainer,

    [Parameter(Mandatory = $True, valueFromPipeline=$True)]
    [int] $AmountOfMemoryPerContainer
)

$Env:AZURE_CORE_ONLY_SHOW_ERRORS = $True
$Env:AZURE_CORE_OUTPUT = "tsv"

# Set defaults in case not provided
if ($FileCount -eq 0) { $FileCount = 100 }
if ($MaxFileSize -eq 0) { $MaxFileSize = 100 }
if ($MinFileSize -eq 0) { $MinFileSize = 4 }
if ($ReportStatusIncrement -eq 0) { $ReportStatusIncrement = 1000 }
if ($BlobContainers -eq 0) { $BlobContainers = 5 }
if ($AmountOfCoresPerContainer -eq 0) { $AmountOfCoresPerContainer = 2 }
if ($AmountOfMemoryPerContainer -eq 0) { $AmountOfMemoryPerContainer = 4 }

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

# Request authentication information from container registry
$ContainerRegistryLoginServer = `
az acr show `
    --name $ContainerRegistry `
    --query loginServer

$ContainerRegistryUsername = `
az acr credential show `
    --name $ContainerRegistry `
    --query username

$ContainerRegistryPassword = `
az acr credential show `
    --name $ContainerRegistry `
    --query passwords[0].value
    
# Create the storage account (if needed)
$TargetStorageAccountAvailable = `
az storage account check-name `
    --name $TargetStorageAccountName `
    --query "nameAvailable"

if ($TargetStorageAccountAvailable)
{
    ## nobody has this account, create it 
    az storage account create `
        --name $TargetStorageAccountName `
        --sku Standard_LRS
}
else {    

    $TargetStorageAccountsInResourceGroup = `
    az storage account list `
        -g $ResourceGroup `
        --query "[?name=='$TargetStorageAccountName'] | length(@)"

        if ($TargetStorageAccountsInResourceGroup -ne 1)
        {
            ## somebody has this account and it is not me
            Write-Error -Message "The storage account name is already taken by someone else."
            exit
        }
        else {
            Write-Warning -Message "Using existing target storage account, data might be overwritten."
        }

}

# Request authentication information from storage account
$TargetStorageAccountConnectionString = `
az storage account show-connection-string `
    --name $TargetStorageAccountName

$RunningContainers = `
@(az container list `
    --query [*].name `
    | Where-Object { $_ -Like "azdatamaker-instance-*" } `
    | Sort-Object -Descending { $_ })

if ($RunningContainers.Length -eq $InstanceCount) 
{
    Write-Output "Not starting new instances, already running $($InstanceCount) instance(s)"
    Write-Output $RunningContainers
}
else 
{
    if ($RunningContainers.Length -lt $InstanceCount) 
    {
        # Deploy Container Instance(s)
        Write-Output "Starting $($InstanceCount - $RunningContainers.Length) instance(s)"

        for($InstanceCounter = $RunningContainers.Count; $InstanceCounter -lt $InstanceCount; $InstanceCounter++)
        {
            $ContainerName = "azdatamaker-instance-$(($InstanceCounter + 1).ToString("000"))"

            Write-Output "$($ContainerName)"

            # Create the container
            az container create `
                --name $ContainerName `
                --cpu $AmountOfCoresPerContainer `
                --memory $AmountOfMemoryPerContainer `
                --registry-login-server $ContainerRegistryLoginServer `
                --registry-username $ContainerRegistryUsername `
                --registry-password $ContainerRegistryPassword `
                --image "$($ContainerRegistryLoginServer)/azdatamaker:latest" `
                --restart-policy Never `
                --environment-variables `
                    FileCount=$FileCount `
                    MaxFileSize=$MaxFileSize `
                    MinFileSize=$MinFileSize `
                    ReportStatusIncrement=$ReportStatusIncrement `
                    BlobContainers=$BlobContainers `
                    RandomFileContents=$RandomFileContents `
                    ConnectionStrings__MyStorageConnection=$TargetStorageAccountConnectionString `
                --no-wait
        }
    }
    else 
    {
        # Terminate Container Instance(s)
        Write-Output "Stopping $($RunningContainers.Length - $InstanceCount) instance(s)"

        for($InstanceCounter = 0; $InstanceCounter -lt ($RunningContainers.Length - $InstanceCount); $InstanceCounter++) 
        {
            Write-Output "$($RunningContainers[$InstanceCounter])"

            az container delete `
                --name $RunningContainers[$InstanceCounter] `
                --yes
        }
    }
}

