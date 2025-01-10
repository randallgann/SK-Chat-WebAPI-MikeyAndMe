// using Microsoft.Extensions.Hosting;
// using Microsoft.Extensions.Logging;

// namespace CopilotChat.WebApi.Services;


// public class QuestionGenerationService : IHostedService
// {
//     private readonly ITranscriptSearchService _searchService;
//     private readonly IQuestionRepository _questionRepository;
//     private readonly ILogger<StartupFileProcessingService> _logger;

//     public QuestionGenerationService(
//        IVectorStore vectorStore,
//        ILLMService llmService,
//        IQuestionRepository questionRepository)
//     {
//         _vectorStore = vectorStore;
//         _llmService = llmService;
//         _questionRepository = questionRepository;
//         _timer = new Timer(GenerateQuestions, null, TimeSpan.Zero, TimeSpan.FromHours(1));
//     }

//     private async Task GenerateQuestions(object state)
//     {
//         // Get random content from vector store
//         var randomContent = await _vectorStore.GetRandomContent(limit: 3);

//         foreach (var content in randomContent)
//         {
//             var prompt = $"Based on this transcript segment: '{content.Text}' " +
//                         "generate 2-3 natural, conversational questions that a viewer " +
//                         "might ask about this content. Format as JSON array.";

//             var questions = await _llmService.GenerateQuestions(prompt);
//             await _questionRepository.SaveGeneratedQuestions(questions);
//         }
//     }


// }