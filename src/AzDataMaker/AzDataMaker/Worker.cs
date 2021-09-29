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
        private const int KiB = 1024;
        private const int MiB = KiB * 1024;
        private const int GiB = MiB * 1024;
        private const long TiB = GiB * 1024L;

        private readonly string _name;
        private readonly ILogger<Worker> _logger;
        private readonly ConfigHelper _config;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        private readonly BlobServiceClient _blobServiceClient;
      

        public Worker(ILogger<Worker> logger,
            IHostApplicationLifetime hostApplicationLifetime,
            BlobServiceClient blobServiceClient,
            ConfigHelper config)
        {
            _name = this.GetType().Name;
            _logger = logger;
            _hostApplicationLifetime = hostApplicationLifetime;
            _blobServiceClient = blobServiceClient;
            _config = config;
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
            SemaphoreSlim slim = null;
            MD5 md5 = MD5.Create();

            // Target containers to round robin over
            var blobContainerClients = await _config.GetTargetContainersAsync(5, cancellationToken);
            int containerCount = blobContainerClients.Count();

            // How many files should we create
            int fileCount = _config.GetConfigValue("FileCount", 100);

            // How many threads should we use
            slim = new SemaphoreSlim(_config.GetConfigValue("Threads", Environment.ProcessorCount * 2));

            // How many files have we created
            int completedFiles = 0;
            long completedBytes = 0;

            // Max File Size (in MiB)
            int maxFileSizeMiB = _config.GetConfigValue("MaxFileSize", 100);
            long maxFileSizeBytes = maxFileSizeMiB * MiB;

            // Min File Size (in MiB)
            int minFileSizeMiB = _config.GetConfigValue("MinFileSize", 4);
            long minFileSizeBytes = minFileSizeMiB * MiB;

            // How often should I log progress on the upload (in num of files)?
            int statusIncrement = _config.GetConfigValue("ReportStatusIncrement", 1000);

            bool randomFileContents = _config.GetConfigValue("RandomFileContents", false);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            _logger.LogInformation("Processing Starting");

            var uploads = new List<Task>();

            for (int fileNumber = 0; fileNumber < fileCount; fileNumber++)
            {
                slim.Wait();
                var fileNum = fileNumber;
                uploads.Add(Task.Run(async () => 
                {
                    var random = new Random();
                    int blobContainerIndex = (fileCount - fileNum) % containerCount;
                    string fileName = $"{Guid.NewGuid().ToString().ToLower()}.dat";
                    long fileSizeInBytes = random.NextLong(minFileSizeBytes, maxFileSizeBytes);
                    long chunkSize = 4 * MiB;

                    //A blobClent we are going to use to upload
                    var blobClient = blobContainerClients[blobContainerIndex].GetBlockBlobClient(fileName);

                    _logger.LogDebug($"Starting File {fileNum:N0} of {fileSizeInBytes:N0} bytes PutBlob {fileSizeInBytes < blobClient.BlockBlobMaxUploadBlobBytes} ");


                    using (FileStream fs = File.OpenWrite(fileName))
                    {
                        foreach (var chunk in GetChunks(fileSizeInBytes, chunkSize, randomFileContents))
                        {
                            fs.Write(chunk, 0, chunk.Length);
                        }
                    }

                    // Get a hash of our file
                    // We are reading the file back to emulate what might happen in a real app
                    var hash = string.Empty;
                    using (FileStream fs = File.OpenRead(fileName))
                    {
                        hash = Convert.ToBase64String(md5.ComputeHash(fs));
                    }

                    var metadata = new Dictionary<string, string>()
                    {
                        { "Md5Hash", hash },
                        { "Randomized", randomFileContents.ToString() },
                        { "FileNum", fileNum.ToString() },
                        { "FileSize", fileSizeInBytes.ToString() }
                    };


                    if (fileSizeInBytes > (long)blobClient.BlockBlobMaxStageBlockBytes * (long)blobClient.BlockBlobMaxBlocks)
                    {
                        throw new ArgumentOutOfRangeException($"File Too Big {fileSizeInBytes:N0} > {blobClient.BlockBlobMaxStageBlockBytes * blobClient.BlockBlobMaxBlocks}");
                    }
                    else if (fileSizeInBytes < blobClient.BlockBlobMaxUploadBlobBytes)
                    {
                        using (var stream = File.OpenRead(fileName))
                        {
                            await blobClient.UploadAsync(stream, metadata: metadata, cancellationToken: cancellationToken);
                        }
                    }
                    else
                    {
                        var blockSize = Math.Max(chunkSize, fileSizeInBytes / blobClient.BlockBlobMaxBlocks);
                        byte[] buffer = new byte[blockSize];

                        var blockList = new List<string>();

                        using (var stream = File.OpenRead(fileName))
                        {
                            int readAmount = await stream.ReadAsync(buffer, 0, buffer.Length);

                            while (readAmount != 0)
                            {
                                var blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                                blockList.Add(blockId);
                                await blobClient.StageBlockAsync(blockId, new MemoryStream(buffer), cancellationToken: cancellationToken);

                                readAmount = await stream.ReadAsync(buffer, 0, buffer.Length);
                            }
                        }

                        await blobClient.CommitBlockListAsync(blockList, metadata: metadata, cancellationToken: cancellationToken);
                    }

                    File.Delete(fileName);

                    Interlocked.Increment(ref completedFiles);
                    Interlocked.Add(ref completedBytes, fileSizeInBytes);

                    if (completedFiles % statusIncrement == 0)
                    {
                        var pctComplete = (completedFiles / (double)fileCount);
                        var mbps = (completedBytes / MiB / stopwatch.Elapsed.TotalSeconds) * 8;
                        var eta = TimeSpan.FromSeconds((stopwatch.Elapsed.TotalSeconds / completedFiles) * (fileCount - completedFiles));
                        _logger.LogInformation($"Processed file {completedFiles:N0} of {fileCount:N0} ({pctComplete * 100:N1}%) after {stopwatch.Elapsed:ddd\\.hh\\:mm\\:ss} ({mbps:N} Mbps) estimated in {eta:ddd\\.hh\\:mm\\:ss}");
                    }

                    _logger.LogDebug($"Finished File {fileNum}");

                    slim.Release();
                }, cancellationToken));
            }

            Task.WaitAll(uploads.ToArray());

            _logger.LogInformation($"Processing Finished {fileCount:N0} files after {stopwatch.Elapsed:ddd\\.hh\\:mm\\:ss} ({(completedBytes / stopwatch.Elapsed.TotalSeconds) * 8:N} Mbps)");
        }


        private IEnumerable<byte[]> GetChunks(long fileSizeInBytes, long chunkSize, bool randomFileContents)
        {
            var random = new Random();
            long currentByteIndex = 0;
            while (fileSizeInBytes > currentByteIndex)
            {
                byte[] chunk = new byte[Math.Min(chunkSize, fileSizeInBytes - currentByteIndex)];

                if (randomFileContents)
                {
                    random.NextBytes(chunk);
                }

                currentByteIndex += chunk.Length;
                yield return chunk;
            }
        }
    }
}
