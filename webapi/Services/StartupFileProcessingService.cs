using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using CopilotChat.WebApi.Models.Request;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;
using static CopilotChat.WebApi.Controllers.VectorStoreController;
using System.Collections.Generic;
using System.Linq;
using CopilotChat.WebApi.Controllers;

namespace CopilotChat.WebApi.Services;

public class JsonProcessingOptions
{
    public string JsonFilesDirectory { get; set; } = string.Empty;
}

public class StartupFileProcessingService : IHostedService
{
    private readonly ILogger<StartupFileProcessingService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly JsonProcessingOptions _options;
    private readonly TaskCompletionSource _processingComplete = new TaskCompletionSource();


    public Task ProcessingComplete => _processingComplete.Task;

    public StartupFileProcessingService(
        ILogger<StartupFileProcessingService> logger,
        IServiceProvider serviceProvider,
        IOptions<JsonProcessingOptions> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("StartupFileProcessingService has been resolved successfully.");
        var jsonDirectory = string.IsNullOrEmpty(_options.JsonFilesDirectory)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "JsonFiles")
            : _options.JsonFilesDirectory;

        if (!Directory.Exists(jsonDirectory))
        {
            _logger.LogError("The specified directory does not exist: {DirectoryPath}", jsonDirectory);
            return;
        }

        var jsonFiles = Directory.GetFiles(jsonDirectory, "*.json");
        if (!jsonFiles.Any())
        {
            _logger.LogInformation("No JSON files found in {Directory}", jsonDirectory);
            return;
        }

        _logger.LogInformation("Found {Count} JSON files to process", jsonFiles.Length);

        foreach (var filePath in jsonFiles)
        {
            try
            {
                _logger.LogInformation("Processing file: {FilePath}", filePath);

                // Create a new scope for each file processing operation
                using var scope = _serviceProvider.CreateScope();
                var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();
                var vectorController = scope.ServiceProvider.GetRequiredService<VectorStoreController>();
                var documentTypeProvider = scope.ServiceProvider.GetRequiredService<DocumentTypeProvider>();

                // Read the file content into memory first
                byte[] fileContent;
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    fileContent = new byte[fileStream.Length];
                    await fileStream.ReadAsync(fileContent, 0, (int)fileStream.Length);
                }

                // Create a memory stream from the file content
                using var memoryStream = new MemoryStream(fileContent);
                var fileName = Path.GetFileName(filePath);
                var formFile = new FormFile(memoryStream, 0, fileContent.Length, "file", fileName)
                {
                    Headers = new HeaderDictionary(),
                    ContentType = "application/json"
                };

                // Create DocumentImportForm
                var documentImportForm = new DocumentImportForm
                {
                    FormFiles = new List<IFormFile> { formFile }
                };

                // Process the file using the VectorStoreController
                var result = await vectorController.ProcessDocumentAsync(kernel, documentImportForm);

                if (result is OkObjectResult okResult)
                {
                    var processingResponse = okResult.Value as ProcessingResponse;
                    _logger.LogInformation(
                        "Successfully processed file {FilePath}. Processed {Total} records, {Successful} successful",
                        filePath,
                        processingResponse?.TotalProcessed ?? 0,
                        processingResponse?.SuccessfulCount ?? 0);
                }
                else
                {
                    _logger.LogWarning("Failed to process file: {FilePath}", filePath);
                }

                _processingComplete.TrySetResult();

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing file: {FilePath}", filePath);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}