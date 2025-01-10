using System.Threading;
using System.Threading.Tasks;
using static TranscriptSearchService;

public interface ITranscriptSearchService
{
    Task<SearchServiceResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default);
    Task<SearchServiceResult> SearchWithIntentAsync(string userIntent, CancellationToken cancellationToken = default);

}