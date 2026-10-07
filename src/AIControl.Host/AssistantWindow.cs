using System.IO;
using System.Text;
using System.Text.Json;

namespace AIControl;
public partial class MainWindow
{
    private readonly ChatSessionStore chatStore=new();
    private readonly Dictionary<string,SessionRuntime> chats=[];
    private readonly Queue<ChatJob> chatQueue=[];
    private string selectedChat="";
    private ChatJob? activeJob;
    private bool chatBusy, speechBusy;
    private CancellationTokenSource? chatCancellation,speechCancellation;
    private TaskCompletionSource<bool>? pendingReview;
    private string? pendingReviewId;
    private sealed class SessionRuntime(ChatSession data):IDisposable
    {
        public ChatSession Data {get;}=data;
        public AssistantChat Chat {get;}=new();
        public void Dispose()=>Chat.Dispose();
    }
    private sealed record ChatJob(SessionRuntime Runtime,ControlSettings Settings,string Prompt,string[] Images,bool Compact,CancellationTokenSource Cancellation);
    private SessionRuntime CurrentChat=>chats[selectedChat];
    private void InitializeChats()
    {
        foreach(var data in chatStore.Load()){var runtime=new SessionRuntime(data);runtime.Chat.ImportMemory(data.Memory);chats.Add(data.Id,runtime);}
        var latest=chats.Values.Where(s=>!s.Data.Deleted).OrderByDescending(s=>s.Data.Updated).FirstOrDefault();
        if(latest is null)NewChat();else selectedChat=latest.Data.Id;
        if(chatStore.Warnings.Count>0)notice=string.Join("\n",chatStore.Warnings);
    }
    private void SaveChat(SessionRuntime runtime,bool memory=false)
    {
        if(memory)runtime.Data.Memory=runtime.Chat.ExportMemory();runtime.Data.Updated=DateTimeOffset.UtcNow;chatStore.Save(runtime.Data);
    }
    private void NewChat()
    {
        var defaults=selectedChat.Length>0?settings with{ProjectDirectory=CurrentChat.Data.Workspace}:settings;
        var data=ChatSessionStore.Create(defaults);var runtime=new SessionRuntime(data);chats.Add(data.Id,runtime);selectedChat=data.Id;SaveChat(runtime);SendChats();
    }
    private void SendChats(bool transcript=true)
    {
        Send(new{type="chatSessions",selectedId=selectedChat,activeId=activeJob?.Runtime.Data.Id,
            sessions=chats.Values.Where(s=>!s.Data.Deleted).OrderByDescending(s=>s.Data.Updated).Select(s=>new{s.Data.Id,s.Data.Title,s.Data.Status,s.Data.Workspace,s.Data.Model}).ToArray(),
            current=transcript&&chats.TryGetValue(selectedChat,out var current)?ChatView(current.Data):null});
    }
    private static object ChatView(ChatSession data)=>new{data.Id,data.Title,data.Workspace,data.Model,data.Access,data.AllowScripts,data.Compaction,data.ReplyReserve,data.AutomaticReplyBudget,data.Status,data.Entries,
        memory=new{data.Memory.Usage},checkpoints=data.Checkpoints.Select(c=>new{c.Id,c.Created,c.Before,c.After}).ToArray()};
    private ControlSettings ChatSettings(ChatSession data)=>settings with{ProjectDirectory=data.Workspace,Coding=settings.Coding with{Model=data.Model},Assistant=settings.Assistant with{Access=data.Access,AllowScriptExecution=data.AllowScripts}};
    private SessionRuntime MessageChat(JsonElement root)
    {
        var id=root.TryGetProperty("sessionId",out var value)?value.GetString():selectedChat;
        if(id is null||!chats.TryGetValue(id,out var runtime)||runtime.Data.Deleted)throw new InvalidDataException("This chat is no longer available.");return runtime;
    }
    private bool Busy(SessionRuntime runtime)=>runtime.Data.Status is "Running" or "Queued" or "Compacting" or "Waiting for approval";
    private void RequireIdle(SessionRuntime runtime){if(Busy(runtime))throw new InvalidOperationException("Finish or cancel this chat's task first.");}
    private void ChooseChatWorkspace(string path)
    {
        RequireIdle(CurrentChat);
        if(CurrentChat.Data.Entries.Count>0)NewChat();
        CurrentChat.Data.Workspace=path;CurrentChat.Chat.Clear();SaveChat(CurrentChat,true);SendChats();
    }
    private void ChooseChatModel(string model,SessionRuntime? target=null)
    {
        var runtime=target??CurrentChat;RequireIdle(runtime);runtime.Data.Model=model;
        var memory=runtime.Chat.ExportMemory() with{SessionKey="",Usage=null};runtime.Chat.ImportMemory(memory);SaveChat(runtime,true);SendChats();
    }
    private void CloseChats()
    {
        foreach(var runtime in chats.Values)
        {
            if(Busy(runtime)){runtime.Data.Status="Interrupted";runtime.Data.Entries.Add(new(){Text="The app closed during a task. It was not resumed; completed changes remain saved. Pending approvals are invalid."});}
            if(runtime.Data.Status=="Interrupted")try{chatStore.Save(runtime.Data);}catch(Exception ex){Log("chat-storage",ex);}runtime.Dispose();
        }
        foreach(var job in chatQueue)job.Cancellation.Cancel();
    }
    private async Task<bool> HandleAssistantAsync(JsonElement root)
    {
        var type=root.GetProperty("type").GetString();
        if(type=="editGlobalRules"){OpenPath(AgentRules.GlobalPath);return true;}
        if(type=="speechCancel"){speechCancellation?.Cancel();return true;}
        if(type=="chatNew"||type=="chatClear"){NewChat();return true;}
        if(type=="chatSelect") {selectedChat=MessageChat(root).Data.Id;SendChats();return true;}
        if(type=="loadRules") {var target=MessageChat(root);var rules=AgentRules.Load(new Workspace(target.Data.Workspace));Send(new{type="rulesLoaded",sessionId=target.Data.Id,value=rules.Select(r=>r.Path).ToArray()});return true;}
        if(type=="reviewDecision")
        {
            if(activeJob is null||pendingReview is null||root.GetProperty("id").GetString()!=pendingReviewId||MessageChat(root)!=activeJob.Runtime)throw new InvalidDataException("This review is no longer active for this chat.");
            pendingReview.TrySetResult(root.GetProperty("approved").GetBoolean());return true;
        }
        if(type=="transcribe")
        {
            if(speechBusy)throw new InvalidOperationException("Transcription is already running.");
            var target=MessageChat(root).Data.Id;speechBusy=true;speechCancellation=new();
            try{Send(new{type="speechStatus",sessionId=target,value="Transcribing locally…"});var text=await SpeechInput.TranscribeAsync(settings.Assistant,root.GetProperty("audio").GetString()??"",speechCancellation.Token);Send(new{type="speechResult",sessionId=target,value=text});}
            catch(OperationCanceledException){Send(new{type="speechError",sessionId=target,value="Transcription canceled."});}
            catch(Exception ex){Send(new{type="speechError",sessionId=target,value=FriendlyError(ex)});}
            finally{speechBusy=false;speechCancellation.Dispose();speechCancellation=null;}return true;
        }
        if(type is not ("assistantPermissions" or "chatCancel" or "chatRename" or "chatDelete" or "chatContextOptions" or "chatMeasure" or "chatRestore" or "chatCompact" or "chatSend" or "chatModel"))return false;
        var runtime=MessageChat(root);var data=runtime.Data;
        if(type=="chatCancel")
        {
            if(activeJob?.Runtime==runtime){activeJob.Cancellation.Cancel();pendingReview?.TrySetResult(false);}
            foreach(var queued in chatQueue.Where(j=>j.Runtime==runtime))queued.Cancellation.Cancel();return true;
        }
        if(type=="assistantPermissions")
        {
            RequireIdle(runtime);var access=root.GetProperty("access").GetString();if(access is not ("ReadOnly" or "ReadWrite"))throw new InvalidDataException("Invalid access mode.");
            data.Access=Enum.Parse<WorkspaceAccess>(access);data.AllowScripts=root.GetProperty("allowScripts").GetBoolean();
            runtime.Chat.ImportMemory(runtime.Chat.ExportMemory() with{SessionKey="",Usage=null});SaveChat(runtime,true);SendChats();return true;
        }
        if(type=="chatRename") {var title=root.GetProperty("title").GetString()?.Trim();if(string.IsNullOrEmpty(title)||title.Length>100)throw new InvalidDataException("Use a chat title of 1–100 characters.");data.Title=title;SaveChat(runtime);SendChats();return true;}
        if(type=="chatDelete") {RequireIdle(runtime);data.Deleted=true;SaveChat(runtime);selectedChat=chats.Values.FirstOrDefault(s=>!s.Data.Deleted)?.Data.Id??"";if(selectedChat.Length==0)NewChat();SendChats();return true;}
        if(type=="chatModel")
        {
            RequireIdle(runtime);var model=root.GetProperty("model").GetString()??"";
            if(!settings.Coding.ListModels().Contains(model,StringComparer.OrdinalIgnoreCase))throw new InvalidDataException("The model is not installed.");ChooseChatModel(model,runtime);return true;
        }
        if(type=="chatContextOptions")
        {
            RequireIdle(runtime);var mode=root.GetProperty("mode").GetString();if(mode is not ("Automatic" or "Manual" or "Off"))throw new InvalidDataException("Invalid compaction mode.");
            var reserve=root.GetProperty("reserve").GetInt32();if(reserve is <1 or >1048576)throw new InvalidDataException("Choose a custom output budget from 1 to 1,048,576 tokens.");
            data.Compaction=Enum.Parse<CompactionMode>(mode);data.ReplyReserve=reserve;data.AutomaticReplyBudget=root.GetProperty("automaticReplyBudget").GetBoolean();if(data.Memory.Usage is {} usage){data.Memory=data.Memory with{Usage=usage with{Reserve=usage.Used is int used?ResponseBudget.Resolve(usage.Capacity,used,reserve,data.AutomaticReplyBudget):0}};runtime.Chat.ImportMemory(data.Memory);}SaveChat(runtime);SendChats();return true;
        }
        if(type=="chatMeasure")
        {
            RequireIdle(runtime);if(chatBusy)throw new InvalidOperationException("Wait for the current request before refreshing context.");
            var previous=data.Memory;using var probe=new AssistantChat();probe.ImportMemory(previous);
            var usage=await probe.RefreshContextAsync(ChatSettings(data),data.ReplyReserve,CancellationToken.None,data.AutomaticReplyBudget);
            if(data.Memory!=previous||Busy(runtime))return true;
            data.Memory=previous with{Usage=usage};runtime.Chat.ImportMemory(data.Memory);SaveChat(runtime);SendChats();return true;
        }
        if(type=="chatRestore")
        {
            RequireIdle(runtime);var checkpoint=data.Checkpoints.SingleOrDefault(c=>c.Id==root.GetProperty("id").GetString())??throw new InvalidDataException("Checkpoint not found.");
            var previous=runtime.Chat.ExportMemory();data.Checkpoints.Add(new(){Memory=previous,Before=previous.Usage?.Used??0,After=checkpoint.Before});
            runtime.Chat.ImportMemory(checkpoint.Memory with{SessionKey="",Usage=null});
            data.Entries.Add(new(){Text="Restored the context checkpoint from "+checkpoint.Created.ToLocalTime().ToString("g")+". Transcript and completed file changes are retained; actions are not replayed."});SaveChat(runtime,true);SendChats();return true;
        }
        RequireIdle(runtime);
        if(!(await services["coding"].Control.ReadStatusAsync()).Healthy)throw new InvalidOperationException("Start the coding server first.");
        var prompt=type=="chatSend"?root.GetProperty("prompt").GetString()??"":"";
        var images=type=="chatSend"?root.GetProperty("images").EnumerateArray().Select(x=>x.GetString()??"").ToArray():[];
        if(type=="chatSend"&&(prompt.Length>24000||string.IsNullOrWhiteSpace(prompt)&&images.Length==0||images.Length>4||images.Any(i=>!AssistantChat.ValidImage(i))))throw new InvalidDataException("Enter a message of up to 24,000 characters and up to four valid images.");
        if(images.Length>0)
        {
            using var http=new System.Net.Http.HttpClient();using var props=JsonDocument.Parse(await http.GetStringAsync(settings.Coding.Url+"/props"));
            if(!props.RootElement.TryGetProperty("modalities",out var modes)||!modes.TryGetProperty("vision",out var vision)||!vision.GetBoolean())throw new InvalidOperationException("The running server has no image support. Restart with the matching vision component.");
        }
        RequireIdle(runtime);
        if(type=="chatSend")
        {
            data.Entries.Add(new(){Role="user",Text=prompt,Images=images});
            if(data.Title=="New chat")data.Title=(string.IsNullOrWhiteSpace(prompt)?"Image conversation":prompt.Replace('\n',' '))[..Math.Min(string.IsNullOrWhiteSpace(prompt)?18:prompt.Length,80)];
        }
        data.Status="Queued";SaveChat(runtime);chatQueue.Enqueue(new(runtime,ChatSettings(data),prompt,images,type=="chatCompact",new()));chatBusy=true;SendChats();SendSnapshot();
        if(activeJob is null)_=ProcessChatsAsync();return true;
    }
    private async Task ProcessChatsAsync()
    {
        while(chatQueue.TryDequeue(out var job))
        {
            activeJob=job;chatCancellation=job.Cancellation;var runtime=job.Runtime;var data=runtime.Data;ChatEntry? answer=null;var answerText=new StringBuilder();bool incomplete=false;DateTimeOffset flushed=DateTimeOffset.MinValue;
            void Checkpoint(ContextCheckpoint point){data.Checkpoints.Add(point);SaveChat(runtime);}
            void Report(string type,object? value)
            {
                if(type=="chatToken") {answer??=new(){Role="assistant"};if(!data.Entries.Contains(answer))data.Entries.Add(answer);answerText.Append(Convert.ToString(value));if(DateTimeOffset.UtcNow-flushed>TimeSpan.FromSeconds(1)){answer.Text=answerText.ToString();SaveChat(runtime);flushed=DateTimeOffset.UtcNow;}}
                if(type is "chatIncomplete" or "contextWarning"){if(type=="chatIncomplete")incomplete=true;data.Entries.Add(new(){Text=Convert.ToString(value)??""});}
                if(type=="toolResult"||type=="contextCompacted") {data.Entries.Add(new(){Text=type=="toolResult"?Convert.ToString(value)??"":"Context compacted: "+JsonSerializer.Serialize(value,SettingsStore.Json)});if(type=="contextCompacted")SaveChat(runtime,true);else SaveChat(runtime);}
                if(type=="contextUsage")data.Memory=data.Memory with{Usage=runtime.Chat.LastUsage};
                Send(new{type,sessionId=data.Id,value});
            }
            try
            {
                job.Cancellation.Token.ThrowIfCancellationRequested();data.Status=job.Compact?"Compacting":"Running";SaveChat(runtime);SendChats();
                if(job.Compact)await runtime.Chat.CompactAsync(job.Settings,Report,job.Cancellation.Token,Checkpoint,data.ReplyReserve,data.AutomaticReplyBudget,compactRecent:!runtime.Chat.CanKeepRecentTurns);
                else await runtime.Chat.RunAsync(job.Settings,job.Prompt,job.Images,(review,token)=>ReviewAsync(runtime,review,token),Report,job.Cancellation.Token,data.Compaction,data.ReplyReserve,Checkpoint,data.AutomaticReplyBudget);
                data.Status=incomplete?"Incomplete":"Ready";
            }
            catch(Exception ex)
            {
                var error=ex is OperationCanceledException?"Task canceled. Completed file changes remain saved.":FriendlyError(ex);
                data.Status=ex is OperationCanceledException?"Canceled":"Failed";data.Entries.Add(new(){Text=error});Send(new{type="chatError",sessionId=data.Id,value=error});Log("assistant",new InvalidOperationException(ex.GetType().Name+": "+error));
            }
            finally
            {
                pendingReview=null;pendingReviewId=null;if(answer is not null)answer.Text=answerText.ToString();
                try{SaveChat(runtime,true);}catch(Exception ex){notice="Chat could not be saved: "+FriendlyError(ex);Send(new{type="chatError",sessionId=data.Id,value=notice});}
                job.Cancellation.Dispose();activeJob=null;chatCancellation=null;SendChats();
            }
        }
        chatBusy=false;SendChats(false);SendSnapshot();
    }
    private async Task<bool> ReviewAsync(SessionRuntime runtime,ToolReview review,CancellationToken token)
    {
        pendingReview=new(TaskCreationOptions.RunContinuationsAsynchronously);pendingReviewId=review.Id;runtime.Data.Status="Waiting for approval";SaveChat(runtime);
        selectedChat=runtime.Data.Id;SendChats();Send(new{type="toolReview",sessionId=runtime.Data.Id,value=review});
        try{return await pendingReview.Task.WaitAsync(token);}
        finally{pendingReview=null;pendingReviewId=null;runtime.Data.Status="Running";Send(new{type="reviewClosed",sessionId=runtime.Data.Id});}
    }
}
