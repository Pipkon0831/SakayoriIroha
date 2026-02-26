using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class DeepSeekConnectionTest : MonoBehaviour
{
    [Header("可选：直接填Key（调试用）")]
    [SerializeField] private string manualApiKey = "";

    [Header("模型名")]
    [SerializeField] private string model = "deepseek-chat";

    private void Start()
    {
        StartCoroutine(TestConnection());
    }

    private IEnumerator TestConnection()
    {
        Debug.Log("===== DeepSeek Connection Test =====");

        string key = manualApiKey;

        if (string.IsNullOrWhiteSpace(key))
        {
            key = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
            Debug.Log("Using ENV key: " + (!string.IsNullOrWhiteSpace(key)));
        }
        else
        {
            Debug.Log("Using MANUAL key");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            Debug.LogError("❌ 没有找到 API Key");
            yield break;
        }

        string url = "https://api.deepseek.com/chat/completions";

        string json = $@"
{{
  ""model"": ""{model}"",
  ""messages"": [
    {{ ""role"": ""system"", ""content"": ""You are a test bot."" }},
    {{ ""role"": ""user"", ""content"": ""Say hello."" }}
  ],
  ""max_tokens"": 50,
  ""temperature"": 0.7
}}";

        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();

            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + key);

            Debug.Log("Sending request...");
            yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
            bool hasError = req.result != UnityWebRequest.Result.Success;
#else
            bool hasError = req.isNetworkError || req.isHttpError;
#endif

            Debug.Log("HTTP Status: " + req.responseCode);

            if (hasError)
            {
                Debug.LogError("❌ Request Failed");
                Debug.LogError("Error: " + req.error);
                Debug.LogError("Response: " + req.downloadHandler.text);
            }
            else
            {
                Debug.Log("✅ Request Success");
                Debug.Log("Response: " + req.downloadHandler.text);
            }
        }

        Debug.Log("===== Test Finished =====");
    }
}