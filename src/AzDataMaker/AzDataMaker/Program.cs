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
                    services.AddSingleton<Random>();

                    services.AddSingleton(x =>
                    {
                        return new BlobServiceClient(hostContext.Configuration.GetConnectionString("MyStorageConnection"));
                    });

                    services.AddHostedService<Worker>();
                });
    }
}
