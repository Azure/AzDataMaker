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

## Repository Content

| File/folder | Description |
|-------------|-------------|
| `src`   | This folder contains the AzDataMaker Core Module |

## Deploying AzDataMaker

Shared parameters. Run this each time you start a new console session.

``` bash
# login
az login

# show the current subscriptions
az account list --all -o table

# change the default subscription (if needed)
az account set -s "My Subscription Name"

# the region you want to deploy to 
# this should match the region the storage account is in if you already have a storage account
REGION="southcentralus"

# the name of the resource group that you want to deploy the resources to
# the sample assumes everything is in the same resource group, your situation might differ
RG="testrg"

# the name of the storage account you want to load data to
STORAGEACCT="testacct"

# the name you want to use for your Azure Container Registry
ACR="testacr"

# the name prefix you want to use for the ACI instance(s)
ACIPREFIX="testaci"

# the number of ACI instances you want to use
ACICOUNT=5
```

Deploy the RG, the ACR and build and publish the container to ACR

Run this once to seploy the resources the Data Maker needs. If you already have some of these resources you can skip some of these steps

``` bash
# Create the Resource Group
az group create -n $RG -l $REGION

# Create Storage Account if you dont already have one
az storage account create --name $STORAGEACCT --access-tier Hot --kind StorageV2 --sku Standard_LRS --https-only true -g $RG -l $REGION

# Create Container Registry
az acr create --name $ACR --admin-enabled true --sku Standard -g $RG -l $REGION

# Package the sample into a docker container and publish it to ACR
# PICK ONE OPTION

# Here we are building with the published sample code directly from GitHub
az acr build -g $RG -r $ACR https://github.com/Azure/azdatamaker.git -f src/AzDataMaker/AzDataMaker/Dockerfile --image azdatamaker:latest

# To build using a copy of the code you downloaded use this command
# You will need to be in the AzDataMaker directory for this to work
az acr build -g $RG -r $ACR . -f src/AzDataMaker/AzDataMaker/Dockerfile --image azdatamaker:latest
```

Create instances of the running app.

Environment Variables
> Omit options to take the default
- **FileCount**: the number of files you want to create. This is the number that you want a given ACR instance to create. So if you have 1 instance and want 100 files, use 100. On the other hand, if you have 5 instances and want 100 files, use 20. int, default 100
- **MaxFileSize**: the max size of the files to create in MiB. double, default 100
- **MinFileSize**: the min size of the files you want to create in MiB. double, default 4
- **ReportStatusIncrement**: after how many files should the application report a status update to the console. If you are creating small files you want to use a larger number to reduce the performance impact of status updates. If you are creating larger files you can use a smaller number. int, default 1000
- **RandomFileContents**: should the objects be filled with all 0's (false) or all random bytes (true). Creating random bytes does require more cpu. bool, default false
- **Threads**: the number of threads to use. int, default 2x the number of cpu cores
- **BlobContainers**: the storage containers to create the objects in, round robin style 
  - If an number is provided, we will create that many containers using GUIDs as the container names. 
  - If a comma separated list of names is provided, we will use those names
  - If no value is specified then we will create 5 containters with guids as names.
- **ConnectionStrings__MyStorageConnection**: the connection string to the storage account you want the files created in 


``` bash
# Request authentication information from container registry
ACRSVR="$(az acr show --name $ACR -g $RG --query loginServer -o tsv)"
ACRUSER="$(az acr credential show --name $ACR -g $RG --query username  -o tsv)"
ACRPWD="$(az acr credential show --name $ACR -g $RG --query passwords[0].value -o tsv)"

# Request authentication information from the storage account
STORAGEACCTCS="$(az storage account show-connection-string --name $STORAGEACCT -g $RG -o tsv)"

# Find the number of currently running instances
MAXACI=$(az container list -g $RG --query "max([?starts_with(name, '$ACIPREFIX-')].name)" -o tsv)
if [ -z "$MAXACI" ]; then MAXACI=0; else MAXACI=${MAXACI:$(expr length "$ACIPREFIX")+1:$(expr length "$MAXACI")-$(expr length "$ACIPREFIX")-1}; fi

for ((x=MAXACI+1; x<=$ACICOUNT ; x++)); 
do 
{ 
    ACINAME="$(printf -v x %02d $x; echo "$ACIPREFIX-$x";)"
    echo "Create $ACINAME"
    az container create \
        --name "$ACINAME" \
        --resource-group $RG \
        --location $REGION \
        --cpu 1 \
        --memory 1 \
        --registry-login-server $ACRSVR \
        --registry-username $ACRUSER \
        --registry-password $ACRPWD \
        --image "$ACRSVR/azdatamaker:latest" \
        --restart-policy Never \
        --no-wait \
        --environment-variables \
            FileCount="" \
            MaxFileSize="" \
            MinFileSize="" \
            ReportStatusIncrement="" \
            BlobContainers="" \
            RandomFileContents="" \
            Threads="" \
        --secure-environment-variables \
            ConnectionStrings__MyStorageConnection=$STORAGEACCTCS 
} 
done

# Find the number of currently running instances
MAXACI=$(az container list -g $RG --query "max([?starts_with(name, '$ACIPREFIX-')].name)" -o tsv)
if [ -z "$MAXACI" ]; then MAXACI=0; else MAXACI=${MAXACI:$(expr length "$ACIPREFIX")+1:$(expr length "$MAXACI")-$(expr length "$ACIPREFIX")-1}; fi

# Remove Instances if needed
for ((x=MAXACI ; x>$ACICOUNT ; x--)); 
do 
{ 
    ACINAME="$(printf -v x %02d $x; echo "$ACIPREFIX-$x";)"
    echo "Delete $ACINAME"
    az container delete \
        --name "$ACINAME" \
        --resource-group $RG \
        --yes
} 
done
```

## Tips
- To reduce memory consumption the application creates all files on disk first and then uploads them. This can create lots of IO, and depending on the size files you want to create and number of threads you are using, will consume lots of local disk space. When creating larger files reduce the number of threads used. In the above examples we deploy to ACI, consider deploying to an environment with more local disk space if very large files are required.


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
