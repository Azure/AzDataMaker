#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /app
COPY . ./

WORKDIR /app/src/AzDataMaker/AzDataMaker
RUN dotnet publish -c Release -o out -r linux-musl-x64 --self-contained false 

FROM mcr.microsoft.com/dotnet/aspnet:5.0-alpine-amd64
WORKDIR /app
COPY --from=build /app/src/AzDataMaker/AzDataMaker/out ./
ENTRYPOINT ["dotnet", "AzDataMaker.dll"]