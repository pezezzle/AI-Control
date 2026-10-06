using AIControl;
using System.Text.Json;

internal static class AssistantLiveChecks
{
    public static async Task Run(string evidence)
    {
        evidence=Path.GetFullPath(evidence);Directory.CreateDirectory(evidence);
        var root=Path.Combine(evidence,"synthetic-workspace");Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root,"sample.txt"),"SYNTHETIC_VALUE=42");
        var rules=Path.Combine(evidence,"synthetic-global-AGENTS.md");File.WriteAllText(rules,"Use only the provided tools. Read the specified sample file before answering. This is a synthetic integration test.");
        var settings=SettingsStore.Load() with{ProjectDirectory=root,Assistant=new(){Access=WorkspaceAccess.ReadWrite}};
        var events=new List<object>();var answer="";
        using(var chat=new AssistantChat(rulesPath:rules))
            await chat.RunAsync(settings,"Read sample.txt using read_file. Then write result.txt containing exactly SYNTHETIC_OK using write_file. Do not use any other files or commands.",[],(review,_)=>Task.FromResult(review.Kind=="write_file"&&review.Path=="result.txt"&&review.After=="SYNTHETIC_OK"),(type,value)=>{events.Add(new{type,value});if(type=="chatToken")answer+=value;},CancellationToken.None);
        if(!File.Exists(Path.Combine(root,"result.txt"))||File.ReadAllText(Path.Combine(root,"result.txt"))!="SYNTHETIC_OK")throw new Exception("Live approved write failed.");
        File.WriteAllText(Path.Combine(evidence,"assistant-live.json"),JsonSerializer.Serialize(new{events,answer,passed=true},SettingsStore.Json));
        Console.WriteLine("Live local tools and approved synthetic write passed.");
        using(var chat=new AssistantChat(rulesPath:rules))
        {
            answer="";var image="data:image/png;base64,"+Convert.ToBase64String(File.ReadAllBytes(Path.Combine(evidence,"vision-test.png")));
            await chat.RunAsync(settings,"Look only at the attached image. State its background color and the word shown. Do not use tools.",[image],(_,_)=>Task.FromResult(false),(type,value)=>{if(type=="chatToken")answer+=value;},CancellationToken.None);
            File.WriteAllText(Path.Combine(evidence,"vision-live.txt"),answer);
            if(!answer.Contains("VISION",StringComparison.OrdinalIgnoreCase))throw new Exception("Live image recognition did not identify the synthetic test word: "+answer);
            Console.WriteLine("Live image recognition passed: "+answer);
        }
        await Speech(evidence);
        Console.WriteLine("Current model context limit: "+ModelMetadata.ContextLimit(Path.Combine(settings.Coding.Root,"models",settings.Coding.Model)));
    }
    public static async Task Speech(string evidence)
    {
        var audio=Convert.ToBase64String(File.ReadAllBytes(Path.Combine(evidence,"speech-test-pcm.wav")));
        var transcription=await SpeechInput.TranscribeAsync(new(),audio,CancellationToken.None);File.WriteAllText(Path.Combine(evidence,"speech-live.txt"),transcription);
        if(!transcription.Contains("country",StringComparison.OrdinalIgnoreCase))throw new Exception("Local speech transcription test failed: "+transcription);
        Console.WriteLine("Local Whisper transcription passed: "+transcription);
    }
    public static async Task Recovery(string evidence)
    {
        evidence=Path.GetFullPath(evidence);Directory.CreateDirectory(evidence);
        var root=Path.Combine(evidence,"synthetic-path-recovery-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var rules=Path.Combine(evidence,"recovery-global-AGENTS.md");File.WriteAllText(rules,"Use the provided tools only for this synthetic test. Every file write needs approval. Do not execute scripts.");
        var options=SettingsStore.Load() with{ProjectDirectory=root,Assistant=new(){Access=WorkspaceAccess.ReadWrite}};
        var events=new List<object>();var reviews=new List<ToolReview>();using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var chat=new AssistantChat(rulesPath:rules);
        var absolute=Path.Combine(root,"hello.txt");
        await chat.RunAsync(options,"This is a short synthetic tool test. First list_files with path empty string. Then write_file using this absolute path: "+absolute+" . The complete content must be exactly APP_PATH_OK. Finally answer DONE. Do not read files or run scripts.",[],(review,_)=>{reviews.Add(review);return Task.FromResult(review.Kind=="write_file"&&review.Path=="hello.txt"&&review.Before is null&&review.After=="APP_PATH_OK");},(type,value)=>events.Add(new{type,value}),deadline.Token);
        var passed=File.Exists(absolute)&&File.ReadAllText(absolute)=="APP_PATH_OK"&&reviews.Count==1;
        File.WriteAllText(Path.Combine(evidence,"assistant-path-recovery-live.json"),JsonSerializer.Serialize(new{passed,root,reviews,events},SettingsStore.Json));
        if(!passed)throw new Exception("Live path recovery did not produce the exact reviewed synthetic file.");
        Console.WriteLine("Live empty-workspace and reviewed absolute-path write passed.");
    }
}
