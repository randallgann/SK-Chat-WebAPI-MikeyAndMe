using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Models.Request;
using CopilotChat.WebApi.Services;
using Google.Apis.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.KernelMemory.AI;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using static TranscriptSearchService;

namespace CopilotChat.WebApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VectorStoreController : ControllerBase
{
    private readonly ILogger<VectorStoreController> _logger;
    private readonly DocumentTypeProvider _documentTypeProvider;

    private readonly ITranscriptSearchService _searchService;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    private readonly IEmbeddingGenerationService<string, float> _embeddingService;
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public VectorStoreController(ILogger<VectorStoreController> logger, DocumentTypeProvider documentTypeProvider, ITranscriptSearchService searchService, IConfiguration configuration)
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    {
        _logger = logger;
        _documentTypeProvider = documentTypeProvider;
        _searchService = searchService;

        var openAiApiKey = configuration["KernelMemory:Services:OpenAI:APIKey"];

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        _embeddingService = new OpenAITextEmbeddingGenerationService(
               "text-embedding-ada-002",
               openAiApiKey);
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }

    /// <summary>
    /// Processes a text document and stores its vectors.
    /// </summary>
    [Route("process")]
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ProcessDocumentAsync(
        [FromServices] Kernel kernel,
        [FromForm] DocumentImportForm documentImportForm
    )
    {
        try
        {
            if (!await ValidateDocumentImportAsync(documentImportForm))
            {
                return BadRequest("Invalid document submission");
            }

            var vectorStoreRecordCollection = kernel.GetRequiredService<IVectorStoreRecordCollection<Guid, TranscriptText>>();
            if (vectorStoreRecordCollection == null)
            {
                return StatusCode(500, "Vector store service not available");
            }

            await vectorStoreRecordCollection.CreateCollectionIfNotExistsAsync();
            var results = new List<ProcessingResult>();

            foreach (var file in documentImportForm.FormFiles)
            {
                try
                {
                    _logger.LogInformation("Processing file {FileName} for vector storage", file.FileName);

                    // Read and parse the file content
                    string fileContent;
                    using (var streamReader = new StreamReader(file.OpenReadStream()))
                    {
                        fileContent = await streamReader.ReadToEndAsync();
                    }

                    // Split into lines and parse each line
                    var jsonItems = JsonSerializer.Deserialize<List<JsonTranscriptItem>>(fileContent);
                    if (jsonItems == null)
                    {
                        _logger.LogWarning("Failed to parse JSON content for file {FileName}", file.FileName);
                        results.Add(new ProcessingResult
                        {
                            FileName = file.FileName,
                            Success = false,
                            ErrorMessage = "Failed to parse JSON content"
                        });
                        continue;
                    }

                    // Convert to TranscriptText
                    var transcripts = jsonItems.Select(item => new TranscriptText
                    {
                        Id = Guid.NewGuid(),
                        Text = item.text ?? string.Empty,
                        StartTime = item.metadata.timestamp_start,
                        EndTime = item.metadata.timestamp_end,
                        EpisodeDate = DateTime.Parse(item.metadata.date),
                        EpisodeNumber = item.metadata.episode_number.ToString(),
                        EpisodeTitle = item.metadata.episode_title ?? string.Empty,
                        ChunkTopic = item.metadata.chunk_topic ?? string.Empty,
                        Topics = item.metadata.topics ?? string.Empty
                    }).ToList();

                    // Process transcripts in batches
                    var batchSize = 100; // Adjust based on your needs
                    for (int i = 0; i < transcripts.Count; i += batchSize)
                    {
                        var batch = transcripts.Skip(i).Take(batchSize).ToList();
                        var textsToEmbed = batch.Select(t => t.Text).ToList();

                        // Generate embeddings for the batch
                        var embeddings = await _embeddingService.GenerateEmbeddingsAsync(
                            textsToEmbed,
                            kernel,
                            CancellationToken.None);

                        // Create TranscriptText instances with embeddings
                        var processedTranscripts = batch.Select((transcript, index) => new TranscriptText
                        {
                            Id = transcript.Id,
                            Text = transcript.Text,
                            StartTime = transcript.StartTime,
                            EndTime = transcript.EndTime,
                            EpisodeDate = transcript.EpisodeDate,
                            EpisodeNumber = transcript.EpisodeNumber,
                            EpisodeTitle = transcript.EpisodeTitle,
                            ChunkTopic = transcript.ChunkTopic,
                            Topics = transcript.Topics,
                            Embedding = embeddings[index].ToArray()
                        });

                        // Upsert batch
                        try
                        {
                            await foreach (var recordId in vectorStoreRecordCollection.UpsertBatchAsync(
                                processedTranscripts,
                                cancellationToken: CancellationToken.None))
                            {
                                var processedTranscript = batch.First(t => t.Id.Equals(recordId));
                                results.Add(new ProcessingResult
                                {
                                    FileName = file.FileName,
                                    RecordId = processedTranscript.Id,
                                    Success = true
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error upserting transcripts batch for file {FileName}", file.FileName);
                            foreach (var transcript in batch)
                            {
                                results.Add(new ProcessingResult
                                {
                                    FileName = file.FileName,
                                    RecordId = transcript.Id,
                                    Success = false,
                                    ErrorMessage = ex.Message
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file {FileName}", file.FileName);
                    results.Add(new ProcessingResult
                    {
                        FileName = file.FileName,
                        Success = false,
                        ErrorMessage = ex.Message
                    });
                }
            }

            return Ok(new ProcessingResponse
            {
                Results = results,
                TotalProcessed = results.Count,
                SuccessfulCount = results.Count(r => r.Success)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in document processing endpoint");
            return StatusCode(500, "An error occurred while processing the documents");
        }
    }

    public class ProcessingResult
    {
        public string FileName { get; set; } = string.Empty;
        public Guid RecordId { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class ProcessingResponse
    {
        public IEnumerable<ProcessingResult> Results { get; set; } = Array.Empty<ProcessingResult>();
        public int TotalProcessed { get; set; }
        public int SuccessfulCount { get; set; }
    }

    /// <summary>
    /// Searches for similar transcript segments
    /// </summary>
    [HttpPost("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchAsync(
        [FromBody] SearchRequest request)
    {
        try
        {
            var response = await _searchService.SearchAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing transcript search");
            return StatusCode(500, "An error occurred during search");
        }
    }

    private async Task<bool> ValidateDocumentImportAsync(DocumentImportForm documentImportForm)
    {
        if (!documentImportForm.FormFiles.Any())
        {
            return false;
        }

        foreach (var file in documentImportForm.FormFiles)
        {
            if (file.Length == 0)
            {
                return false;
            }

            var fileType = Path.GetExtension(file.FileName);
            if (!_documentTypeProvider.IsSupported(fileType, out _))
            {
                return false;
            }
        }

        return true;
    }

    // Classes for deserializing input JSON
    private class JsonTranscriptItem
    {
        public string text { get; set; } = string.Empty;
        public JsonMetadata metadata { get; set; } = new JsonMetadata();
    }

    private class JsonMetadata
    {
        public string date { get; set; } = string.Empty;
        public int episode_number { get; set; }
        public string episode_title { get; set; } = string.Empty;
        public double timestamp_start { get; set; }
        public double timestamp_end { get; set; }
        public string chunk_topic { get; set; } = string.Empty;
        public string topics { get; set; } = string.Empty;
    }
}
