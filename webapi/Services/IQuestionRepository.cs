using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public class GeneratedQuestion
{
    public Guid Id { get; set; }
    public string SourceEpisodeNumber { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastShownAt { get; set; }
    public int TimesShown { get; set; }
    public List<string> Questions { get; set; } = new();
}

public interface IQuestionRepository
{
    // Save new generated questions
    Task SaveGeneratedQuestions(GeneratedQuestion questions);

    // Get random questions for display, optionally filtering by topic
    Task<List<GeneratedQuestion>> GetRandomQuestionsAsync(int count, string? topic = null);

    Task<List<GeneratedQuestion>> GetAllQuestionsAsync();

    // Mark questions as shown to track usage
    Task UpdateQuestionShownAsync(Guid questionId);

    // Get questions by various filters for management/reporting
    Task<List<GeneratedQuestion>> GetQuestionsByEpisodeAsync(string episodeNumber);
    Task<List<GeneratedQuestion>> GetQuestionsByTopicAsync(string topic);
    Task<List<GeneratedQuestion>> GetQuestionsGeneratedInLastDaysAsync(int days);

    // Optional: Clean up old or frequently shown questions
    Task DeleteQuestionsOlderThanAsync(DateTime cutoffDate);
    Task DeleteQuestionsByIdAsync(Guid id);
}