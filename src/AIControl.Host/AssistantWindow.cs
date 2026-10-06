using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace AIControl;
public partial class MainWindow
{
    private readonly AssistantChat assistant = new();
    private bool chatBusy, speechBusy;
    private CancellationTokenSource? chatCancellation, speechCancellation;
    private TaskCompletionSource<bool>? pendingReview;
    private string? pendingReviewId;
    private async Task<bool> HandleAssistantAsync(JsonElement root)
    {
        var type=root.GetProperty("type").GetString();
        if(type=="chatCancel") { chatCancellation?.Cancel(); pendingReview?.TrySetResult(false); return true; }
        if(type=="speechCancel") { speechCancellation?.Cancel();return true; }
        if(type=="chatClear") { if(chatBusy)throw new InvalidOperationException("Cancel the current task first.");assistant.Clear();Send(new{type="chatCleared"});return true; }
        if(type=="editGlobalRules") { OpenPath(AgentRules.GlobalPath);return true; }
        if(type=="loadRules")
        {
            var rules=AgentRules.Load(new Workspace(settings.ProjectDirectory));Send(new{type="rulesLoaded",value=rules.Select(r=>r.Path).ToArray()});return true;
        }
        if(type=="assistantPermissions")
        {
            if(chatBusy)throw new InvalidOperationException("Finish or cancel the current task before changing permissions.");
            var access=root.GetProperty("access").GetString();if(access is not ("ReadOnly" or "ReadWrite"))throw new InvalidDataException("Invalid access mode.");
            settings=settings with{Assistant=settings.Assistant with{Access=Enum.Parse<WorkspaceAccess>(access),AllowScriptExecution=root.GetProperty("allowScripts").GetBoolean()}};
            SettingsStore.Save(settings);assistant.Clear();Send(new{type="chatCleared"});SendSnapshot();return true;
        }
        if(type=="reviewDecision")
        {
            if(pendingReview is null || root.GetProperty("id").GetString()!=pendingReviewId)throw new InvalidDataException("This review is no longer active.");
            pendingReview.TrySetResult(root.GetProperty("approved").GetBoolean());return true;
        }
        if(type=="transcribe")
        {
            if(speechBusy)throw new InvalidOperationException("Transcription is already running.");
            speechBusy=true;speechCancellation=new();
            try { Send(new{type="speechStatus",value="Transcribing locally…"});var text=await SpeechInput.TranscribeAsync(settings.Assistant,root.GetProperty("audio").GetString()??"",speechCancellation.Token);Send(new{type="speechResult",value=text}); }
            catch(OperationCanceledException){Send(new{type="speechError",value="Transcription canceled."});}
            catch(Exception ex){Send(new{type="speechError",value=FriendlyError(ex)});}
            finally{speechBusy=false;speechCancellation.Dispose();speechCancellation=null;}
            return true;
        }
        if(type!="chatSend")return false;
        if(chatBusy)throw new InvalidOperationException("An assistant request is already running.");
        if(!(await services["coding"].Control.ReadStatusAsync()).Healthy)throw new InvalidOperationException("Start the coding server first.");
        var prompt=root.GetProperty("prompt").GetString()??"";
        var images=root.GetProperty("images").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
        if(images.Length>0)
        {
            using var http=new System.Net.Http.HttpClient();
            using var props=JsonDocument.Parse(await http.GetStringAsync(settings.Coding.Url+"/props"));
            if(!props.RootElement.TryGetProperty("modalities",out var modalities) || !modalities.TryGetProperty("vision",out var vision) || !vision.GetBoolean())throw new InvalidOperationException("The running server has no image support. Restart it with the matching vision component enabled.");
        }
        chatBusy=true;chatCancellation=new();SendSnapshot();
        try
        {
            await assistant.RunAsync(settings,prompt,images,ReviewAsync,(eventType,value)=>Send(new{type=eventType,value}),chatCancellation.Token);
        }
        catch(OperationCanceledException){Send(new{type="chatError",value="Task canceled. Completed file changes remain saved."});}
        catch(Exception ex){Send(new{type="chatError",value=FriendlyError(ex)});Log("assistant",new InvalidOperationException(ex.GetType().Name+": "+FriendlyError(ex)));}
        finally{pendingReview=null;pendingReviewId=null;chatBusy=false;chatCancellation.Dispose();chatCancellation=null;SendSnapshot();}
        return true;
    }
    private async Task<bool> ReviewAsync(ToolReview review,CancellationToken token)
    {
        pendingReview=new(TaskCreationOptions.RunContinuationsAsynchronously);pendingReviewId=review.Id;
        Send(new{type="toolReview",value=review});
        try{return await pendingReview.Task.WaitAsync(token);}
        finally{pendingReview=null;pendingReviewId=null;Send(new{type="reviewClosed"});}
    }
}
