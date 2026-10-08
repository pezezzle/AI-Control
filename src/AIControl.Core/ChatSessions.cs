using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIControl;

public enum CompactionMode { Automatic, Manual, Off }
public sealed record AssistantTaskProgress(int Round, string Phase, int CompletedTools, int SavedFiles, string LastAction, int Recoveries, bool ThinkingEnabled, string Request="");
public sealed record ContextUsage(int? Used, int Capacity, int Reserve, bool Exact, string Source)
{
    public double? Percent => Used is int n && Capacity > 0 ? 100.0 * n / Capacity : null;
}
public sealed record ChatMemory
{
    public string SessionKey { get; init; } = "";
    public List<JsonObject> History { get; init; } = [];
    public ContextUsage? Usage { get; init; }
}
public sealed record ChatEntry
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Role { get; init; } = "activity";
    public string Text { get; set; } = "";
    public string[] Images { get; init; } = [];
    public int Round { get; init; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
}
public sealed record ContextCheckpoint
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;
    public int Before { get; init; }
    public int After { get; init; }
    public ChatMemory Memory { get; init; } = new();
}
public sealed record ChatSession
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "New chat";
    public string Workspace { get; set; } = "";
    public string Model { get; set; } = "";
    public WorkspaceAccess Access { get; set; } = WorkspaceAccess.ReadOnly;
    public bool AllowScripts { get; set; }
    public CompactionMode Compaction { get; set; } = CompactionMode.Automatic;
    public int ReplyReserve { get; set; } = 16384;
    public bool AutomaticReplyBudget { get; set; } = true;
    public bool ThinkingEnabled { get; set; } = true;
    public AssistantTaskProgress? Progress { get; set; }
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.UtcNow;
    public bool Deleted { get; set; }
    public string Status { get; set; } = "Ready";
    public List<ChatEntry> Entries { get; init; } = [];
    public ChatMemory Memory { get; set; } = new();
    public List<ContextCheckpoint> Checkpoints { get; init; } = [];
    public void Validate()
    {
        if (!Guid.TryParseExact(Id,"N",out _) || Title is null || Title.Length is <1 or >100 || !Path.IsPathFullyQualified(Workspace)
            || string.IsNullOrWhiteSpace(Model) || Model!=Path.GetFileName(Model) || !Model.EndsWith(".gguf",StringComparison.OrdinalIgnoreCase)
            || !Enum.IsDefined(Access) || !Enum.IsDefined(Compaction) || ReplyReserve is <1 or >1048576)
            throw new InvalidDataException("Invalid saved chat settings.");
        if(Entries is null || Checkpoints is null)throw new InvalidDataException("Missing chat data.");
        if(Progress is {} progress && (progress.Round<0 || progress.CompletedTools<0 || progress.SavedFiles<0 || progress.Recoveries<0 || progress.Phase is null || progress.Phase.Length>100 || progress.LastAction is null || progress.LastAction.Length>600 || progress.Request is null || progress.Request.Length>200))throw new InvalidDataException("Invalid task progress.");
        AssistantChat.ValidateMemory(Memory);
        foreach(var checkpoint in Checkpoints) { if(checkpoint is null||checkpoint.Before<0||checkpoint.After<0||!Guid.TryParseExact(checkpoint.Id,"N",out _))throw new InvalidDataException("Invalid checkpoint.");AssistantChat.ValidateMemory(checkpoint.Memory); }
        if(Entries.Any(e=>e is null || e.Text is null || e.Images is null || e.Round<0 || e.Role is not ("user" or "assistant" or "activity") || e.Text.Length>ResponseBudget.MaxMessageCharacters || e.Images.Length>4 || e.Images.Any(i=>i is null||!AssistantChat.ValidImage(i))))
            throw new InvalidDataException("Invalid chat transcript.");
    }
}
public sealed class ChatSessionStore
{
    public string Root { get; }
    public List<string> Warnings { get; } = [];
    public ChatSessionStore(string? root=null)
    {
        Root=Path.GetFullPath(root??Path.Combine(SettingsStore.DataDirectory,"chats"));Directory.CreateDirectory(Root);
        for(var dir=new DirectoryInfo(Root);dir is not null;dir=dir.Parent)
            if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked chat storage is not supported.");
    }
    private string FilePath(string id)
    {
        if(!Guid.TryParseExact(id,"N",out _))throw new InvalidDataException("Invalid chat identity.");
        var path=Path.Combine(Root,id+".json");
        if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Linked chat files are blocked.");
        return path;
    }
    public List<ChatSession> Load()
    {
        Warnings.Clear();var result=new List<ChatSession>();
        foreach(var path in Directory.EnumerateFiles(Root,"*.json"))
        {
            try
            {
                var id=Path.GetFileNameWithoutExtension(path);FilePath(id);
                if(new FileInfo(path).Length>128_000_000)throw new InvalidDataException("Chat exceeds the 128 MB per-file limit.");
                var session=JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(path),SettingsStore.Json)??throw new InvalidDataException("Empty chat.");
                if(session.Id!=id)throw new InvalidDataException("Chat identity does not match its file.");session.Validate();
                if(session.Status is "Running" or "Queued" or "Compacting" or "Waiting for approval")
                {
                    session.Status="Interrupted";session.Entries.Add(new(){Text="The previous task was interrupted. It was not resumed; earlier completed file changes remain saved. Pending approvals are invalid."});Save(session);
                }
                result.Add(session);
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NullReferenceException)
            { Warnings.Add("A saved chat could not be loaded: "+Path.GetFileName(path)+". It was preserved. "+ex.Message); }
        }
        return result.OrderByDescending(s=>s.Updated).ToList();
    }
    public void Save(ChatSession session)
    {
        session.Validate();var data=JsonSerializer.SerializeToUtf8Bytes(session,SettingsStore.Json);
        if(data.Length>128_000_000)throw new InvalidDataException("This chat exceeds the 128 MB storage limit. Export or start a new chat; history will not be discarded.");
        var path=FilePath(session.Id);var temporary=Path.Combine(Root,session.Id+"."+Guid.NewGuid().ToString("N")+".tmp");
        try { using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}FilePath(session.Id);File.Move(temporary,path,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public static ChatSession Create(ControlSettings settings)=>new(){Workspace=settings.ProjectDirectory,Model=settings.Coding.Model,Access=settings.Assistant.Access,AllowScripts=settings.Assistant.AllowScriptExecution};
}
