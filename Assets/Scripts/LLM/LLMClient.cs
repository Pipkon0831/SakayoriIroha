using System.Threading;
using System.Threading.Tasks;

public class LLMClient
{
    private readonly ILLMClient _provider;

    public LLMClient(ILLMClient provider)
    {
        _provider = provider;
    }

    public Task<string> RequestJsonAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens = 800,
        float temperature = 0.7f,
        int timeoutSeconds = 10,
        CancellationToken ct = default)
    {
        return _provider.RequestJsonAsync(
            systemPrompt,
            userPrompt,
            maxTokens,
            temperature,
            timeoutSeconds,
            ct);
    }
}