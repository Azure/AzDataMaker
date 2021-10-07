using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzDataMaker
{
    public class ConfigHelper
    {
        private readonly IConfiguration _configuration;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ILogger<ConfigHelper> _logger;


        public ConfigHelper(ILogger<ConfigHelper> logger, 
            BlobServiceClient blobServiceClient, 
            IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
            _blobServiceClient = blobServiceClient;
        }

        /// <summary>
        /// Helper to get int values from config
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public int GetConfigValue(string key, int defaultValue)
        {
            string configValue = _configuration[key];
            int value;
            if (!int.TryParse(configValue, out value))
            {
                value = defaultValue;
            }
            _logger.LogInformation($"{key} = {configValue} - Default {defaultValue} - Used {value}");
            return value;
        }

        /// <summary>
        /// Helper to get int values from config
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public double GetConfigValue(string key, double defaultValue)
        {
            string configValue = _configuration[key];
            double value;
            if (!double.TryParse(configValue, out value))
            {
                value = defaultValue;
            }
            _logger.LogInformation($"{key} = {configValue} - Default {defaultValue} - Used {value}");
            return value;
        }

        /// <summary>
        /// Helper to get bool values from config
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        public bool GetConfigValue(string key, bool defaultValue)
        {
            string configValue = _configuration[key];
            bool value;
            if (!bool.TryParse(configValue, out value))
            {
                value = defaultValue;
            }
            _logger.LogInformation($"{key} = {configValue} - Default {defaultValue} - Used {value}");
            return value;
        }

        /// <summary>
        /// Get the target container names from the config option "BlobContainers". 
        /// 
        /// The config option can be set with either:
        /// 
        ///  - An Integer representing the number of random conatiner names to use. 
        ///    containers will be named with a GUID.
        ///    
        ///  - A comma separated list of container names to use. 
        /// </summary>
        /// <returns></returns>
        public async Task<List<BlobContainerClient>> GetTargetContainersAsync(int defultNumber, CancellationToken cancellationToken)
        {
            var blobContainers = new List<BlobContainerClient>();

            string blobContainerConfig = _configuration["BlobContainers"];
            if (!string.IsNullOrEmpty(blobContainerConfig))
            {
                int containerCount = 0;
                if (int.TryParse(blobContainerConfig, out containerCount))
                {
                    // If a number was specified create that many container names
                    _logger.LogInformation($"Creating {containerCount} Containers");
                    for (int i = 0; i < containerCount; i++)
                    {
                        blobContainers.Add(_blobServiceClient.GetBlobContainerClient(Guid.NewGuid().ToString().ToLower()));
                    }
                }
                else
                {
                    // If container names were specified use them
                    _logger.LogInformation($"Creating Containers named {blobContainerConfig}");
                    foreach (var name in blobContainerConfig.Split(",").Select(x => x.Trim()).Distinct())
                    {
                        blobContainers.Add(_blobServiceClient.GetBlobContainerClient(name));
                    }
                }
            }
            else
            {
                // create the default number of conatiners
                _logger.LogInformation($"Creating Default {defultNumber} Containers");
                for (int i = 0; i < defultNumber; i++)
                {
                    blobContainers.Add(_blobServiceClient.GetBlobContainerClient(Guid.NewGuid().ToString().ToLower()));
                }
            }

            //Ensure all the containers exist
            foreach (var container in blobContainers)
            {
                await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }

            return blobContainers;
        }
    }
}
