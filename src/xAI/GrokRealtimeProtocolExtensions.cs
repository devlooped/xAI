using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace xAI;

/// <summary>Realtime protocol conversions shared by the xAI MEAI adapter.</summary>
public static partial class GrokProtocolExtensions
{
    internal static JsonObject ToRealtimeSessionUpdate(this RealtimeSessionOptions options)
    {
        var session = new JsonObject();
        var voice = options is GrokRealtimeOptions { CustomVoiceId: { Length: > 0 } customVoice }
            ? customVoice
            : options.Voice;

        if (voice is not null)
            session["voice"] = voice;
        if (options.Instructions is not null)
            session["instructions"] = options.Instructions;
        if (options.MaxOutputTokens is int maxOutputTokens)
            session["max_output_tokens"] = maxOutputTokens;

        if (options.InputAudioFormat is not null || options.OutputAudioFormat is not null)
        {
            var audio = new JsonObject();
            if (options.InputAudioFormat is { } input)
                audio["input"] = ToRealtimeAudioConfig(input);
            if (options.OutputAudioFormat is { } output)
                audio["output"] = ToRealtimeAudioConfig(output);
            session["audio"] = audio;
        }

        if (options.VoiceActivityDetection is { } vad)
            session["turn_detection"] = vad.Enabled ? new JsonObject { ["type"] = "server_vad" } : null;

        if (options is GrokRealtimeOptions { EnableResumption: bool resumption })
            session["resumption"] = new JsonObject { ["enabled"] = resumption };

        if (options.Tools is { Count: > 0 } tools)
        {
            var toolArray = new JsonArray();
            foreach (var tool in tools)
                toolArray.Add(ToRealtimeTool(tool));
            session["tools"] = toolArray;
        }

        return new JsonObject
        {
            ["type"] = "session.update",
            ["session"] = session,
        };
    }

    internal static JsonObject ToRealtimeAudioAppend(this InputAudioBufferAppendRealtimeClientMessage audio)
    {
        var data = audio.Content.Base64Data.ToString();
        if (string.IsNullOrEmpty(data))
            throw new NotSupportedException("xAI realtime audio input requires inline audio data.");

        return new JsonObject
        {
            ["type"] = "input_audio_buffer.append",
            ["audio"] = data,
        };
    }

    internal static JsonObject ToRealtimeConversationItem(this RealtimeConversationItem item)
    {
        if (item.Contents.Count == 1 && item.Contents[0] is FunctionCallContent functionCall)
        {
            var arguments = functionCall.Arguments is null ? "{}" : JsonSerializer.Serialize(functionCall.Arguments);
            return new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "function_call",
                    ["call_id"] = functionCall.CallId,
                    ["name"] = functionCall.Name,
                    ["arguments"] = arguments,
                },
            };
        }

        if (item.Contents.Count == 1 && item.Contents[0] is FunctionResultContent functionResult)
        {
            var output = functionResult.Result is string resultText
                ? resultText
                : JsonSerializer.Serialize(functionResult.Result);
            return new JsonObject
            {
                ["type"] = "conversation.item.create",
                ["item"] = new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = functionResult.CallId,
                    ["output"] = output,
                },
            };
        }

        var content = new JsonArray();
        foreach (var itemContent in item.Contents)
        {
            if (itemContent is not TextContent text)
                throw new NotSupportedException($"xAI realtime conversation items do not support {itemContent.GetType().Name} through the MEAI text-item mapping.");

            content.Add(new JsonObject
            {
                ["type"] = item.Role == ChatRole.User ? "input_text" : "text",
                ["text"] = text.Text,
            });
        }

        var conversationItem = new JsonObject
        {
            ["type"] = "message",
            ["role"] = (item.Role ?? ChatRole.User).ToString().ToLowerInvariant(),
            ["content"] = content,
        };
        if (item.Id is not null)
            conversationItem["id"] = item.Id;

        return new JsonObject
        {
            ["type"] = "conversation.item.create",
            ["item"] = conversationItem,
        };
    }

    internal static JsonObject ToRealtimeResponse(this CreateResponseRealtimeClientMessage response)
    {
        if (response.Items is { Count: > 0 })
            throw new NotSupportedException("xAI realtime does not document response.create input items; send them as conversation.item.create messages first.");

        var json = new JsonObject { ["type"] = "response.create" };
        var options = new JsonObject();
        if (response.Instructions is not null)
            options["instructions"] = response.Instructions;
        if (response.MaxOutputTokens is int maxOutputTokens)
            options["max_output_tokens"] = maxOutputTokens;
        if (response.OutputVoice is not null)
            options["voice"] = response.OutputVoice;
        if (response.OutputAudioOptions is { } audio)
            options["audio"] = new JsonObject { ["output"] = ToRealtimeAudioConfig(audio) };
        if (response.ExcludeFromConversation is bool exclude)
            options["conversation"] = exclude ? "none" : "auto";

        if (response.AdditionalProperties is { Count: > 0 } additionalProperties)
        {
            foreach (var (key, value) in additionalProperties)
                options[key] = JsonSerializer.SerializeToNode(value);
        }

        if (options.Count > 0)
            json["response"] = options;
        return json;
    }

    internal static JsonObject ToRealtimeRawMessage(this RealtimeClientMessage message)
    {
        if (message.RawRepresentation is JsonElement element && element.ValueKind == JsonValueKind.Object)
            return JsonNode.Parse(element.GetRawText())!.AsObject();
        if (message.RawRepresentation is JsonObject node)
            return (JsonObject)node.DeepClone();
        if (message.RawRepresentation is string raw)
            return JsonNode.Parse(raw)?.AsObject() ?? throw new JsonException("Raw realtime message must be a JSON object.");

        throw new NotSupportedException($"xAI realtime does not support client message type {message.GetType().Name} without a raw JSON object representation.");
    }

    internal static RealtimeServerMessage ToRealtimeServerMessage(this JsonElement root)
    {
        var raw = root.Clone();
        var type = root.TryGetProperty("type", out var typeValue) ? typeValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("xAI realtime server message is missing its event type.");

        var messageId = GetString(root, "event_id");
        RealtimeServerMessage message = type switch
        {
            "response.created" => new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseCreated)
            {
                ResponseId = GetNestedString(root, "response", "id"),
            },
            "response.done" => new ResponseCreatedRealtimeServerMessage(RealtimeServerMessageType.ResponseDone)
            {
                ResponseId = GetNestedString(root, "response", "id"),
            },
            "response.output_text.delta" => CreateTextAudioMessage(RealtimeServerMessageType.OutputTextDelta, root, "delta"),
            "response.output_text.done" => CreateTextAudioMessage(RealtimeServerMessageType.OutputTextDone, root, "text"),
            "response.output_audio_transcription.delta" or "response.audio_transcript.delta" =>
                CreateTextAudioMessage(RealtimeServerMessageType.OutputAudioTranscriptionDelta, root, "delta"),
            "response.output_audio_transcription.done" or "response.audio_transcript.done" =>
                CreateTextAudioMessage(RealtimeServerMessageType.OutputAudioTranscriptionDone, root, "transcript"),
            "response.output_audio.delta" or "response.audio.delta" =>
                CreateTextAudioMessage(RealtimeServerMessageType.OutputAudioDelta, root, "delta", audio: true),
            "response.output_audio.done" or "response.audio.done" =>
                CreateTextAudioMessage(RealtimeServerMessageType.OutputAudioDone, root, "audio", audio: true),
            "response.output_item.added" => CreateOutputItemMessage(RealtimeServerMessageType.ResponseOutputItemAdded, root),
            "response.output_item.done" => CreateOutputItemMessage(RealtimeServerMessageType.ResponseOutputItemDone, root),
            "response.function_call_arguments.done" => CreateFunctionCallMessage(root),
            "error" => new ErrorRealtimeServerMessage
            {
                Error = new ErrorContent(GetNestedString(root, "error", "message") ?? GetString(root, "message") ?? "xAI realtime server error."),
                OriginatingMessageId = GetNestedString(root, "error", "event_id"),
            },
            _ => new RealtimeServerMessage { Type = new RealtimeServerMessageType(type) },
        };

        message.MessageId = messageId;
        message.RawRepresentation = raw;
        return message;
    }

    static JsonObject ToRealtimeAudioConfig(RealtimeAudioFormat format)
    {
        var audioFormat = new JsonObject { ["type"] = format.MediaType };
        if (format.SampleRate is int sampleRate)
            audioFormat["rate"] = sampleRate;
        return new JsonObject { ["format"] = audioFormat };
    }

    static JsonObject ToRealtimeTool(AITool tool) => tool switch
    {
        AIFunction function => new JsonObject
        {
            ["type"] = "function",
            ["name"] = function.Name,
            ["description"] = function.Description,
            ["parameters"] = JsonSerializer.SerializeToNode(function.JsonSchema),
        },
        GrokXSearchTool xSearch => ToRealtimeXSearchTool(xSearch),
        GrokSearchTool webSearch => ToRealtimeWebSearchTool(webSearch),
        HostedWebSearchTool => new JsonObject { ["type"] = "web_search" },
        HostedFileSearchTool fileSearch => ToRealtimeFileSearchTool(fileSearch),
        HostedMcpServerTool mcp => ToRealtimeMcpTool(mcp),
        _ => throw new NotSupportedException($"xAI realtime does not support the MEAI tool type {tool.GetType().Name}."),
    };

    static JsonObject ToRealtimeWebSearchTool(GrokSearchTool tool)
    {
        if (tool.AllowedDomains is { Count: > 0 } && tool.ExcludedDomains is { Count: > 0 })
            throw new NotSupportedException("xAI realtime does not allow allowed and excluded web-search domains together.");

        var result = new JsonObject { ["type"] = "web_search" };
        if (tool.AllowedDomains is { } allowedDomains)
            result["allowed_domains"] = JsonSerializer.SerializeToNode(allowedDomains);
        if (tool.ExcludedDomains is { } excludedDomains)
            result["excluded_domains"] = JsonSerializer.SerializeToNode(excludedDomains);
        if (tool.EnableImageUnderstanding)
            result["enable_image_understanding"] = true;
        if (tool.Country is not null || tool.Region is not null || tool.City is not null || tool.Timezone is not null)
        {
            var location = new JsonObject();
            if (tool.Country is not null)
                location["country"] = tool.Country;
            if (tool.Region is not null)
                location["region"] = tool.Region;
            if (tool.City is not null)
                location["city"] = tool.City;
            if (tool.Timezone is not null)
                location["timezone"] = tool.Timezone;
            result["location"] = location;
        }

        if (tool.EnableImageSearch)
            throw new NotSupportedException("xAI realtime web search does not document the MEAI image-search option.");

        return result;
    }

    static JsonObject ToRealtimeXSearchTool(GrokXSearchTool tool)
    {
        if (tool.AllowedHandles is { Count: > 0 } && tool.ExcludedHandles is { Count: > 0 })
            throw new NotSupportedException("xAI realtime does not allow allowed and excluded X handles together.");

        var result = new JsonObject { ["type"] = "x_search" };
        if (tool.AllowedHandles is { } allowedHandles)
            result["allowed_x_handles"] = JsonSerializer.SerializeToNode(allowedHandles);
        if (tool.ExcludedHandles is { } excludedHandles)
            result["excluded_x_handles"] = JsonSerializer.SerializeToNode(excludedHandles);
        if (tool.FromDate is DateOnly fromDate)
            result["from_date"] = fromDate.ToString("yyyy-MM-dd");
        if (tool.ToDate is DateOnly toDate)
            result["to_date"] = toDate.ToString("yyyy-MM-dd");
        if (tool.EnableImageUnderstanding)
            result["enable_image_understanding"] = true;
        if (tool.EnableVideoUnderstanding)
            result["enable_video_understanding"] = true;
        return result;
    }

    static JsonObject ToRealtimeFileSearchTool(HostedFileSearchTool tool)
    {
        var storeIds = tool.Inputs?.OfType<HostedVectorStoreContent>()
            .Select(content => content.VectorStoreId)
            .Distinct()
            .ToArray() ?? [];

        if (storeIds.Length == 0)
            throw new NotSupportedException("xAI realtime file_search requires one or more MEAI HostedVectorStoreContent inputs.");

        var result = new JsonObject
        {
            ["type"] = "file_search",
            ["vector_store_ids"] = JsonSerializer.SerializeToNode(storeIds),
        };
        if (tool.MaximumResultCount is int maximumResultCount)
            result["max_num_results"] = maximumResultCount;
        return result;
    }

    static JsonObject ToRealtimeMcpTool(HostedMcpServerTool tool)
    {
        var result = new JsonObject
        {
            ["type"] = "mcp",
            ["server_url"] = tool.ServerAddress,
            ["server_label"] = tool.ServerName,
        };
        if (tool.AllowedTools is { Count: > 0 } allowedTools)
            result["allowed_tools"] = JsonSerializer.SerializeToNode(allowedTools);
        if (tool.Headers is { Count: > 0 } headers)
        {
            foreach (var (key, value) in headers)
            {
                if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                    result["authorization"] = value;
                else
                    (result["headers"] ??= new JsonObject())[key] = value;
            }
        }

        return result;
    }

    static ResponseOutputItemRealtimeServerMessage CreateOutputItemMessage(RealtimeServerMessageType type, JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("xAI realtime output-item event did not contain an item object.");

        var contents = new List<AIContent>();
        var id = GetString(item, "id");
        ChatRole? role = null;

        switch (GetString(item, "type"))
        {
            case "function_call":
                var callId = GetString(item, "call_id") ?? "";
                var name = GetString(item, "name") ?? "";
                var arguments = GetString(item, "arguments");
                contents.Add(new FunctionCallContent(callId, name,
                    arguments is { Length: > 0 } ? JsonSerializer.Deserialize<IDictionary<string, object?>>(arguments) : null)
                {
                    RawRepresentation = item.Clone(),
                });
                break;

            case "message":
                role = GetString(item, "role") switch
                {
                    "assistant" => ChatRole.Assistant,
                    "user" => ChatRole.User,
                    "system" or "developer" => ChatRole.System,
                    _ => null,
                };
                if (item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in content.EnumerateArray())
                    {
                        if (GetString(part, "text") is { } text)
                            contents.Add(new TextContent(text));
                    }
                }
                break;
        }

        var conversationItem = contents.Count == 0 ? null : new RealtimeConversationItem(contents, id, role)
        {
            RawRepresentation = item.Clone(),
        };

        return new ResponseOutputItemRealtimeServerMessage(type)
        {
            ResponseId = GetString(root, "response_id"),
            OutputIndex = GetInt(root, "output_index"),
            Item = conversationItem,
        };
    }

    static ResponseOutputItemRealtimeServerMessage CreateFunctionCallMessage(JsonElement root)
    {
        var callId = GetString(root, "call_id") ?? "";
        var name = GetString(root, "name") ?? "";
        var arguments = GetString(root, "arguments");
        var item = new RealtimeConversationItem(
            [new FunctionCallContent(callId, name,
                arguments is { Length: > 0 } ? JsonSerializer.Deserialize<IDictionary<string, object?>>(arguments) : null)
            {
                RawRepresentation = root.Clone(),
            }]);

        return new ResponseOutputItemRealtimeServerMessage(RealtimeServerMessageType.ResponseOutputItemDone)
        {
            ResponseId = GetString(root, "response_id"),
            OutputIndex = GetInt(root, "output_index"),
            Item = item,
        };
    }

    static OutputTextAudioRealtimeServerMessage CreateTextAudioMessage(
        RealtimeServerMessageType type,
        JsonElement root,
        string valueProperty,
        bool audio = false)
    {
        var message = new OutputTextAudioRealtimeServerMessage(type)
        {
            ContentIndex = GetInt(root, "content_index"),
            ItemId = GetString(root, "item_id"),
            OutputIndex = GetInt(root, "output_index"),
            ResponseId = GetString(root, "response_id"),
        };

        if (audio)
            message.Audio = GetString(root, valueProperty);
        else
            message.Text = GetString(root, valueProperty);

        return message;
    }

    static string? GetString(JsonElement json, string property) =>
        json.ValueKind == JsonValueKind.Object &&
        json.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static string? GetNestedString(JsonElement json, string objectProperty, string property) =>
        json.TryGetProperty(objectProperty, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? GetString(nested, property)
            : null;

    static int? GetInt(JsonElement json, string property) =>
        json.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : null;
}
