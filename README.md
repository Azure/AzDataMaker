---
page_type: sample
name: AzDataMaker
topic: sample
description: |
  It is a sample .NET Core app that runs in a Linux Azure Container Instance that generates files and uploads them to Azure Blob Storage.
languages:
  - csharp
products:
  - azure
  - azure-blob-storage
urlFragment: azdatamaker
---

# AzDataMaker

The goal of this app is to generate files and upload them to Azure Blob Storage. It is a .NET Core app that runs in a Linux Azure Container Instance. You can configure the deployment script to start as many instances as your target storage account can take.

> NOTE: AzCopy has a "benchmark" feature, [more info here](https://docs.microsoft.com/azure/storage/common/storage-ref-azcopy-bench). AzCopy Benchmark is designed to run a performance benchmark by uploading or downloading test data to or from a specified destination. We recommend using AzCopy for running performance benchmarks. This sample is designed to create massive quantities of data, leveraging the horizontal scale of the Azure Container Instance service.

## Repository Contents

| File/folder | Description |
|-------------|-------------|
| `deploy` | This folder contains sample deployment scripts  |
| `src`   | This folder contains the AzDataMaker Core Module |

## Deploying AzDataMaker

Shared parameters for both scripts.

``` powershell
$subscriptionId="guid" # the GUID of your subscription
$regionName="southcentralus" # the Azure region you want to deploy to
$resourceGroup="rg" # the name of your resource group, unique in your subscription
$storageAccount="sa" # the name of the storage account you want to create, globally unique, just the name not the URL.
$acrName="acr" # the name of the container registry you want to create, globally unique, just the name not the URL
```

Deploy the RG, the ACR and build and publish the container to ACR

``` powershell
./deploy/cli/Provision-Infrastructure.ps1 `
    -SubscriptionId $subscriptionId `
    -Region $regionName `
    -ResourceGroup $resourceGroup `
    -SourcePath "./src/AzDataMaker/" `
    -ContainerRegistry $acrName 
```

Create the target storage account (if needed), deploy/destroy instances of the running app.

``` powershell
./deploy/cli/Deploy-AzDataMaker.ps1 `
    -SubscriptionId $subscriptionId `
    -Region $regionName `
    -ResourceGroup $resourceGroup `
    -TargetStorageAccountName $storageAccount <# Target storage account for the files to get uploaded to #> `
    -FileCount 100 <# How many files should we create per instance #> `
    -MaxFileSize 100 <# Max File Size (in MB) #> `
    -MinFileSize 4 <# Min File Size (in MB) #> `
    -ReportStatusIncrement 1000 <# How often should I log progress on the upload (in num of files)? #> `
    -BlobContainers 1 <# Number of containers to spray files across (per instance) #> `
    -RandomFileContents $TRUE <# Randomize File Contents (true/false) #> `
    -ContainerRegistry $acrName <# the ACR that the container was published to #> `
    -InstanceCount 0 <# how many instances should we have, it will add/removed based on the number currently deployed #> `
    -AmountOfCoresPerContainer 1 <# the number of cores for the running container #> `
    -AmountOfMemoryPerContainer 1 <# the GB of memory for the running container #>

```

## Contributing

This project welcomes contributions and suggestions.  Most contributions require you to agree to a
Contributor License Agreement (CLA) declaring that you have the right to, and actually do, grant us
the rights to use your contribution. For details, visit https://cla.opensource.microsoft.com.

When you submit a pull request, a CLA bot will automatically determine whether you need to provide
a CLA and decorate the PR appropriately (e.g., status check, comment). Simply follow the instructions
provided by the bot. You will only need to do this once across all repos using our CLA.

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/).
For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or
contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.
