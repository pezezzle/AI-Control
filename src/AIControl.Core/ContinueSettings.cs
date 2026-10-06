using System.Text.Json.Nodes;

namespace AIControl;
public static class ContinueSettings
{
    public static string SyncContext(int context, string? path = null)
    {
        path ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".continue", "config.json");
        if (!File.Exists(path)) return "Continue config.json was not found; configure its context manually.";
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Continue configuration is empty.");
        bool changed = false;
        void Update(JsonNode? item)
        {
            if (item is not JsonObject obj || obj["apiBase"]?.GetValue<string>() is not string address || !Uri.TryCreate(address, UriKind.Absolute, out var uri) || !uri.IsLoopback || obj["model"]?.GetValue<string>() != "local-coder") return;
            obj["contextLength"] = context;
            if (obj["completionOptions"] is JsonObject options) options["maxTokens"] = Math.Min(8192, context / 2);
            changed = true;
        }
        if (root["models"] is JsonArray models) foreach (var model in models) Update(model);
        Update(root["tabAutocompleteModel"]);
        if (!changed) return "No matching local model found in Continue config.json.";
        File.Copy(path, path + ".before-ai-control-context", true);
        var temporary = path + ".aicontrol.tmp"; File.WriteAllText(temporary, root.ToJsonString(SettingsStore.Json)); File.Move(temporary, path, true);
        return "Continue context updated. Start a new conversation.";
    }
}
