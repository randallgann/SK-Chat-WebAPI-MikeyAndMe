using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

public class InMemoryQuestionRepository : IQuestionRepository
{
    private readonly ConcurrentDictionary<Guid, GeneratedQuestion> _questions = new();
    private readonly ILogger<InMemoryQuestionRepository> _logger;

    public InMemoryQuestionRepository(ILogger<InMemoryQuestionRepository> logger)
    {
        _logger = logger;
    }

    public Task<List<GeneratedQuestion>> GetAllQuestionsAsync()
    {
        try
        {
            var questions = _questions.Values
                .OrderByDescending(q => q.GeneratedAt)
                .ToList();
            return Task.FromResult(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all questions");
            throw;
        }
    }

    public Task SaveGeneratedQuestions(GeneratedQuestion questions)
    {
        try
        {
            if (questions.Id == Guid.Empty)
            {
                questions.Id = Guid.NewGuid();
            }
            _questions.TryAdd(questions.Id, questions);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving generated questions");
            throw;
        }
    }

    public Task<List<GeneratedQuestion>> GetRandomQuestionsAsync(int count, string? topic = null)
    {
        try
        {
            var query = _questions.Values.AsQueryable();

            if (!string.IsNullOrWhiteSpace(topic))
            {
                query = query.Where(q => q.Topics.Contains(topic));
            }

            // Use OrderBy with Random to shuffle, then prioritize less shown questions
            var random = new Random();
            var questions = query
                .OrderBy(x => random.Next())
                .ThenBy(q => q.TimesShown)
                .ThenBy(q => q.LastShownAt ?? DateTime.MinValue)
                .Take(count)
                .ToList();

            return Task.FromResult(questions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting random questions");
            throw;
        }
    }

    public Task UpdateQuestionShownAsync(Guid questionId)
    {
        if (_questions.TryGetValue(questionId, out var question))
        {
            question.LastShownAt = DateTime.UtcNow;
            question.TimesShown++;
        }
        return Task.CompletedTask;
    }

    public Task<List<GeneratedQuestion>> GetQuestionsByEpisodeAsync(string episodeNumber)
    {
        var questions = _questions.Values
            .Where(q => q.SourceEpisodeNumber == episodeNumber)
            .OrderByDescending(q => q.GeneratedAt)
            .ToList();
        return Task.FromResult(questions);
    }

    public Task<List<GeneratedQuestion>> GetQuestionsByTopicAsync(string topic)
    {
        var questions = _questions.Values
            .Where(q => q.Topics.Contains(topic))
            .OrderByDescending(q => q.GeneratedAt)
            .ToList();
        return Task.FromResult(questions);
    }

    public Task<List<GeneratedQuestion>> GetQuestionsGeneratedInLastDaysAsync(int days)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var questions = _questions.Values
            .Where(q => q.GeneratedAt >= cutoffDate)
            .OrderByDescending(q => q.GeneratedAt)
            .ToList();
        return Task.FromResult(questions);
    }

    public Task DeleteQuestionsOlderThanAsync(DateTime cutoffDate)
    {
        var oldQuestionIds = _questions.Values
            .Where(q => q.GeneratedAt < cutoffDate)
            .Select(q => q.Id);

        foreach (var id in oldQuestionIds)
        {
            _questions.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }

    public Task DeleteQuestionsByIdAsync(Guid id)
    {
        _questions.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}