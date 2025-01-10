using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotChat.WebApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class StartupQuestionGenerationService : IHostedService
{
    private readonly ILogger<StartupQuestionGenerationService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly StartupFileProcessingService _fileProcessingService;

    // Fields for periodic work
    private Timer? _timer;
    private bool _isRunning; // simple flag to prevent overlapping executions

    public StartupQuestionGenerationService(
        ILogger<StartupQuestionGenerationService> logger,
        IServiceProvider serviceProvider,
        StartupFileProcessingService fileProcessingService)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _fileProcessingService = fileProcessingService;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Wait for file processing to complete
        await _fileProcessingService.ProcessingComplete;

        _logger.LogInformation("File processing completed. Starting question generation.");

        // 2) Run an initial generation immediately
        await GenerateQuestionsOnce(cancellationToken);

        // 3) Schedule periodic generation
        //    Adjust the TimeSpan.FromMinutes(...) intervals as needed
        _timer = new Timer(
            async _ =>
            {
                // Prevent overlap if a previous invocation is still running
                if (_isRunning)
                {
                    return;
                }

                _isRunning = true;
                try
                {
                    await GenerateQuestionsOnce(cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during periodic question generation");
                }
                finally
                {
                    _isRunning = false;
                }
            },
            null,                  // state
            TimeSpan.FromMinutes(5),   // Initial delay before first periodic run
            TimeSpan.FromHours(24));  // Interval for subsequent runs
    }

    // Encapsulate the question generation logic
    private async Task GenerateQuestionsOnce(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var questionPlugin = scope.ServiceProvider.GetRequiredService<QuestionGenerationPlugin>();

        try
        {
            var result = await questionPlugin.GenerateQuestionsAsync(5, cancellationToken);
            _logger.LogInformation("Question generation completed: {Result}", result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating questions");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
