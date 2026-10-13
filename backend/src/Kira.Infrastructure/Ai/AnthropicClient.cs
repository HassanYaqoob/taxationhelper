using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kira.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kira.Infrastructure.Ai;

public sealed class AnthropicOptions
{
    public const string Section = "Anthropic";
    /// <summary>Keep out of source control: use user-secrets locally and an environment variable in production.</summary>
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-5-5";
    public int MaxTokens { get; set; } = 16000;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1/messages";
    public string Version { get; set; } = "2023-06-01";
}

/// <summary>Calls the Anthropic Messages API. The API key never leaves the server.</summary>
public sealed class AnthropicClient(HttpClient http, IOptions<AnthropicOptions> options, ILogger<AnthropicClient> log) : IAiClient
{
    AnthropicOptions O => options.Value;
    public bool Enabled => !string.IsNullOrWhiteSpace(O.ApiKey);

    public async Task<string> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        if (!Enabled) throw new AiUnavailableException("AI isn't set up on this server. Add an Anthropic API key (Anthropic:ApiKey) and restart.");

        var content = new List<object>();
        foreach (var a in request.Attachments ?? [])
        {
            var data = Convert.ToBase64String(a.Data);
            content.Add(a.MediaType == FileRules.Pdf
                ? new { type = "document", source = new { type = "base64", media_type = a.MediaType, data } }
                : new { type = "image", source = new { type = "base64", media_type = a.MediaType, data } });
        }
        content.Add(new { type = "text", text = request.Prompt });

        var body = new Dictionary<string, object?>
        {
            ["model"] = O.Model,
            ["max_tokens"] = request.MaxTokens ?? O.MaxTokens,
            ["messages"] = new object[] { new { role = "user", content } }
        };
        if (request.System != null) body["system"] = request.System;

        using var msg = new HttpRequestMessage(HttpMethod.Post, O.BaseUrl) { Content = JsonContent.Create(body) };
        msg.Headers.Add("x-api-key", O.ApiKey);
        msg.Headers.Add("anthropic-version", O.Version);

        using var res = await http.SendAsync(msg, ct);
        var json = await res.Content.ReadAsStringAsync(ct);
        if (!res.IsSuccessStatusCode)
        {
            log.LogWarning("Anthropic API returned {Status}: {Body}", (int)res.StatusCode, json.Length > 500 ? json[..500] : json);
            throw new AiUnavailableException((int)res.StatusCode switch
            {
                401 => "The Anthropic API key was rejected. Check Anthropic:ApiKey.",
                413 => "That file is too large for the AI to read. Try a smaller file or split the statement.",
                429 => "The AI is busy or the usage limit was reached. Try again in a minute.",
                529 => "The AI service is overloaded. Try again in a minute.",
                _ => $"The AI request failed ({(int)res.StatusCode})."
            });
        }

        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text")
                sb.Append(block.GetProperty("text").GetString());
        if (doc.RootElement.TryGetProperty("stop_reason", out var stop) && stop.GetString() == "max_tokens")
            log.LogWarning("AI reply was cut off at max_tokens; consider raising Anthropic:MaxTokens.");
        return sb.ToString();
    }
}
