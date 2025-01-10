using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

[ApiController]
[Route("api/[controller]")]
public class SuggestedQuestionsController : ControllerBase
{
    private readonly Kernel _kernel;
    private readonly IQuestionRepository _questionRepository;
    private readonly ILogger<SuggestedQuestionsController> _logger;

    public SuggestedQuestionsController(
        Kernel kernel,
        IQuestionRepository questionRepository,
        ILogger<SuggestedQuestionsController> logger)
    {
        _kernel = kernel;
        _questionRepository = questionRepository;
        _logger = logger;
    }

    [HttpPost("generate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateQuestions(
        [FromQuery] int sampleSize = 3)
    {
        try
        {
            var result = await _kernel.InvokeAsync<string>(
                "QuestionGenerationPlugin",
                "GenerateQuestions",
                new() { ["sampleSize"] = sampleSize.ToString() });

            return Ok(new { message = result });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions");
            return StatusCode(500, "An error occurred while generating questions");
        }
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GeneratedQuestion>>> GetRandomQuestions(
        [FromQuery] int count = 5,
        [FromQuery] string? topic = null)
    {
        try
        {
            var questions = await _questionRepository.GetRandomQuestionsAsync(count, topic);

            // If we don't have enough questions stored, generate some new ones
            if (questions.Count < count)
            {
                var result = await _kernel.InvokeAsync<string>(
                    "QuestionGenerationPlugin",
                    "GenerateQuestionsAsync",
                    new() { ["sampleSize"] = "3" }); // Generate from 3 different transcript segments

                _logger.LogInformation("Generated new questions: {Result}", result);

                // Try getting questions again after generation
                questions = await _questionRepository.GetRandomQuestionsAsync(count, topic);
            }

            // Mark these questions as shown
            foreach (var question in questions)
            {
                await _questionRepository.UpdateQuestionShownAsync(question.Id);
            }

            return Ok(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting random questions");
            return StatusCode(500, "An error occurred while fetching questions");
        }
    }

    [HttpGet("episode/{episodeNumber}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GeneratedQuestion>>> GetQuestionsByEpisode(string episodeNumber)
    {
        try
        {
            var questions = await _questionRepository.GetQuestionsByEpisodeAsync(episodeNumber);
            return Ok(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting questions for episode {EpisodeNumber}", episodeNumber);
            return StatusCode(500, "An error occurred while fetching questions");
        }
    }

    [HttpGet("topic/{topic}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GeneratedQuestion>>> GetQuestionsByTopic(string topic)
    {
        try
        {
            var questions = await _questionRepository.GetQuestionsByTopicAsync(topic);
            return Ok(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting questions for topic {Topic}", topic);
            return StatusCode(500, "An error occurred while fetching questions");
        }
    }

    [HttpGet("all")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<GeneratedQuestion>>> GetAllQuestions()
    {
        try
        {
            var questions = await _questionRepository.GetAllQuestionsAsync();
            return Ok(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all questions");
            return StatusCode(500, "An error occurred while fetching questions");
        }
    }
}