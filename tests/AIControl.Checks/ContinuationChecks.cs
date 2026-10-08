using AIControl;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class ContinuationChecks
{
    private static string Limited(string text="",bool proposal=false)
    {
        var delta=new JsonObject{["content"]=text,["reasoning_content"]=new string('r',60000)};
        if(proposal)delta["tool_calls"]=new JsonArray(new JsonObject{["index"]=0,["id"]="unfinished",["function"]=new JsonObject{["name"]="write_file",["arguments"]="{\"path\":\"never.txt\","}});
        return "data: "+new JsonObject{["choices"]=new JsonArray(new JsonObject{["delta"]=delta,["finish_reason"]="length"})}.ToJsonString()+"\n\ndata: [DONE]\n\n";
    }
    private static string Calls(params (string Name,object Args)[] calls)=>"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{tool_calls=calls.Select((call,index)=>new{index,id="call-"+index,type="function",function=new{name=call.Name,arguments=JsonSerializer.Serialize(call.Args)}})},finish_reason="tool_calls"}}})+"\n\ndata: [DONE]\n\n";
    public static async Task Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var root=Path.Combine(Path.GetTempPath(),"AIControl-continuation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var global=Path.Combine(root,"synthetic-global.md");File.WriteAllText(global,"Synthetic mandatory rules.");
        File.WriteAllText(Path.Combine(root,"large.txt"),new string('x',55000));
        var settings=new ControlSettings{ProjectDirectory=root,Assistant=new(){Access=WorkspaceAccess.ReadWrite}};
        try
        {
            var backend=new Backend([Limited("Synthetic partial answer",true),AssistantChecks.Answer("Synthetic completion")]);
            var store=new ChatSessionStore(Path.Combine(root,"sessions"));var session=ChatSessionStore.Create(settings);session.Status="Running";
            var events=new List<string>();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Finish the synthetic task",[],(_,_)=>throw new Exception("Incomplete proposal must not be reviewed"),(kind,_)=>events.Add(kind),CancellationToken.None,
                    checkpoint:point=>{session.Checkpoints.Add(point);session.Memory=chat.ExportMemory();store.Save(session);},
                    saveMemory:memory=>{session.Memory=memory;store.Save(session);});
                var restored=store.Load().Single();
                check(events.Contains("taskRecovery")&&events.Contains("chatDone")&&!events.Contains("chatIncomplete")&&backend.Normal.Count==2,"Length-limited automatic output resumes the same task after a durable checkpoint");
                check(restored.Status=="Interrupted"&&restored.Checkpoints.Count==1&&restored.Checkpoints[0].Memory.History.Last()["reasoning_content"]!.GetValue<string>().Length==60000,"Restart retains the full recovery checkpoint but never resumes an interrupted task");
                check(!backend.Normal[1]["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>()&&backend.Normal[0]["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>(),"Thinking is disabled visibly for recovery without capping normal output");
                check(backend.Normal[0]["max_tokens"]!.GetValue<int>()>8192&&chat.ExportMemory().History.Any(m=>m["content"] is JsonArray)&&chat.ExportMemory().History.All(m=>m["tool_calls"] is null)&&!File.Exists(Path.Combine(root,"never.txt")),"Recovery retains the exact current request and discards incomplete tools without execution");
                check(backend.Summaries.All(r=>r["tools"] is null&&!r.ToJsonString().Contains(new string('r',1000)))&&restored.Memory.History.Last()["content"]!.GetValue<string>()=="Synthetic completion","Recovery summaries omit raw thinking and tools; completed output survives persistence");
            }

            backend=new Backend([Calls(("write_file",new{path="first.txt",content="First synthetic file"})),Calls(("read_file",new{path="large.txt"})),Calls(("write_file",new{path="second.txt",content="Second synthetic file"})),AssistantChecks.Answer("Both saved")]);
            backend.OnSummary=()=>File.WriteAllText(global,"Fresh synthetic rules after compaction.");
            int approvals=0;var boundaries=new List<ChatMemory>();events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Save two synthetic files",[],(_,_)=>{approvals++;return Task.FromResult(true);},(kind,_)=>events.Add(kind),CancellationToken.None,
                    checkpoint:_=>{},saveMemory:memory=>boundaries.Add(memory));
                check(events.Contains("contextCompacted")&&approvals==2&&File.ReadAllText(Path.Combine(root,"first.txt"))=="First synthetic file"&&File.ReadAllText(Path.Combine(root,"second.txt"))=="Second synthetic file","Completed tool rounds compact within one task without repeating reviewed writes");
                check(backend.Normal[2].ToJsonString().Contains("Saved first.txt")&&backend.Normal[2].ToJsonString().Contains("Fresh synthetic rules after compaction")&&backend.Normal[2]["messages"]!.AsArray().Last()!["role"]!.ToString()=="user","Post-compaction requests include action receipts and fresh rules without an assistant prefill");
                check(boundaries.Count>=4&&boundaries.All(memory=>{AssistantChat.ValidateMemory(memory);return true;}),"Each completed tool round has persistable paired memory before the next inference");
            }

            backend=new Backend([Calls(("write_file",new{path="retained.txt",content="Durable action"})),Calls(("read_file",new{path="large.txt"})),"data: {bad}\n\n"]){Summary=Limited("Truncated summary")};
            ChatMemory? lastSaved=null;bool failed=false;events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                try{await chat.RunAsync(settings,"Synthetic failed summary",[],(_,_)=>Task.FromResult(true),(kind,_)=>events.Add(kind),CancellationToken.None,saveMemory:memory=>lastSaved=memory);}catch(JsonException){failed=true;}
                var after=chat.ExportMemory();AssistantChat.ValidateMemory(after);
                check(failed&&lastSaved is not null&&after.History.Select(m=>m.ToJsonString()).SequenceEqual(lastSaved.History.Select(m=>m.ToJsonString()))&&File.ReadAllText(Path.Combine(root,"retained.txt"))=="Durable action","Failed summary and malformed later stream retain exact completed rounds and saved file effects");
                check(events.Contains("contextWarning")&&!events.Contains("contextCompacted"),"A truncated in-task summary rolls back and cannot replace working memory");
            }

            backend=new Backend([Limited("Checkpoint failure")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic checkpoint failure",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None,checkpoint:_=>throw new IOException("Synthetic checkpoint failure"));
                check(backend.Normal.Count==1&&chat.ExportMemory().History.Last()["reasoning_content"]!.GetValue<string>().Length==60000&&events.Any(e=>e.StartsWith("chatIncomplete")&&e.Contains("recovery failed")),"Checkpoint storage failure prevents continuation and preserves the original partial response");
            }

            backend=new Backend([Calls(("write_file",new{path="storage.txt",content="Saved before failure"})),AssistantChecks.Answer("Must never run")]);failed=false;
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                try{await chat.RunAsync(settings,"Synthetic storage failure",[],(_,_)=>Task.FromResult(true),(_,_)=>{},CancellationToken.None,saveMemory:_=>throw new IOException("Synthetic storage failure"));}catch(IOException){failed=true;}
                AssistantChat.ValidateMemory(chat.ExportMemory());
                check(failed&&backend.Normal.Count==1&&File.Exists(Path.Combine(root,"storage.txt"))&&chat.ExportMemory().History.Last()["content"]!.GetValue<string>()=="Saved storage.txt","A failed durable boundary stops inference and retains the successful mutation result in memory");
            }

            using(var cancellation=new CancellationTokenSource())
            {
                backend=new Backend([Limited("Cancellation fixture")]){OnSummary=()=>cancellation.Cancel()};failed=false;
                using var chat=new AssistantChat(new HttpClient(backend),global);
                try{await chat.RunAsync(settings,"Synthetic cancel during summary",[],(_,_)=>Task.FromResult(false),(_,_)=>{},cancellation.Token,checkpoint:_=>throw new Exception("No checkpoint after cancellation"));}catch(OperationCanceledException){failed=true;}
                check(failed&&backend.Normal.Count==1&&chat.ExportMemory().History.Last()["reasoning_content"]!.GetValue<string>().Length==60000,"Cancellation during recovery preserves partial memory and cannot resume the task");
            }

            using(var cancellation=new CancellationTokenSource())
            {
                backend=new Backend([Calls(("write_file",new{path="cancelled-round.txt",content="Keep completed action"}),("write_file",new{path="not-run.txt",content="Never written"}))]);failed=false;
                using var chat=new AssistantChat(new HttpClient(backend),global);
                try{await chat.RunAsync(settings,"Synthetic cancellation between tools",[],(_,_)=>Task.FromResult(true),(kind,_)=>{if(kind=="toolResult")cancellation.Cancel();},cancellation.Token);}catch(OperationCanceledException){failed=true;}
                var memory=chat.ExportMemory();AssistantChat.ValidateMemory(memory);
                check(failed&&File.Exists(Path.Combine(root,"cancelled-round.txt"))&&!File.Exists(Path.Combine(root,"not-run.txt"))&&memory.History.Any(m=>m["content"]?.ToString()=="Saved cancelled-round.txt")&&memory.History.Last()["role"]!.GetValue<string>()=="tool","Cancellation inside a multi-tool round preserves completed writes and settles remaining proposals without execution");
            }

            backend=new Backend([Limited(),Limited(),Limited(),AssistantChecks.Answer("Must not run")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic no-progress recovery",[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,checkpoint:_=>{});
                check(backend.Normal.Count==2&&events.Count(e=>e=="taskRecovery")==1&&events.Contains("chatIncomplete"),"Repeated output exhaustion without any useful progress stops recovery instead of looping");
            }
            foreach(var mode in new[]{CompactionMode.Manual,CompactionMode.Off})
            {
                backend=new Backend([Limited()]);events.Clear();using var chat=new AssistantChat(new HttpClient(backend),global);
                await chat.RunAsync(settings,"Synthetic explicit compaction",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None,mode);
                check(backend.Summaries.Count==0&&backend.Normal.Count==1&&events.Any(e=>e.StartsWith("chatIncomplete")&&e.Contains(mode+" compaction mode")),mode+" never automatically recovers or recommends an already-enabled Automatic budget");
            }
            backend=new Backend([Limited()]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic custom output",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None,replyReserve:64,automaticReplyBudget:false);
                check(backend.Normal[0]["max_tokens"]!.GetValue<int>()==64&&backend.Summaries.Count==0&&events.Any(e=>e.Contains("Custom output limit was respected")),"Custom output exhaustion respects the selected cap and requires explicit continuation");
            }
            backend=new Backend([AssistantChecks.Answer("Thinking disabled fixture")]);
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic thinking toggle",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,thinkingEnabled:false);
                check(!backend.Normal.Single()["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>()&&backend.Normal[0]["max_tokens"]!.GetValue<int>()>8192,"The explicit Thinking toggle changes the template flag without adding an output cap");
            }
            backend=new Backend([Calls(("write_file",new{path="approved.txt",content="Approved content"})),Limited(),Calls(("write_file",new{path="new-proposal.txt",content="Must be reviewed separately"})),AssistantChecks.Answer("Denied new proposal")]);approvals=0;
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic approval isolation",[],(_,_)=>Task.FromResult(++approvals==1),(_,_)=>{},CancellationToken.None,checkpoint:_=>{});
                check(approvals==2&&File.ReadAllText(Path.Combine(root,"approved.txt"))=="Approved content"&&!File.Exists(Path.Combine(root,"new-proposal.txt")),"A new proposal after recovery requires a fresh review and cannot inherit a past approval");
            }
            var image="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aB9sAAAAASUVORK5CYII=";
            backend=new Backend([Limited(),AssistantChecks.Answer("Synthetic image continuation")]);
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic attached image task",[image],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,checkpoint:_=>{});
                check(chat.ExportMemory().History.Any(m=>m["content"] is JsonArray parts&&parts.Any(p=>p?["image_url"]?["url"]?.ToString()==image))&&backend.Summaries.All(r=>!r.ToJsonString().Contains(image)),"In-task compaction retains the current attachment verbatim while summary input uses an image marker");
            }
            File.WriteAllText(Path.Combine(root,"medium.txt"),new string('m',10000));
            backend=new Backend(Enumerable.Repeat(Calls(("read_file",new{path="medium.txt"})),48).Append(AssistantChecks.Answer("Long synthetic task complete"))){Capacity=8192};events.Clear();
            AssistantTaskProgress? longProgress=null;ChatMemory? longSaved=null;
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Read the synthetic fixture repeatedly",[],(_,_)=>Task.FromResult(false),(kind,value)=>{events.Add(kind);if(value is AssistantTaskProgress progress)longProgress=progress;},CancellationToken.None,checkpoint:_=>{},saveMemory:memory=>longSaved=memory);
                check(backend.Normal.Count==49&&longProgress is {Round:49,CompletedTools:48}&&events.Contains("chatDone")&&!events.Contains("chatIncomplete"),"A single task completes after 48 tool rounds without a fixed round limit");
                check(events.Count(e=>e=="contextCompacted")>3&&longSaved is not null&&longSaved.History.Last()["content"]!.ToString()=="Long synthetic task complete","Repeated successful in-task compactions continue beyond three while keeping durable completed memory");
                AssistantChat.ValidateMemory(chat.ExportMemory());
            }
            backend=new Backend([Limited(new string('a',18000)),Limited(new string('b',18000)),Limited(new string('c',18000)),Limited(new string('d',18000)),AssistantChecks.Answer("Progressing recovery complete")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic distinct partial progress",[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,checkpoint:_=>{});
                check(backend.Normal.Count==5&&events.Count(e=>e=="taskRecovery")==4&&events.Contains("chatDone")&&!events.Contains("chatIncomplete"),"Distinct partial answers can recover more than twice and finish in the same task");
            }
            backend=new Backend([Limited("Repeated identical partial"),Limited("Repeated identical partial"),AssistantChecks.Answer("Must not run")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                await chat.RunAsync(settings,"Synthetic identical partial progress",[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,checkpoint:_=>{});
                check(backend.Normal.Count==2&&events.Count(e=>e=="taskRecovery")==1&&events.Contains("chatIncomplete"),"Repeated identical partial answers stop automatic continuation for lack of progress");
            }
            using(var cancellation=new CancellationTokenSource())
            {
                backend=new Backend(Enumerable.Repeat(Calls(("read_file",new{path="first.txt"})),30));int finishedTools=0;bool cancelled=false;ChatMemory? durable=null;
                using var chat=new AssistantChat(new HttpClient(backend),global);
                try{await chat.RunAsync(settings,"Synthetic cancellation after many rounds",[],(_,_)=>Task.FromResult(false),(kind,_)=>{if(kind=="toolResult"&&++finishedTools==25)cancellation.Cancel();},cancellation.Token,saveMemory:memory=>durable=memory);}catch(OperationCanceledException){cancelled=true;}
                var memory=chat.ExportMemory();AssistantChat.ValidateMemory(memory);
                check(cancelled&&backend.Normal.Count==25&&memory.History.Count(m=>m["role"]?.ToString()=="tool")==25&&durable is not null&&memory.History.Select(m=>m.ToJsonString()).SequenceEqual(durable.History.Select(m=>m.ToJsonString())),"Cancellation after 25 rounds stops new inference and durably retains every completed tool result");
            }
            backend=new Backend([Calls(("read_file",new{path="medium.txt"}))]){Capacity=8192,Summary=AssistantChecks.Answer(new string('s',19000))};events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global))
            {
                // An oversized summary plus the exact retained request cannot reduce this context.
                await chat.RunAsync(settings,new string('g',23000),[],(_,_)=>Task.FromResult(false),(kind,_)=>events.Add(kind),CancellationToken.None,checkpoint:_=>{});
                check(events.Contains("chatIncomplete")&&!events.Contains("contextCompacted")&&chat.ExportMemory().History.Any(m=>m["role"]?.ToString()=="tool"),"A non-reducing in-task summary leaves complete tool results in the original working context");
            }
            session.ThinkingEnabled=false;session.Progress=new(3,"Finished",4,2,"write_file (path: synthetic.txt)",1,false);store.Save(session);
            var persisted=store.Load().Single();check(!persisted.ThinkingEnabled&&persisted.Progress==session.Progress,"Thinking preference and measured task progress survive session persistence");
            session.Status="Ready";session.Progress=new(49,"Finished",320,300,"Synthetic long task",7,false);session.Entries.Add(new(){Role="assistant",Text="Synthetic long task",Round=49});store.Save(session);
            persisted=store.Load().Single();check(persisted.Progress==session.Progress&&persisted.Entries.Last().Round==49,"Long-task round, tool, file and recovery counters persist without obsolete upper bounds");
            var legacy=JsonSerializer.SerializeToNode(session,SettingsStore.Json)!.AsObject();legacy.Remove("thinkingEnabled");check(legacy.Deserialize<ChatSession>(SettingsStore.Json)!.ThinkingEnabled,"Legacy chats retain Thinking by default");
            reject(()=>store.Save(session with{Progress=new(-1,"Bad",0,0,"",0,true)}),"Malformed persisted task progress is rejected");

            // Independent instances model the serialized host queue: recovered memory must not leak.
            backend=new Backend([Limited("Isolated first task"),AssistantChecks.Answer("First task complete")]);
            using var firstChat=new AssistantChat(new HttpClient(backend),global);
            await firstChat.RunAsync(settings,"First synthetic identity",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None,checkpoint:_=>{});
            var secondBackend=new Backend([AssistantChecks.Answer("Second task complete")]);using var secondChat=new AssistantChat(new HttpClient(secondBackend),global);
            await secondChat.RunAsync(settings,"Second synthetic identity",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);
            check(!secondBackend.Normal.Single().ToJsonString().Contains("First synthetic identity")&&secondChat.ExportMemory().History.Count==3,"Recovery stays isolated from the next queued chat's independent memory");
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class Backend(IEnumerable<string> responses):HttpMessageHandler
    {
        private readonly Queue<string> replies=new(responses);
        public string Summary=AssistantChecks.Answer("Synthetic summary: preserve the current goal; consult action receipts for completed mutations.");
        public Action? OnSummary;
        public int Capacity=16384;
        public List<JsonObject> Normal=[],Summaries=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var input=request.Content is null?"":await request.Content.ReadAsStringAsync(token);string body;var path=request.RequestUri!.AbsolutePath;
            if(path=="/props")body=JsonSerializer.Serialize(new{default_generation_settings=new{n_ctx=Capacity}});
            else if(path=="/apply-template")
            {
                var messages=JsonNode.Parse(input)!["messages"]!.AsArray();
                if(messages.Count>1&&messages[^1]!["role"]!.ToString()=="assistant"&&messages[^2]!["role"]!.ToString()=="assistant")throw new InvalidDataException("Strict template rejects adjacent terminal assistant messages.");
                body=JsonSerializer.Serialize(new{prompt=input});
            }
            else if(path=="/tokenize")body=JsonSerializer.Serialize(new{tokens=Enumerable.Repeat(1,Math.Max(1,JsonNode.Parse(input)!["content"]!.GetValue<string>().Length/4))});
            else
            {
                var data=JsonNode.Parse(input)!.AsObject();
                if(input.Contains("Summarize conversation data")){Summaries.Add(data);OnSummary?.Invoke();body=Summary;}
                else{Normal.Add(data);body=replies.Dequeue();}
            }
            token.ThrowIfCancellationRequested();return new(HttpStatusCode.OK){Content=new StringContent(body,System.Text.Encoding.UTF8,path=="/v1/chat/completions"?"text/event-stream":"application/json")};
        }
    }
}
