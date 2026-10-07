using AIControl;
using System.Text.Json;
using System.Text.Json.Nodes;
internal static class SessionLiveChecks
{
    public static async Task OutputBudget(string evidence)
    {
        Directory.CreateDirectory(evidence);var workspace=Path.Combine(evidence,"synthetic-budget-workspace");Directory.CreateDirectory(workspace);
        var global=Path.Combine(evidence,"synthetic-budget-rules.md");File.WriteAllText(global,"Synthetic context measurement only. Do not access user projects or perform actions.");
        var settings=SettingsStore.Load() with{ProjectDirectory=workspace,Assistant=new()};
        using var control=new ServerControl(settings.Coding);var status=await control.ReadStatusAsync();
        if(!status.QueueKnown||!status.Healthy||status.HasJobs)throw new Exception("The live coding server must be healthy and idle for this read-only measurement.");
        using var chat=new AssistantChat(rulesPath:global);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var automatic=await chat.RefreshContextAsync(settings,16384,timeout.Token,automaticReplyBudget:true);
        var custom=await chat.RefreshContextAsync(settings,131072,timeout.Token,automaticReplyBudget:false);
        if(automatic.Used is not int used||!automatic.Exact||automatic.Reserve!=automatic.Capacity-used-ResponseBudget.SafetyTokens||automatic.Reserve<=8192||custom.Reserve!=Math.Min(131072,automatic.Reserve))throw new Exception("Live output budget does not match the server's measured available context.");
        File.WriteAllText(Path.Combine(evidence,"live-budget.json"),JsonSerializer.Serialize(new{passed=true,inferenceStarted=false,automatic,custom},SettingsStore.Json));
        Console.WriteLine($"Read-only live context check passed: {used} / {automatic.Capacity} input tokens; Automatic {automatic.Reserve}, Custom {custom.Reserve}. No inference started.");
    }
    public static async Task Run(string evidence)
    {
        Directory.CreateDirectory(evidence);var workspace=Path.Combine(evidence,"synthetic-session-workspace");Directory.CreateDirectory(workspace);
        var global=Path.Combine(evidence,"synthetic-session-rules.md");File.WriteAllText(global,"This is a synthetic feature test. Do not use tools. Follow the user's small request directly.");
        var settings=SettingsStore.Load() with{ProjectDirectory=workspace,Assistant=new()};
        using var chat=new AssistantChat(rulesPath:global);using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var measured=await chat.RefreshContextAsync(settings,2048,timeout.Token);var events=new List<object>();var text="";bool serverUsage=false;
        await chat.RunAsync(settings,"Reply with one short sentence confirming this synthetic chat test. Do not use tools.",[],(_,_)=>Task.FromResult(false),(type,value)=>{events.Add(new{type,value});if(type=="chatToken")text+=value;if(type=="contextUsage"&&value is ContextUsage u&&u.Source.StartsWith("Last server response"))serverUsage=true;},timeout.Token);
        if(string.IsNullOrWhiteSpace(text)||!serverUsage||chat.LastUsage?.Exact!=true)throw new Exception("Live response or measured server usage is missing.");
        var history=chat.ExportMemory().History;var system=(JsonObject)history[0].DeepClone();
        var source=string.Join(" ",Enumerable.Range(0,150).Select(i=>$"Synthetic decision {i}: keep the original blue sample, read only, and preserve all prior files. The next task is to report progress accurately."));
        chat.ImportMemory(new(){History=[system,new(){["role"]="user",["content"]=source},new(){["role"]="assistant",["content"]="Recorded these synthetic decisions."},new(){["role"]="user",["content"]="Recent synthetic question one"},new(){["role"]="assistant",["content"]="Recent answer one"},new(){["role"]="user",["content"]="Recent synthetic question two"},new(){["role"]="assistant",["content"]="Recent answer two"}]});
        var checkpoint=await chat.CompactAsync(settings,(type,value)=>events.Add(new{type,value}),timeout.Token);
        var store=new ChatSessionStore(Path.Combine(evidence,"saved-session-proof"));var session=ChatSessionStore.Create(settings);session.Title="Synthetic feature verification";session.Memory=chat.ExportMemory();session.Checkpoints.Add(checkpoint);session.Entries.Add(new(){Role="user",Text=source});store.Save(session);
        var loaded=store.Load().Single();if(loaded.Checkpoints.Count!=1||loaded.Memory.History.Count!=6||loaded.Entries[0].Text!=source||checkpoint.After>=checkpoint.Before)throw new Exception("Live compaction or persistent recovery failed.");
        File.WriteAllText(Path.Combine(evidence,"session-live.json"),JsonSerializer.Serialize(new{passed=true,measured,text,serverUsage,checkpoint.Before,checkpoint.After,events},SettingsStore.Json));
        Console.WriteLine($"Live counting, inference, compaction and persisted checkpoint passed: {checkpoint.Before} -> {checkpoint.After} tokens.");
    }
}
