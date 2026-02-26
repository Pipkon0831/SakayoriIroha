using System.Threading;
using System.Threading.Tasks;

public interface ILLMClient
{
    Task<string> RequestJsonAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        float temperature,
        int timeoutSeconds,
        CancellationToken ct);
}