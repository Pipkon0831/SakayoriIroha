using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public class DeepSeekLLMProvider : ILLMClient
{
    private const string URL = "https://api.deepseek.com/chat/completions";

    private readonly string _model;
    private readonly Func<string> _apiKeyProvider;

    public DeepSeekLLMProvider(string model, Func<string> apiKeyProvider)
    {
        _model = model;
        _apiKeyProvider = apiKeyProvider;
    }

    public async Task<string> RequestJsonAsync(
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        float temperature,
        int timeoutSeconds,
        CancellationToken ct)
    {
        string apiKey = _apiKeyProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DEEPSEEK_API_KEY is missing. Set it as an environment variable.");

        var payload = new Payload
        {
            model = _model,
            response_format = new ResponseFormat { type = "json_object" },
            max_tokens = maxTokens,
            temperature = temperature,
            messages = new Message[]
            {
                new Message { role = "system", content = systemPrompt },
                new Message { role = "user", content = userPrompt }
            }
        };

        string body = JsonUtility.ToJson(payload);

        using var req = new UnityWebRequest(URL, "POST");
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.SetRequestHeader("Authorization", $"Bearer {apiKey}");
        req.timeout = timeoutSeconds;

        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            if (ct.IsCancellationRequested)
            {
                req.Abort();
                ct.ThrowIfCancellationRequested();
            }
            await Task.Yield();
        }

        if (req.result != UnityWebRequest.Result.Success)
            throw new Exception($"DeepSeek HTTP failed: {req.responseCode} {req.error}\n{req.downloadHandler.text}");

        return req.downloadHandler.text;
    }

    [Serializable] private class Payload
    {
        public string model;
        public Message[] messages;
        public ResponseFormat response_format;
        public int max_tokens;
        public float temperature;
    }

    [Serializable] private class ResponseFormat
    {
        public string type;
    }

    [Serializable] private class Message
    {
        public string role;
        public string content;
    }
}