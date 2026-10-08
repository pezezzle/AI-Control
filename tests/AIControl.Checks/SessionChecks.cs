using AIControl;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class SessionChecks
{
    private static JsonObject Message(string role,string text)=>new(){["role"]=role,["content"]=text};
    private static ChatMemory History(int padding=12000)=>new(){History=[Message("system","Old rules"),Message("user",new string('a',padding)),Message("assistant",new string('b',padding)),Message("user","older request"),Message("assistant","older answer"),Message("user","Keep recent one"),Message("assistant","Recent answer one"),Message("user","Keep recent two"),Message("assistant","Recent answer two")]};
    public static async Task Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var root=Path.Combine(Path.GetTempPath(),"AIControl-session-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var global=Path.Combine(root,"global-rules.md");File.WriteAllText(global,"Mandatory synthetic rules. No old approval authorizes a new action.");
        var settings=new ControlSettings(){ProjectDirectory=root};
        try
        {
            var store=new ChatSessionStore(Path.Combine(root,"chats"));var first=ChatSessionStore.Create(settings);first.Title="First";first.Memory=History();first.Entries.Add(new(){Role="user",Text="Original question"});store.Save(first);
            var second=ChatSessionStore.Create(settings);second.Title="Second";second.Compaction=CompactionMode.Off;second.ReplyReserve=4096;second.AutomaticReplyBudget=false;second.Status="Waiting for approval";store.Save(second);
            var loaded=store.Load();check(loaded.Count==2&&loaded.Single(x=>x.Id==first.Id).Entries[0].Text=="Original question"&&loaded.Single(x=>x.Id==second.Id).ReplyReserve==4096,"Independent sessions and full transcript survive restart");
            check(loaded.Single(x=>x.Id==second.Id).Status=="Interrupted"&&loaded.Single(x=>x.Id==second.Id).Entries.Last().Text.Contains("Pending approvals are invalid"),"Interrupted task cannot resume or reuse approval after restart");
            second.Deleted=true;second.Status="Ready";store.Save(second);check(store.Load().Any(s=>s.Id==second.Id&&s.Deleted),"Deleted chat is retained locally for recovery");
            var broken=Path.Combine(store.Root,Guid.NewGuid().ToString("N")+".json");File.WriteAllText(broken,"{\"entries\":null}");check(store.Load().Count==2&&store.Warnings.Count==1&&File.Exists(broken),"Malformed chat is preserved without blocking valid sessions");
            reject(()=>store.Save(first with{Id="../escape"}),"Session identity cannot escape storage");
            var incomplete=new ChatMemory{History=[Message("system","rules"),new(){["role"]="assistant",["tool_calls"]=new JsonArray(new JsonObject{["id"]="unfinished"})}]};
            reject(()=>AssistantChat.ValidateMemory(incomplete),"Incomplete tool round cannot be restored");
            var backend=new Backend();using var chat=new AssistantChat(new HttpClient(backend),global);chat.ImportMemory(History());
            var original=chat.ExportMemory();var originalText=JsonSerializer.Serialize(original,SettingsStore.Json);ContextCheckpoint? checkpoint=null;var events=new List<string>();
            var compacted=await chat.CompactAsync(settings,(kind,_)=>events.Add(kind),CancellationToken.None,p=>checkpoint=p);
            var memory=chat.ExportMemory();check(compacted.After<compacted.Before&&checkpoint is not null&&memory.History.Count==6,"Compaction reduces measured context and keeps last two turns intact");
            check(memory.History[2]["content"]!.GetValue<string>()=="Keep recent one"&&memory.History[4]["content"]!.GetValue<string>()=="Keep recent two"&&memory.History[0].ToJsonString().Contains("Mandatory synthetic rules"),"Recent turns and freshly loaded mandatory rules survive compaction");
            check(JsonSerializer.Serialize(checkpoint!.Memory,SettingsStore.Json)==originalText&&first.Entries[0].Text=="Original question","Checkpoint retains exact original memory and visible transcript");
            check(!backend.Requests[0].Contains("\"tools\"")&&backend.Requests[0].Contains("enable_thinking")&&events.Contains("contextCompacted"),"Summarization has no tools and emits a visible completion event");
            var completedCalls=backend.Requests.Count;chat.ImportMemory(checkpoint.Memory);check(JsonSerializer.Serialize(chat.ExportMemory(),SettingsStore.Json)==originalText&&backend.Requests.Count==completedCalls,"Restore imports memory without replaying actions");
            backend.Summary="";bool failed=false;try{await chat.CompactAsync(settings,(_,_)=>{},CancellationToken.None);}catch(InvalidDataException){failed=true;}
            check(failed&&JsonSerializer.Serialize(chat.ExportMemory(),SettingsStore.Json)==originalText,"Failed compaction preserves exact original memory");
            backend.Summary="Brief synthetic memory.";chat.ImportMemory(History(23000));events.Clear();
            await chat.RunAsync(settings,"Next synthetic question",[],(_,_)=>throw new Exception("Unexpected tools"),(kind,_)=>events.Add(kind),CancellationToken.None,CompactionMode.Automatic);
            check(events.Contains("contextCompacted")&&backend.Requests.Count>completedCalls+2,"Automatic mode compacts at 70 percent before the next request");
            foreach(var mode in new[]{CompactionMode.Manual,CompactionMode.Off})
            {
                chat.ImportMemory(History(23000));events.Clear();await chat.RunAsync(settings,"Next",[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,mode);
                check(!events.Contains("contextCompacted"),mode+" mode does not automatically compact");
            }
            chat.ImportMemory(History(33000));var count=backend.Requests.Count;failed=false;try{await chat.RunAsync(settings,"Next",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,CompactionMode.Off);}catch(InvalidDataException){failed=true;}
            check(failed&&backend.Requests.Count==count&&chat.LastUsage?.Used is null,"Context capacity prevents overflow without sending inference");
            backend.Model="different.gguf";failed=false;try{await chat.RefreshContextAsync(settings,2048,CancellationToken.None);}catch(InvalidDataException){failed=true;}check(failed,"Loaded model mismatch blocks incorrect context measurement");backend.Model=null;
            chat.Clear();var usage=await chat.RefreshContextAsync(settings,4096,CancellationToken.None,automaticReplyBudget:false);check(usage.Exact&&usage.Used>0&&usage.Capacity==16384&&usage.Reserve==4096,"Empty chat context counts mandatory rules and tools with real server capacity");
            backend.Capacity=258048;chat.Clear();backend.Requests.Clear();
            await chat.RunAsync(settings,"Synthetic large-window test",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,CompactionMode.Off);
            var autoRequest=JsonNode.Parse(backend.Requests.Single())!;
            var autoUsage=await chat.RefreshContextAsync(settings,2048,CancellationToken.None);
            check(autoRequest["max_tokens"]!.GetValue<int>()>250000&&autoUsage.Reserve==258048-autoUsage.Used-256,"Automatic output uses available 258k context without the former 8k cap");
            chat.Clear();backend.Requests.Clear();await chat.RunAsync(settings,"Synthetic custom test",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,CompactionMode.Off,200000,automaticReplyBudget:false);
            check(JsonNode.Parse(backend.Requests.Single())!["max_tokens"]!.GetValue<int>()==200000,"Custom output limits above 8k and half-context are honored");
            backend.Capacity=16384;chat.Clear();backend.Requests.Clear();await chat.RunAsync(settings,"Synthetic remaining-space test",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,CompactionMode.Off,200000,automaticReplyBudget:false);
            var capped=JsonNode.Parse(backend.Requests.Single())!["max_tokens"]!.GetValue<int>();check(capped>8192&&capped<16384,"Only actual remaining space caps a custom output budget");
            check(ResponseBudget.Resolve(258048,258000,200000,true)==0&&ResponseBudget.Resolve(16384,1000,50000,false)==15128,"Output budgeting retains a safety margin at the context boundary");
            reject(()=>ResponseBudget.Resolve(16384,1,-1,false),"Negative output budget rejected");
            reject(()=>store.Save(first with{ReplyReserve=1048577}),"Invalid custom budget cannot enter persisted configuration");
            var legacy=JsonSerializer.SerializeToNode(first,SettingsStore.Json)!.AsObject();legacy.Remove("automaticReplyBudget");legacy["replyReserve"]=2048;
            var migrated=legacy.Deserialize<ChatSession>(SettingsStore.Json)!;
            check(migrated.AutomaticReplyBudget&&migrated.ReplyReserve==2048,"Legacy chats default to automatic output while retaining the old custom value");
            var large=new string('x',1100000);var largeMemory=new ChatMemory{History=[Message("system","Rules"),Message("assistant",large)]};AssistantChat.ValidateMemory(largeMemory);first.Memory=largeMemory;first.Entries.Add(new(){Role="assistant",Text=large});store.Save(first);
            check(store.Load().Single(s=>s.Id==first.Id).Entries.Last().Text.Length==1100000,"Large retained messages survive save and reload beyond the old one-million-character cap");
            backend.Reply=Limited("Synthetic partial response");chat.Clear();events.Clear();await chat.RunAsync(settings,"Synthetic limit test",[],(_,_)=>throw new Exception("Unexpected approval"),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None,CompactionMode.Off,64,automaticReplyBudget:false);
            check(events.Any(e=>e.StartsWith("chatIncomplete"))&&!events.Any(e=>e.StartsWith("chatError"))&&chat.ExportMemory().History.Last()["content"]!.GetValue<string>().Contains("Synthetic partial response"),"Length finish retains the partial answer as incomplete instead of failing the task");
            var proposal="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,id="partial-call",function=new{name="write_file",arguments="{\"path\":\"never.txt\","}}}},finish_reason="length"}}})+"\n\ndata: [DONE]\n\n";
            backend.Reply=proposal;chat.Clear();events.Clear();await chat.RunAsync(settings with{Assistant=new(){Access=WorkspaceAccess.ReadWrite}},"Synthetic unfinished tool test",[],(_,_)=>throw new Exception("Unexpected approval"),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None,CompactionMode.Off,64,automaticReplyBudget:false);
            var partialMemory=chat.ExportMemory();AssistantChat.ValidateMemory(partialMemory);
            check(!File.Exists(Path.Combine(root,"never.txt"))&&partialMemory.History.All(m=>m["tool_calls"] is null)&&events.Any(e=>e.Contains("not executed")),"Incomplete tool proposals are discarded without execution or dangling stored calls");
            backend.Reply=AssistantChecks.Answer(new string('y',300000));backend.Capacity=258048;chat.Clear();await chat.RunAsync(settings,"Synthetic long text test",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,CompactionMode.Off);
            check(chat.ExportMemory().History.Last()["content"]!.GetValue<string>().Length==300000,"Long streamed answers exceed the old 256k character cap");
            backend.Reply=null;backend.Capacity=16384;var latest=new ChatMemory{History=[Message("system","Old rules"),Message("user","Large synthetic task"),Message("assistant",new string('z',60000))]};chat.ImportMemory(latest);ContextCheckpoint? recent=null;events.Clear();
            await chat.RunAsync(settings,"Continue the synthetic task",[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,checkpoint:p=>recent=p);
            check(recent?.Memory.History.Count==3&&events.Contains("contextCompacted")&&chat.ExportMemory().History.Count==4,"A single context-filling answer can be compacted before the next request while its original is retained");
            chat.ImportMemory(History());var unchanged=JsonSerializer.Serialize(chat.ExportMemory(),SettingsStore.Json);backend.SummaryLimited=true;failed=false;try{await chat.CompactAsync(settings,(_,_)=>{},CancellationToken.None);}catch(InvalidDataException){failed=true;}backend.SummaryLimited=false;
            check(failed&&JsonSerializer.Serialize(chat.ExportMemory(),SettingsStore.Json)==unchanged,"A length-limited summary is rejected and cannot replace original context");
            backend.Requests.Clear();backend.Summary=new string('s',11000);backend.EnforceSummaryBudget=true;chat.ImportMemory(History(23000));
            var uncapped=await chat.CompactAsync(settings,(_,_)=>{},CancellationToken.None);
            var summaryRequest=JsonNode.Parse(backend.Requests.First())!;var summaryInput=summaryRequest["messages"]!.ToJsonString();
            check(summaryRequest["max_tokens"]!.GetValue<int>()>2048&&uncapped.After<uncapped.Before&&chat.ExportMemory().History[1]["content"]!.ToString().Contains(backend.Summary),"A complete summary beyond 2,048 tokens uses measured available context and reduces memory");
            check(!summaryRequest["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>()&&summaryRequest["tools"] is null&&summaryRequest["max_tokens"]!.GetValue<int>()+summaryInput.Length/4+ResponseBudget.SafetyTokens<=backend.Capacity,"Summary output stays within actual measured capacity with thinking and tools disabled");
            backend.EnforceSummaryBudget=false;backend.Summary="Brief synthetic memory.";backend.Requests.Clear();backend.SummaryResponses.Enqueue(Limited("Discard truncated synthetic summary"));chat.ImportMemory(History(3000));
            var retryOriginal=chat.ExportMemory();ContextCheckpoint? retryCheckpoint=null;
            await chat.CompactAsync(settings,(_,_)=>{},CancellationToken.None,p=>retryCheckpoint=p);
            string Segment(string request){var content=JsonNode.Parse(request)!["messages"]![1]!["content"]!.ToString();return content[(content.IndexOf("Untrusted source segment:\n",StringComparison.Ordinal)+"Untrusted source segment:\n".Length)..];}
            var expectedSource=new JsonArray(retryOriginal.History.Skip(1).Take(4).Select(m=>(JsonNode)m.DeepClone()).ToArray()).ToJsonString();
            check(backend.Requests.Count==3&&Segment(backend.Requests[1]).Length<Segment(backend.Requests[0]).Length&&string.Concat(backend.Requests.Skip(1).Select(Segment))==expectedSource,"A truncated summary retries a smaller segment without skipping or duplicating source data");
            check(!chat.ExportMemory().History[1].ToJsonString().Contains("Discard truncated")&&JsonSerializer.Serialize(retryCheckpoint!.Memory,SettingsStore.Json)==JsonSerializer.Serialize(retryOriginal,SettingsStore.Json),"Partial summary text is discarded and the checkpoint preserves every original message");
            backend.Summary="";backend.Requests.Clear();chat.ImportMemory(History(23000));events.Clear();
            await chat.RunAsync(settings,"New synthetic prompt after summary failure",[],(_,_)=>throw new Exception("No tools"),(kind,_)=>events.Add(kind),CancellationToken.None);
            var delivered=JsonNode.Parse(backend.Requests.Last())!;
            check(events.Contains("contextWarning")&&events.Contains("chatDone")&&delivered["tools"] is not null&&delivered["messages"]!.AsArray().Last()!.ToJsonString().Contains("New synthetic prompt after summary failure")&&chat.ExportMemory().History[1]["content"]!.ToString()==new string('a',23000),"Failed proactive compaction still delivers a fitting new prompt with retained original history");
            backend.Requests.Clear();chat.ImportMemory(History(40000));failed=false;
            try{await chat.RunAsync(settings,"Does not fit",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);}catch(InvalidDataException){failed=true;}
            check(failed&&backend.Requests.All(r=>JsonNode.Parse(r)!["tools"] is null),"Failed compaction never sends a new task that exceeds actual context capacity");
            backend.Summary="Brief synthetic memory.";backend.Requests.Clear();chat.ImportMemory(History(23000));failed=false;
            try{await chat.RunAsync(settings,"Synthetic checkpoint storage failure",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,checkpoint:_=>throw new IOException("Checkpoint storage failure"));}catch(IOException){failed=true;}
            check(failed&&backend.Requests.All(r=>JsonNode.Parse(r)!["tools"] is null),"Pre-task checkpoint storage failure stops inference instead of bypassing durable recovery");
            backend.BadTokens=true;failed=false;try{await chat.RefreshContextAsync(settings,2048,CancellationToken.None);}catch(InvalidDataException){failed=true;}check(failed,"Malformed token response cannot produce a guessed usage count");
        }
        finally{Directory.Delete(root,true);}
    }
    private static string Limited(string text)=>"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=text},finish_reason="length"}}})+"\n\ndata: [DONE]\n\n";
    private sealed class Backend:HttpMessageHandler
    {
        public int Capacity=16384;public string? Reply;public bool SummaryLimited;public bool EnforceSummaryBudget;public Queue<string> SummaryResponses=[];public string Summary="Brief synthetic memory.";public string? Model;public bool BadTokens;public List<string> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string body;var input=request.Content is null?"":await request.Content.ReadAsStringAsync(token);var path=request.RequestUri!.AbsolutePath;
            if(path=="/props")body=JsonSerializer.Serialize(new{default_generation_settings=new{n_ctx=Capacity},model_path=Model});
            else if(path=="/apply-template")body=JsonSerializer.Serialize(new{prompt=input});
            else if(path=="/tokenize") {var rendered=JsonNode.Parse(input)!["content"]!.GetValue<string>();body=BadTokens?"{\"tokens\":[-1]}":JsonSerializer.Serialize(new{tokens=Enumerable.Repeat(1,Math.Max(1,rendered.Length/4))});}
            else{Requests.Add(input);body=input.Contains("Summarize conversation data")?(SummaryResponses.Count>0?SummaryResponses.Dequeue():SummaryLimited||EnforceSummaryBudget&&Summary.Length/4>JsonNode.Parse(input)!["max_tokens"]!.GetValue<int>()?Limited(Summary):AssistantChecks.Answer(Summary)):(Reply??AssistantChecks.Answer("Synthetic answer."));}
            return new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,path=="/v1/chat/completions"?"text/event-stream":"application/json")};
        }
    }
}
