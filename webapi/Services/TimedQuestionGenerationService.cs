using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

public class TimedQuestionGenerationService : BackgroundService
{
    private readonly ILogger<TimedQuestionGenerationService> _logger;
    private readonly QuestionGenerationPlugin _questionGenerationPlugin;
    private readonly TimeSpan _interval;

    public TimedQuestionGenerationService(
        ILogger<TimedQuestionGenerationService> logger,
        QuestionGenerationPlugin questionGenerationPlugin)
    {
        _logger = logger;
        _questionGenerationPlugin = questionGenerationPlugin;

        // Set your desired interval here (e.g., every 5 minutes)
        _interval = TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Timed Question Generation Service is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Executing QuestionGenerationPlugin at: {time}", DateTimeOffset.Now);

                // Call your plugin method. Note: you can pass in a sampleSize or retrieve from config
                await _questionGenerationPlugin.GenerateQuestionsAsync(sampleSize: 5, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while executing QuestionGenerationPlugin.");
            }

            // Wait for the specified interval before running again
            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Timed Question Generation Service is stopping.");
    }
}
