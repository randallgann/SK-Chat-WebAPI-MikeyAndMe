using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using static TranscriptSearchService;

public class QuestionGenerationPlugin
{
    private readonly Kernel _kernel;
    private readonly ITranscriptSearchService _searchService;
    private readonly IQuestionRepository _questionRepository;
    private readonly ILogger _logger;
    private readonly PromptsOptions _promptOptions;

    public QuestionGenerationPlugin(
        Kernel kernel,
        ITranscriptSearchService searchService,
        IQuestionRepository questionRepository,
        IOptions<PromptsOptions> promptOptions,
        ILogger<QuestionGenerationPlugin> logger)
    {
        _kernel = kernel;
        _searchService = searchService;
        _questionRepository = questionRepository;
        _promptOptions = promptOptions.Value.Copy();
        _logger = logger;
    }

    [KernelFunction, Description("Generate sample questions based on transcript content")]
    public async Task<string> GenerateQuestionsAsync(
        [Description("Number of transcript segments to sample")] int sampleSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var episodeNumbers = new List<int> { 201, 504, 510, 509, 606, 607, 608, 101, 307, 401, 410, 609, 602 };
            var random = new Random();
            var selectedEpisode = episodeNumbers[random.Next(episodeNumbers.Count)];

            _logger.LogInformation("Selected Episode Number: {EpisodeNumber}", selectedEpisode);


            // Use the search service to get random transcript segments
            var searchResult = await _searchService.SearchAsync(new SearchRequest
            {
                MaxResults = sampleSize,
                EpisodeNumber = selectedEpisode,
                QueryText = "Topic"
            });

            if (!searchResult.Success || searchResult.Response == null)
            {
                _logger.LogError("Failed to fetch content for question generation");
                return "Failed to generate questions";
            }

            string transcriptString = string.Join(", ", searchResult.Response.Results.Select(r => r.Text));

            // Create completion settings similar to ChatPlugin
            var settings = new OpenAIPromptExecutionSettings
            {
                MaxTokens = _promptOptions.ResponseTokenLimit,
                Temperature = 0.9, // You might want to adjust these parameters
                TopP = 0.95,
                FrequencyPenalty = 0,
                PresencePenalty = 0
            };

            // Create the prompt for question generation
            var prompt = $"""
                    Here is some transcript text from a popular youtube channel, the speakers are either Matthew or Mikey, make sure to substitue either Matt or Mikey when you talk about the speaker, assume you know which question should be asked about whom.:
                    '{transcriptString}'
                    I want you to provide 3-5 short questions, each question should be between 3-8 words and each question should focus on specific people, places, events or ideas.
                    Be on the lookout for movie references, art, music, and other pop culture references and ask questions about those.
                    Word the questions in such a way that the question is only answerable from the text itself, if the answer to your question cannot be answered by only the text, do not include it in the list.
                    Most importantly, each questions should be interesting and creative enough to engage the reader and entice them to click on it.
                    Please return the questions as a JSON array of strings without any formatting artifacts such as backticks.
                    """;

            // Create and invoke the completion function
            var completionFunction = _kernel.CreateFunctionFromPrompt(
                prompt,
                settings,
                functionName: "GenerateQuestion");

            var response = await completionFunction.InvokeAsync(_kernel, cancellationToken: cancellationToken);
            var questions = JsonSerializer.Deserialize<List<string>>(response.ToString());

            if (questions != null)
            {
                await _questionRepository.SaveGeneratedQuestions(new GeneratedQuestion
                {
                    Questions = questions,
                    SourceEpisodeNumber = selectedEpisode.ToString()
                });
            }

            return "Successfully generated questions";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions");
            return "Error generating questions";
        }
    }
}