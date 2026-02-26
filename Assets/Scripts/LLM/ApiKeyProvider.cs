using System;

public static class ApiKeyProvider
{
    public static string Get()
    {
        return Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
    }
}