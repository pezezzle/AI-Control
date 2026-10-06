using AIControl;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Runtime.InteropServices;

internal static class AssistantChecks
{
    public static async Task Run(Action<bool,string> check, Action<Action,string> reject)
    {
        var temp=Path.Combine(Path.GetTempPath(),"AIControl-assistant-check-"+Guid.NewGuid().ToString("N"));
        var root=Path.Combine(temp,"workspace");Directory.CreateDirectory(root);Directory.CreateDirectory(Path.Combine(root,"docs"));
        var global=Path.Combine(temp,"AGENTS.md");File.WriteAllText(global,"Global test rules: no secrets.");File.WriteAllText(Path.Combine(root,"AGENTS.md"),"Project test rules.");File.WriteAllText(Path.Combine(root,"docs","AGENTS.md"),"Directory test rules.");
        try
        {
            var workspace=new Workspace(root);File.WriteAllText(Path.Combine(root,"sample.txt"),"Synthetic hello");
            check(workspace.Read("sample.txt")=="Synthetic hello","Workspace text read");
            check(workspace.NormalizeToolPath(Path.Combine(root,"sample.txt"))=="sample.txt","Absolute tool path inside workspace is safely normalized");
            check(workspace.NormalizeToolPath(root,true)=="."&&workspace.NormalizeToolPath("",true)==".","Root and empty listing paths refer to workspace root");
            foreach(var bad in new[]{Path.Combine(temp,"outside.txt"),Path.Combine(root,".env"),"\\outside.txt","C:outside.txt","test.txt:stream",""})reject(()=>workspace.NormalizeToolPath(bad),"Invalid tool path remains blocked: "+bad);
            foreach(var bad in new[]{"../outside.txt",Path.Combine(temp,"outside.txt"),".env",".git/config","folder/.ssh/id_rsa","test.txt:stream","NUL",".git./config"}) reject(()=>workspace.Resolve(bad),"Workspace blocks "+bad);
            if(OperatingSystem.IsWindows())
            {
                var outside=Path.Combine(temp,"outside.txt");File.WriteAllText(outside,"Synthetic outside content");
                var linked=Path.Combine(root,"linked.txt");
                if(!CreateHardLink(linked,outside,IntPtr.Zero))throw new IOException("Unable to create the synthetic hard-link fixture.");
                reject(()=>workspace.Read("linked.txt"),"Hard-linked file cannot expose content outside workspace");
                reject(()=>workspace.Write("linked.txt","modified","Synthetic outside content",WorkspaceAccess.ReadWrite),"Hard-linked file cannot be overwritten");
                check(File.ReadAllText(outside)=="Synthetic outside content","Blocked linked write leaves original file intact");
            }
            var rules=AgentRules.Load(workspace,"docs/new.txt",global);
            check(rules.Count==3&&rules[0].Path==global&&rules[2].Content=="Directory test rules.","Global, project, directory rules ordered");
            reject(()=>AgentRules.Load(workspace,global:Path.Combine(temp,"missing.md")),"Missing global rules blocks assistant");
            reject(()=>workspace.Write("new.txt","new",null,WorkspaceAccess.ReadOnly),"Read-only writes rejected");
            workspace.Write("new.txt","first",null,WorkspaceAccess.ReadWrite);
            reject(()=>workspace.Write("new.txt","stale",null,WorkspaceAccess.ReadWrite),"Unexpected existing file cannot be overwritten");
            File.WriteAllText(Path.Combine(root,"new.txt"),"changed");reject(()=>workspace.Write("new.txt","second","first",WorkspaceAccess.ReadWrite),"Changed file invalidates review");
            check(!AssistantChat.Tools(new()).ToJsonString().Contains("write_file"),"Read-only mode excludes writing tools");
            check(!AssistantChat.Tools(new()).ToJsonString().Contains("run_script"),"Script tools disabled by default");
            check(AssistantChat.Tools(new(){Access=WorkspaceAccess.ReadWrite,AllowScriptExecution=true}).ToJsonString().Contains("run_script"),"Script proposals require explicit setting");
            check(!AssistantChat.ValidImage("https://example.com/image.png"),"Remote image URLs rejected");
            check(!AssistantChat.ValidImage("data:image/png;base64,SGVsbG8="),"Fake image signature rejected");
            reject(()=>SpeechInput.DecodeWav(Convert.ToBase64String(new byte[44])),"Malformed audio rejected");
            var wav=new byte[76];using(var memory=new MemoryStream(wav))using(var writer=new BinaryWriter(memory)){writer.Write("RIFF"u8);writer.Write(68);writer.Write("WAVEfmt "u8);writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(16000);writer.Write(32000);writer.Write((short)2);writer.Write((short)16);writer.Write("data"u8);writer.Write(32);}
            check(SpeechInput.DecodeWav(Convert.ToBase64String(wav)).Length==76,"Browser PCM WAV accepted");
            var continuePath=Path.Combine(temp,"config.json");File.WriteAllText(continuePath,"""{"models":[{"model":"local-coder","apiBase":"http://127.0.0.1:8080/v1","contextLength":16384,"completionOptions":{"maxTokens":8192}},{"model":"cloud","contextLength":200000}],"apiKey":"TEST_ONLY"}""");
            ContinueSettings.SyncContext(24576,continuePath);var config=JsonNode.Parse(File.ReadAllText(continuePath))!;
            check(config["models"]![0]!["contextLength"]!.GetValue<int>()==24576&&config["models"]![1]!["contextLength"]!.GetValue<int>()==200000&&config["apiKey"]!.GetValue<string>()=="TEST_ONLY","Continue updates only local model and preserves unrelated configuration");
            var gguf=Path.Combine(temp,"fixture.gguf");using(var writer=new BinaryWriter(File.Create(gguf))){writer.Write(0x46554747u);writer.Write(3u);writer.Write(0ul);writer.Write(1ul);var key=System.Text.Encoding.UTF8.GetBytes("qwen35.context_length");writer.Write((ulong)key.Length);writer.Write(key);writer.Write(4u);writer.Write(262144u);}
            check(ModelMetadata.ContextLimit(gguf)==262144,"Context limit read from GGUF metadata");
            var options=new ControlSettings(){ProjectDirectory=root,Assistant=new(){Access=WorkspaceAccess.ReadWrite}};
            int approvals=0;var reviews=new List<ToolReview>();var events=new List<string>();
            var handler=new ChatBackend([
                Call("write_file",new{path="docs/generated.txt",content="Synthetic output"}),
                Call("write_file",new{path="docs/generated.txt",content="Synthetic output"}), Answer("Saved.")]);
            using(var chat=new AssistantChat(new HttpClient(handler),global))
                await chat.RunAsync(options,"Create a synthetic file.",[],(review,_)=>{approvals++;reviews.Add(review);return Task.FromResult(true);},(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(approvals==1&&File.ReadAllText(Path.Combine(root,"docs","generated.txt"))=="Synthetic output","Nested rules loaded before exact approved write");
            check(reviews[0].Before==null&&reviews[0].After=="Synthetic output","Review contains exact proposed content");
            check(handler.Requests[1].Contains("Directory test rules."),"Nested rules delivered to model before retry");
            handler=new ChatBackend([Call("write_file",new{path=Path.Combine(root,"absolute.txt"),content="Reviewed synthetic content"}),Answer("Saved.")]);
            ToolReview? absoluteReview=null;
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Create a synthetic file.",[],(review,_)=>{absoluteReview=review;return Task.FromResult(true);},(_,_)=>{},CancellationToken.None);
            check(absoluteReview?.Path=="absolute.txt"&&File.ReadAllText(Path.Combine(root,"absolute.txt"))=="Reviewed synthetic content","Normalized absolute write still requires exact relative-path review");
            handler=new ChatBackend([Call("write_file",new{path="",content="Must not save"}),Answer("Missing filename.")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Propose a synthetic file.",[],(_,_)=>throw new Exception("Unexpected approval"),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(events.Any(x=>x.Contains("Tool error in write_file")&&x.Contains("path:")&&x.Contains("file path is required")),"Invalid filename produces a specific tool and path explanation");
            var emptyRoot=Path.Combine(root,"empty");Directory.CreateDirectory(emptyRoot);handler=new ChatBackend([Call("list_files",new{path=""}),Answer("Empty.")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options with{ProjectDirectory=emptyRoot},"List the synthetic folder.",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(events.Any(x=>x.Contains("No accessible files found"))&&!handler.Requests[1].Contains("Listing truncated"),"Empty folder is explicitly reported without misleading truncation message");
            handler=new ChatBackend([Call("search_text",new{query="Synthetic output"}),Answer("Found.")]);
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Search synthetic text.",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);
            check(handler.Requests[1].Contains("Directory test rules.")&&handler.Requests[1].Contains("docs"),"Text search delivers applicable nested rules with matches");
            handler=new ChatBackend([Call("write_file",new{path="denied.txt",content="Denied"}),Answer("Denied.")]);
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Propose.",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);
            check(!File.Exists(Path.Combine(root,"denied.txt")),"Denied file proposal cannot write");
            handler=new ChatBackend([Call("write_file",new{path="readonly.txt",content="Denied"}),Answer("Blocked.")]);
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options with{Assistant=new()},"Read only.",[],(_,_)=>throw new Exception("Unexpected approval"),(_,_)=>{},CancellationToken.None);
            check(!File.Exists(Path.Combine(root,"readonly.txt"))&&handler.Requests[1].Contains("not permitted"),"Hallucinated write tool blocked in read-only mode");
            handler=new ChatBackend([Call("read_file",new{path="../outside.txt"}),Answer("Blocked.")]);
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Inspect.",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);
            check(handler.Requests[1].Contains("outside the selected workspace"),"Model traversal call blocked and reported");
            handler=new ChatBackend(["data: {bad}\n\n"]);bool malformed=false;
            using(var chat=new AssistantChat(new HttpClient(handler),global))try{await chat.RunAsync(options,"Hello",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);}catch(JsonException){malformed=true;}
            check(malformed,"Malformed server stream fails without tool execution");
            handler=new ChatBackend([Answer("Safe <img> text.")]);events.Clear();using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Hello",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(events.Any(x=>x=="chatToken:Safe <img> text.")&&events.Any(x=>x.StartsWith("chatDone")),"Streamed response reaches UI events");
            var thinking="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{reasoning_content="Synthetic thinking"}}}})+"\n\n";
            handler=new ChatBackend([thinking+Answer("Done.")]);events.Clear();
            using(var chat=new AssistantChat(new HttpClient(handler),global))await chat.RunAsync(options,"Hello",[],(_,_)=>Task.FromResult(false),(kind,value)=>events.Add(kind+":"+JsonSerializer.Serialize(value)),CancellationToken.None);
            check(events.Any(x=>x.StartsWith("chatProgress:")&&x.Contains("\"phase\":\"thinking\"")&&x.Contains("\"reasoningCharacters\":18"))&&!events.Any(x=>x.Contains("Synthetic thinking")),"Reasoning progress is visible without exposing raw thought text");
            handler=new ChatBackend([thinking+"data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\n"]);string budgetError="";
            using(var chat=new AssistantChat(new HttpClient(handler),global))try{await chat.RunAsync(options,"Hello",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);}catch(InvalidDataException ex){budgetError=ex.Message;}
            check(budgetError.Contains("while thinking")&&budgetError.Contains("no final answer"),"Reasoning-only budget exhaustion is explained accurately");
        }
        finally{Directory.Delete(temp,true);}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFile,string existingFile,IntPtr security);
    public static string Answer(string content)=>"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";
    private static string Call(string name,object args)=>"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,id="test-call",type="function",function=new{name,arguments=JsonSerializer.Serialize(args)}}}},finish_reason="tool_calls"}}})+"\n\ndata: [DONE]\n\n";
    private sealed class ChatBackend(IEnumerable<string> responses):HttpMessageHandler
    {
        readonly Queue<string> queue=new(responses);public List<string> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {Requests.Add(await request.Content!.ReadAsStringAsync(token));return new(HttpStatusCode.OK){Content=new StringContent(queue.Dequeue(),System.Text.Encoding.UTF8,"text/event-stream")};}
    }
}
