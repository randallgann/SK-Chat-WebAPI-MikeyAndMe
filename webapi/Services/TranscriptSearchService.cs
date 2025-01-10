using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Embeddings;
using Microsoft.AspNetCore.Http;
using System.Linq;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

public class TranscriptSearchService : ITranscriptSearchService
{
    private readonly ILogger<TranscriptSearchService> _logger;
    private readonly IServiceProvider _serviceProvider;
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    private readonly IEmbeddingGenerationService<string, float> _embeddingService;
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    public TranscriptSearchService(
        ILogger<TranscriptSearchService> logger,
        IServiceProvider serviceProvider,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        var openAiApiKey = configuration["KernelMemory:Services:OpenAI:APIKey"];

#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        _embeddingService = new OpenAITextEmbeddingGenerationService(
               "text-embedding-ada-002",
               openAiApiKey);
#pragma warning restore SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    }

    public async Task<SearchServiceResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the kernel from the service provider when needed
            var _kernel = _serviceProvider.GetRequiredService<Kernel>();

            //Generate embedding for the search query
            var queryEmbedding = await _embeddingService.GenerateEmbeddingAsync(
                request.QueryText,
                _kernel,
                CancellationToken.None);


            // Create and configure filter if any metadata filters are present
            var filter = new VectorSearchFilter();
            bool hasFilters = false;

            if (request.EpisodeDate.HasValue)
            {
                filter.EqualTo("EpisodeDate", request.EpisodeDate.Value);
                hasFilters = true;
            }

            if (request.EpisodeNumber.HasValue)
            {
                filter.EqualTo("EpisodeNumber", request.EpisodeNumber.Value.ToString());
                hasFilters = true;
            }

            if (!string.IsNullOrWhiteSpace(request.EpisodeTitle))
            {
                filter.EqualTo("EpisodeTitle", request.EpisodeTitle);
                hasFilters = true;
            }

            if (!string.IsNullOrWhiteSpace(request.ChunkTopic))
            {
                filter.EqualTo("ChunkTopic", request.ChunkTopic);
                hasFilters = true;
            }

            if (!string.IsNullOrWhiteSpace(request.Topic))
            {
                // Use AnyTagEqualTo for the Topics field since it's a comma-separated list
                filter.AnyTagEqualTo("Topics", request.Topic);
                hasFilters = true;
            }

            // Create search options
            var searchOptions = new VectorSearchOptions
            {
                Top = 100,
                Skip = 0,
                IncludeVectors = false,
                IncludeTotalCount = true,
                VectorPropertyName = "Embedding",
                Filter = hasFilters ? filter : null
            };

            var vectorStoreRecordCollection = _kernel.GetRequiredService<IVectorStoreRecordCollection<Guid, TranscriptText>>();
            if (vectorStoreRecordCollection == null)
            {
                return new SearchServiceResult
                {
                    Success = false,
                    ErrorMessage = "Vector store service not available"
                };
            }

            // Search using vectorized search
            var searchResults = await vectorStoreRecordCollection.VectorizedSearchAsync(queryEmbedding,
            searchOptions);


            //Convert to response format
            var results = await searchResults.Results
            .OrderBy(result => result.Record.StartTime)
            .Select(result => new SearchResultRecord
            {
                Id = result.Record.Id,
                Text = result.Record.Text,
                EpisodeNumber = result.Record.EpisodeNumber,
                EpisodeDate = result.Record.EpisodeDate,
                EpisodeTitle = result.Record.EpisodeTitle,
                StartTime = result.Record.StartTime,
                EndTime = result.Record.EndTime,
                ChunkTopic = result.Record.ChunkTopic,
                Topics = result.Record.Topics,
                // The underlying library should provide a similarity score if available.
                // If not, you may store or compute it separately. Here we assume result.Score is available.
                RelevanceScore = (float)result.Score
            }).ToListAsync();

            return new SearchServiceResult
            {
                Success = true,
                Response = new SearchResponse
                {
                    Results = results,
                    TotalResults = (int)searchResults.TotalCount
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing transcript search");
            return new SearchServiceResult
            {
                Success = false,
                ErrorMessage = "An error occurred during search"
            };
        }
    }

    public async Task<SearchServiceResult> SearchWithIntentAsync(string userIntent, CancellationToken cancellationToken = default)
    {
        try
        {
            // Get the kernel from the service provider
            var kernel = _serviceProvider.GetRequiredService<Kernel>();

            // First, extract any episode-related metadata from the user intent
            var metadata = await ExtractEpisodeMetadata(kernel, userIntent, new KernelArguments(), cancellationToken);

            // Create a search request using the extracted metadata
            var searchRequest = new SearchRequest
            {
                QueryText = userIntent,
                MaxResults = 5, // Limit results to most relevant matches
                MinRelevanceScore = 0.7f, // Set minimum relevance threshold
                EpisodeNumber = metadata.EpisodeNumber,
                EpisodeTitle = metadata.EpisodeTitle,
                EpisodeDate = metadata.EpisodeDate,
                Topic = metadata.Topic,
                ChunkTopic = metadata.ChunkTopic
            };

            // Perform the vector search with metadata filters
            var searchResult = await SearchAsync(searchRequest, cancellationToken);

            if (!searchResult.Success)
            {
                _logger.LogWarning($"Vector search failed during intent search: {searchResult.ErrorMessage}");
                return searchResult;
            }

            // If we got results, sort them by relevance and take top matches
            if (searchResult.Response != null && searchResult.Response.Results.Any())
            {
                var sortedResults = searchResult.Response.Results
                    .OrderByDescending(r => r.RelevanceScore)
                    .Where(r => r.RelevanceScore >= (searchRequest.MinRelevanceScore ?? 0))
                    .Take(searchRequest.MaxResults ?? 5);

                return new SearchServiceResult
                {
                    Success = true,
                    Response = new SearchResponse
                    {
                        Results = sortedResults,
                        TotalResults = sortedResults.Count()
                    }
                };
            }

            // If no results found with metadata filters, try a broader search
            if (!searchResult.Response?.Results.Any() ?? true)
            {
                _logger.LogInformation("No results found with metadata filters, attempting broader search");

                // Perform a broader search without metadata filters
                var broadSearchRequest = new SearchRequest
                {
                    QueryText = userIntent,
                    MaxResults = 3, // Reduce results for broader search
                    MinRelevanceScore = 0.8f // Increase relevance threshold for broader search
                };

                searchResult = await SearchAsync(broadSearchRequest, cancellationToken);
            }

            return searchResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing intent-based transcript search");
            return new SearchServiceResult
            {
                Success = false,
                ErrorMessage = "An error occurred during intent-based search"
            };
        }
    }

    private async Task<EpisodeMetadata> ExtractEpisodeMetadata(Kernel kernel, string userIntent, KernelArguments context, CancellationToken cancellationToken)
    {

        // Create a prompt to extract structured metadata
        var metadataExtractionPrompt = @"
            User query: {{$userIntent}}

            Extract related metadata from the user query. 
            Valid metadata fields are episode number, episode title, episode date, and topic.
            Only include fields in the response if they are explicitly mentioned or clearly implied in the query.

            Return valid JSON (not in markdown) with a single top-level object, no backticks and no additional quotes around the entire object.
            For Example:
            {
                ""EpisodeNumber"": 510,
                ""EpisodeTitle"": ""The Title"",
                ""EpisodeDate"": ""2022-01-01"",
                ""Topic"": ""The Topic""
            }";

        var completionFunction = kernel.CreateFunctionFromPrompt(
            metadataExtractionPrompt,
            new OpenAIPromptExecutionSettings
            {
                MaxTokens = 200,
                Temperature = 0.0,
                TopP = 1.0
            });

        var result = await completionFunction.InvokeAsync(
            kernel,
            new KernelArguments { ["userIntent"] = userIntent },
            cancellationToken);

        try
        {
            var rawString = result.GetValue<string>();
            var jsonString = result.GetValue<string>().Replace("{{", "{").Replace("}}", "}");
            var metadata = JsonSerializer.Deserialize<EpisodeMetadata>(jsonString);
            return metadata ?? new EpisodeMetadata();
        }
        catch (JsonException)
        {
            _logger.LogWarning("Failed to parse metadata JSON from LLM response");
            return new EpisodeMetadata();
        }
    }

    public class SearchRequest
    {
        public string QueryText { get; set; } = string.Empty;
        public int? MaxResults { get; set; }
        public float? MinRelevanceScore { get; set; }
        // Optional metadata filters
        public DateTime? EpisodeDate { get; set; }
        public int? EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }
        public string? ChunkTopic { get; set; }
        public string? Topic { get; set; }  // Single topic to search for within Topics
    }

    public class SearchResultRecord
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public string EpisodeNumber { get; set; } = string.Empty;
        public DateTime EpisodeDate { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public string EpisodeTitle { get; set; } = string.Empty;
        public string ChunkTopic { get; set; } = string.Empty;
        public string Topics { get; set; } = string.Empty;
        public float RelevanceScore { get; set; }
    }

    public class SearchResponse
    {
        public IEnumerable<SearchResultRecord> Results { get; set; } = Array.Empty<SearchResultRecord>();
        public int TotalResults { get; set; }
    }

    public class SearchServiceResult
    {
        public SearchResponse? Response { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class EpisodeMetadata
    {
        public int? EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }
        public DateTime? EpisodeDate { get; set; }
        public string? Topic { get; set; }
        public string? ChunkTopic { get; set; }
    }
}