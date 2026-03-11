using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Azure.Storage.Blobs;
using System.Reflection.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Azure;
using Azure.Identity;

namespace AzDataMaker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddSingleton<ConfigHelper>();

                    services.AddSingleton(x =>
                    {
                        var connectionString = hostContext.Configuration.GetConnectionString("MyStorageConnection");
                        if (!string.IsNullOrWhiteSpace(connectionString))
                        {
                            return new BlobServiceClient(connectionString.Trim());
                        }

                        var storageAccountUri = hostContext.Configuration["StorageAccountUri"];
                        if (!string.IsNullOrWhiteSpace(storageAccountUri))
                        {
                            if (!Uri.TryCreate(storageAccountUri.Trim(), UriKind.Absolute, out var storageAccountUriResult))
                            {
                                throw new InvalidOperationException(
                                    "The 'StorageAccountUri' configuration value is not a valid absolute URI. " +
                                    "Expected format: https://<account>.blob.core.windows.net/.");
                            }

                            return new BlobServiceClient(storageAccountUriResult, new DefaultAzureCredential());
                        }

                        throw new InvalidOperationException(
                            "Storage account configuration is missing. " +
                            "Provide either 'ConnectionStrings__MyStorageConnection' (connection string) " +
                            "or 'StorageAccountUri' (for Managed Identity authentication).");
                    });

                    services.AddHostedService<Worker>();
                });
    }
}
