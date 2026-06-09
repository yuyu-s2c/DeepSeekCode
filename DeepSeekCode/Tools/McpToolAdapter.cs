using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using DeepSeekCode.MCP;
using DeepSeekCode.Models;

namespace DeepSeekCode.Tools;

/// <summary>将 MCP 工具适配为 DeepSeekCode ITool 接口</summary>
public class McpToolAdapter : ITool
{
    private readonly McpClient _client;
    private readonly McpToolInfo _info;
    private readonly string _serverName;

    public string Name { get; }
    public string Description { get; }

    public ParameterSchema Parameters
    {
        get
        {
            if (_info.InputSchema is { } schema && schema.ValueKind != JsonValueKind.Null)
                return ParseSchema(schema);
            return new ParameterSchema { Type = "object" };
        }
    }

    public McpToolAdapter(McpClient client, McpToolInfo info, string fullName, string serverName)
    {
        _client = client;
        _info = info;
        _serverName = serverName;
        Name = fullName;
        Description = $"[MCP:{serverName}] {info.Description ?? info.Name}";
    }

    public ToolDefinition ToDefinition() => new()
    {
        Type = "function",
        Function = new FunctionDefinition
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters
        }
    };

    public async Task<string> ExecuteAsync(Dictionary<string, object?> arguments)
    {
        var result = await _client.CallToolAsync(_info.Name, arguments);
        return result;
    }

    private static ParameterSchema ParseSchema(JsonElement schema)
    {
        var properties = new Dictionary<string, PropertySchema>();
        var required = new List<string>();

        if (schema.TryGetProperty("required", out var reqArr) &&
            reqArr.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in reqArr.EnumerateArray())
                required.Add(item.GetString() ?? "");
        }

        if (schema.TryGetProperty("properties", out var props) &&
            props.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in props.EnumerateObject())
            {
                var propSchema = prop.Value;
                var propDef = new PropertySchema
                {
                    Type = propSchema.TryGetProperty("type", out var t) ? t.GetString() ?? "string" : "string",
                    Description = propSchema.TryGetProperty("description", out var d) ? d.GetString() ?? "" : ""
                };
                properties[prop.Name] = propDef;
            }
        }

        return new ParameterSchema
        {
            Type = "object",
            Properties = properties,
            Required = required.Count > 0 ? required : null
        };
    }
}
