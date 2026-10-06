using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIControl;

public enum ServiceKind { ComfyUI, Llama }
public sealed record ServiceDefinition
{
    public ServiceKind Kind { get; init; }
    public string InstallDirectory { get; init; } = "";
    public int Port { get; init; }
    public string Model { get; init; } = "qwen2.5-coder-14b-instruct.gguf";
    public int ContextSize { get; init; } = 32768;
    public string GpuLayers { get; init; } = "auto";
    [JsonIgnore] public string Id => Kind == ServiceKind.ComfyUI ? "comfy" : "coding";
    [JsonIgnore] public string Name => Kind == ServiceKind.ComfyUI ? "ComfyUI" : "Coding server";
    [JsonIgnore] public string Url => $"http://127.0.0.1:{Port}";
    [JsonIgnore] public string Root => Path.GetFullPath(Environment.ExpandEnvironmentVariables(InstallDirectory));
    [JsonIgnore] public string LogDirectory => Path.Combine(SettingsStore.DataDirectory, Id);
    public IEnumerable<string> ListModels() => Directory.Exists(Path.Combine(Root, "models"))
        ? Directory.EnumerateFiles(Path.Combine(Root, "models"), "*.gguf").Select(Path.GetFileName).OfType<string>().Order() : [];
    public void Validate()
    {
        if (Port is < 1024 or > 65535) throw new InvalidDataException("The server port must be between 1024 and 65535.");
        var expanded = Environment.ExpandEnvironmentVariables(InstallDirectory);
        if (!Path.IsPathFullyQualified(expanded)) throw new InvalidDataException("Choose an absolute installation path.");
        if (Kind == ServiceKind.Llama && (string.IsNullOrWhiteSpace(Model) || Model != Path.GetFileName(Model) || Model.Contains('\\') || Model.Contains('/') || !Model.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Choose a GGUF file from the model folder.");
        if (ContextSize is < 2048 or > 131072) throw new InvalidDataException("Invalid context size.");
        if (GpuLayers != "auto" && GpuLayers != "all" && (!int.TryParse(GpuLayers, out var count) || count is < 0 or > 999))
            throw new InvalidDataException("Invalid GPU allocation.");
    }
}
public sealed record ControlSettings
{
    public ServiceDefinition ComfyUI { get; init; } = new() { Kind = ServiceKind.ComfyUI, InstallDirectory = @"%USERPROFILE%\Coding\ComfyUI", Port = 8188 };
    public ServiceDefinition Coding { get; init; } = new() { Kind = ServiceKind.Llama, InstallDirectory = @"%USERPROFILE%\Coding\LocalAI-Worker", Port = 8080 };
    public string ProjectDirectory { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Coding");
    public void Validate()
    {
        ComfyUI.Validate(); Coding.Validate();
        if (ComfyUI.Kind != ServiceKind.ComfyUI || Coding.Kind != ServiceKind.Llama || ComfyUI.Port == Coding.Port)
            throw new InvalidDataException("The two servers require distinct service types and ports.");
        if (!Path.IsPathFullyQualified(ProjectDirectory)) throw new InvalidDataException("Choose an absolute project path.");
    }
}
public static class SettingsStore
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIControl");
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static ControlSettings Load(string? path = null)
    {
        path ??= SettingsPath;
        var settings = File.Exists(path) ? JsonSerializer.Deserialize<ControlSettings>(File.ReadAllText(path), Json) ?? throw new InvalidDataException("Empty settings.") : new();
        settings.Validate(); return settings;
    }
    public static void Save(ControlSettings settings, string? path = null)
    {
        settings.Validate(); path ??= SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        File.Move(temporary, path, true);
    }
}
public static class BridgePolicy
{
    public const string Origin = "https://ai-control.local";
    public static bool IsTrustedSource(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host == "ai-control.local" && uri.IsDefaultPort && uri.UserInfo.Length == 0;
}
