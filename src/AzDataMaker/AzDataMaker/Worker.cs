using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AzDataMaker
{
    public class Worker : BackgroundService
    {
        private readonly string _name;
        private readonly ILogger<Worker> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly IConfiguration _configuration;
        private readonly BlobServiceClient _blobServiceClient;
      

        public Worker(ILogger<Worker> logger,
            IHostApplicationLifetime hostApplicationLifetime,
            IConfiguration configuration,
            BlobServiceClient blobServiceClient)
        {
            _name = this.GetType().Name;
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _configuration = configuration;
            _blobServiceClient = blobServiceClient;
        }


        #region Background Service Overrides

        /// <summary>
        /// Override the Start method on the background service to log start time
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{_name} Worker Starting at: {DateTimeOffset.UtcNow}");
            await base.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Override the execute method on the background service to wire in logging and gracefull shutdown
        /// NOTE: this calls our Run method where the work happens
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            cancellationToken.Register(() =>
            {
                _logger.LogInformation($"{_name} Worker Cancelling at: {DateTimeOffset.UtcNow}");
            });

            try
            {
                _logger.LogInformation($"{_name} Worker Running at: {DateTimeOffset.UtcNow}");

                await RunAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation($"{_name} Worker Canceled at: {DateTimeOffset.UtcNow}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"{_name} Worker Unhandled Exception at: {DateTimeOffset.UtcNow}");

            }
            finally
            {
                _hostApplicationLifetime.StopApplication();
            }
        }


        /// <summary>
        /// Override the stop method of the background service to wire in logging and gracefull shutdown
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation($"{_name} Worker Ending at: {DateTimeOffset.UtcNow}");

            await base.StopAsync(cancellationToken);
        }
        #endregion


        /// <summary>
        /// Do the Work of creating the file and uploading it to Azure
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task RunAsync(CancellationToken cancellationToken)
        {
            Random random = new Random();
            SemaphoreSlim slim = null;
            MD5 md5 = MD5.Create();

            // Target containers to round robin over
            var blobContainerClients = await GetTargetContainersAsync(5, cancellationToken);
            int containerCount = blobContainerClients.Count();

            // How many files should we create
            int fileCount = GetConfigValue("FileCount", 100);

            // How many threads should we use
            slim = new SemaphoreSlim(GetConfigValue("Threads", Environment.ProcessorCount * 2));

            // How many files have we created
            int completedFiles = 0;
            long completedMB = 0;

            // Max File Size (in MB)
            int maxFileSize = GetConfigValue("MaxFileSize", 100);

            // Min File Size (in MB)
            int minFileSize = GetConfigValue("MinFileSize", 4);

            // How often should I log progress on the upload (in num of files)?
            int statusIncrement = GetConfigValue("ReportStatusIncrement", 1000);

            bool randomFileContents = GetConfigValue("RandomFileContents", false);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _logger.LogInformation("Processing Starting");

            var uploads = new List<Task>();

            for (int fileNum = 0; fileNum < fileCount; fileNum++)
            {

                slim.Wait();

                int blobContainerIndex = (fileCount - fileNum) % containerCount;
                string fileName = $"{Guid.NewGuid().ToString().ToLower()}.dat";
                int fileSizeInMb = random.Next(minFileSize, maxFileSize);
                long fileSizeInBytes = fileSizeInMb * 1024 * 1024;
                int blockSize = 8 * 1024;
                int blocksPerMb = (1024 * 1024) / blockSize;

                // create the file on the local disk
                // sending it to the disk to allow for creating larger files and
                // not using all the memory
                if (randomFileContents)
                {
                    // create using the random function in .NET
                    using (FileStream fs = File.OpenWrite(fileName))
                    {
                        byte[] block = new byte[blockSize];
                        for (int i = 0; i < fileSizeInMb * blocksPerMb; i++)
                        {
                            random.NextBytes(block);
                            fs.Write(block, 0, block.Length);
                        }
                    }
                }
                else
                {
                    // create with an empty file
                    using (FileStream fs = File.OpenWrite(fileName))
                    {
                        fs.SetLength(fileSizeInBytes);
                    }
                }

                // Get a hash of our file
                // We are reading the file back to emulate what might happen in a real app
                var hash = string.Empty;
                using (FileStream fs = File.OpenRead(fileName))
                {
                    hash = Convert.ToBase64String(md5.ComputeHash(fs));
                }

                //A blobClent we are going to use to upload
                var blobClient = blobContainerClients[blobContainerIndex].GetBlobClient(fileName);

                var metadata = new Dictionary<string, string>()
                {
                    { "Md5Hash", hash },
                    { "Randomized", randomFileContents.ToString() },
                    { "FileNum", fileNum.ToString() }
                };

                //Upload the file and its metadata, then clean up and report status
                uploads.Add(blobClient.UploadAsync(fileName, metadata: metadata, cancellationToken: cancellationToken)
                    .ContinueWith(x => {
                        
                        File.Delete(fileName);

                        Interlocked.Increment(ref completedFiles);
                        Interlocked.Add(ref completedMB, fileSizeInMb);

                        if (completedFiles % statusIncrement == 0)
                        {
                            var pctComplete = (completedFiles / (double)fileCount);
                            var mbps = (completedMB / stopwatch.Elapsed.TotalSeconds) * 8;
                            var eta = TimeSpan.FromSeconds((stopwatch.Elapsed.TotalSeconds / completedFiles) * (fileCount - completedFiles));
                            _logger.LogInformation($"Processed file {completedFiles:N0} of {fileCount:N0} ({pctComplete * 100:N1}%) after {stopwatch.Elapsed:ddd\\.hh\\:mm\\:ss} ({mbps:N} Mbps) estimated in {eta:ddd\\.hh\\:mm\\:ss}");
                        }

                        slim.Release();
                    }));
                
            }

            Task.WaitAll(uploads.ToArray());

            _logger.LogInformation($"Processing Finished {fileCount:N0} files after {stopwatch.Elapsed:ddd\\.hh\\:mm\\:ss} ({(completedMB / stopwatch.Elapsed.TotalSeconds) * 8:N} Mbps)");
        }

        /// <summary>
        /// Helper to get int values from config
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        private int GetConfigValue(string key, int defaultValue)
        {
            string configValue = _configuration[key];
            int value;
            if (!int.TryParse(configValue, out value))
            {
                value = defaultValue;
            }
            return value;
        }

        /// <summary>
        /// Helper to get bool values from config
        /// </summary>
        /// <param name="key"></param>
        /// <param name="defaultValue"></param>
        /// <returns></returns>
        private bool GetConfigValue(string key, bool defaultValue)
        {
            string configValue = _configuration[key];
            bool value;
            if (!bool.TryParse(configValue, out value))
            {
                value = defaultValue;
            }
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
        private async Task<List<BlobContainerClient>> GetTargetContainersAsync(int defultNumber, CancellationToken cancellationToken)
        {
            var blobContainers = new List<BlobContainerClient>();

            string blobContainerConfig = _configuration["BlobContainers"];
            if (!string.IsNullOrEmpty(blobContainerConfig))
            {
                int containerCount = 0;
                if (int.TryParse(blobContainerConfig, out containerCount))
                {
                    // If a number was specified create that many container names
                    for (int i = 0; i < containerCount; i++)
                    {
                        blobContainers.Add(_blobServiceClient.GetBlobContainerClient(Guid.NewGuid().ToString().ToLower()));
                    }
                }
                else
                {
                    // If container names were specified use them
                    foreach (var name in blobContainerConfig.Split(",").Select(x => x.Trim()).Distinct())
                    {
                        blobContainers.Add(_blobServiceClient.GetBlobContainerClient(name));
                    }
                }
            }
            else
            {
                // create the default number of conatiners
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
