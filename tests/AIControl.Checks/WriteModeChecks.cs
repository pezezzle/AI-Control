using AIControl;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class WriteModeChecks
{
    public static async Task Run(Action<bool,string> check,Action<Action,string> reject)
    {
        var fixture=Path.Combine(Path.GetTempPath(),"AIControl-write-mode-"+Guid.NewGuid().ToString("N"));
        var root=Path.Combine(fixture,"workspace");Directory.CreateDirectory(root);var backups=Path.Combine(fixture,"backups");
        try
        {
            var workspace=new Workspace(root);
            check(workspace.Write("new.txt","New synthetic file",null,WorkspaceAccess.WorkspaceWrite,backups) is null&&File.ReadAllText(Path.Combine(root,"new.txt"))=="New synthetic file"&&!Directory.Exists(backups),"Workspace write creates a new file without approval or an unnecessary backup");
            var bytes=new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("Original synthetic content")).ToArray();File.WriteAllBytes(Path.Combine(root,"existing.txt"),bytes);
            var backup=workspace.Write("existing.txt","Replacement","Original synthetic content",WorkspaceAccess.WorkspaceWrite,backups)!;
            check(File.ReadAllBytes(backup).SequenceEqual(bytes)&&File.ReadAllText(Path.Combine(root,"existing.txt"))=="Replacement"&&!backup.StartsWith(root+Path.DirectorySeparatorChar),"Automatic overwrite preserves exact original bytes including BOM outside the workspace");
            var metadataPath=Path.Combine(Directory.GetParent(Path.GetDirectoryName(backup)!)!.FullName,"metadata.json");
            using(var metadata=JsonDocument.Parse(File.ReadAllText(metadataPath)))
                check(metadata.RootElement.GetProperty("relativePath").GetString()=="existing.txt"&&metadata.RootElement.GetProperty("sha256").GetString()==Convert.ToHexString(SHA256.HashData(bytes)),"Backup metadata records the exact workspace-relative file and original SHA-256");
            reject(()=>workspace.Write("existing.txt","Stale overwrite","Old",WorkspaceAccess.WorkspaceWrite,backups),"Automatic write rejects a changed source version");
            var blocked=Path.Combine(fixture,"not-a-directory");File.WriteAllText(blocked,"Synthetic obstruction");bool failed=false;
            try{workspace.Write("existing.txt","Must not save","Replacement",WorkspaceAccess.WorkspaceWrite,blocked);}catch(IOException){failed=true;}
            check(failed&&File.ReadAllText(Path.Combine(root,"existing.txt"))=="Replacement"&&!Directory.EnumerateFiles(root,"*.aicontrol-*.tmp").Any(),"Backup failure blocks the overwrite and removes only its temporary proposed file");
            reject(()=>workspace.Write("../outside.txt","Blocked",null,WorkspaceAccess.WorkspaceWrite,backups),"Automatic writes cannot escape the selected workspace");
            reject(()=>workspace.Write(".env","Blocked",null,WorkspaceAccess.WorkspaceWrite,backups),"Automatic writes retain credential-path protection");
            var settings=new ControlSettings{ProjectDirectory=root,Assistant=new(){Access=WorkspaceAccess.WorkspaceWrite}};
            var settingsPath=Path.Combine(fixture,"settings.json");SettingsStore.Save(settings,settingsPath);
            check(SettingsStore.Load(settingsPath).Assistant.Access==WorkspaceAccess.WorkspaceWrite,"Workspace write survives settings save/reload");
            var store=new ChatSessionStore(Path.Combine(fixture,"chats"));var session=new ChatSession{Workspace=root,Model=settings.Coding.Model,Access=WorkspaceAccess.WorkspaceWrite};store.Save(session);
            check(store.Load().Single().Access==WorkspaceAccess.WorkspaceWrite,"Workspace write is persisted per chat without changing other chats");
            var global=Path.Combine(fixture,"AGENTS.md");AgentRules.EnsureGlobal(global);var current=File.ReadAllText(global);
            check(current.Contains("Workspace write authorizes")&&current.Contains("scripts only when explicitly requested and approved"),"Generated rules acknowledge automatic folder writes while retaining script review");
            var legacy=current.Replace("Explain planned changes. Follow the selected access mode: Review writes requires approval; Workspace write authorizes file creation and changes within the selected folder without further approval.","Explain planned changes. Request approval before saving or overwriting files.");File.WriteAllText(global,legacy);AgentRules.EnsureGlobal(global);
            check(File.ReadAllText(global)==current&&File.ReadAllText(global+".before-workspace-write")==legacy,"Untouched old generated global rules migrate with an exact retained backup");
            File.WriteAllText(global,"Custom synthetic rules requiring special review.");AgentRules.EnsureGlobal(global);
            check(File.ReadAllText(global)=="Custom synthetic rules requiring special review.","User-customized global instructions are never migrated or overwritten");
            File.WriteAllText(global,"Synthetic mandatory rules. No real user task.");
            var backend=new Backend("write_file",new{path="pipeline.txt",content="Synthetic saved file"});var events=new List<string>();int reviews=0;
            using(var chat=new AssistantChat(new HttpClient(backend),global,backups))await chat.RunAsync(settings,"Save a synthetic file.",[],(_,_)=>{reviews++;return Task.FromResult(false);},(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(reviews==0&&File.ReadAllText(Path.Combine(root,"pipeline.txt"))=="Synthetic saved file"&&events.Any(x=>x.Contains("Saved pipeline.txt"))&&backend.Requests[0].Contains("preauthorizes"),"Complete automatic write proposals save and report success without invoking a review");
            backend=new Backend("write_file",new{path="pipeline.txt",content="Synthetic revised file"});events.Clear();
            using(var chat=new AssistantChat(new HttpClient(backend),global,backups))await chat.RunAsync(settings,"Replace a synthetic file.",[],(_,_)=>throw new Exception("Unexpected file review"),(kind,value)=>events.Add(kind+":"+value),CancellationToken.None);
            check(File.ReadAllText(Path.Combine(root,"pipeline.txt"))=="Synthetic revised file"&&events.Any(x=>x.Contains("Previous version:")),"Automatic overwrite activity identifies the retained previous version");
            File.WriteAllText(Path.Combine(root,"test.ps1"),"throw 'This synthetic script must never execute'");backend=new Backend("run_script",new{path="test.ps1"});reviews=0;
            using(var chat=new AssistantChat(new HttpClient(backend),global,backups))await chat.RunAsync(settings with{Assistant=settings.Assistant with{AllowScriptExecution=true}},"Propose a synthetic script.",[],(review,_)=>{reviews++;return Task.FromResult(false);},(_,_)=>{},CancellationToken.None);
            check(reviews==1&&backend.Requests[1].Contains("user denied"),"Workspace write still invokes separate script approval and respects denial without execution");
            backend=new Backend("write_file",new{path="review.txt",content="Denied synthetic file"});reviews=0;
            using(var chat=new AssistantChat(new HttpClient(backend),global,backups))await chat.RunAsync(settings with{Assistant=new(){Access=WorkspaceAccess.ReadWrite}},"Propose a reviewed file.",[],(_,_)=>{reviews++;return Task.FromResult(false);},(_,_)=>{},CancellationToken.None);
            check(reviews==1&&!File.Exists(Path.Combine(root,"review.txt")),"Review writes retains mandatory approval after adding the automatic mode");
        }
        finally{Directory.Delete(fixture,true);}
    }
    private sealed class Backend(string tool,object arguments):HttpMessageHandler
    {
        public List<string> Requests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            string content;
            switch(request.RequestUri!.AbsolutePath)
            {
                case "/props":content="{\"default_generation_settings\":{\"n_ctx\":32768}}";break;
                case "/apply-template":content="{\"prompt\":\"Synthetic prompt\"}";break;
                case "/tokenize":content="{\"tokens\":[1,2,3]}";break;
                default:
                    Requests.Add(await request.Content!.ReadAsStringAsync(token));
                    content=Requests.Count==1?"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{tool_calls=new[]{new{index=0,id="synthetic-write",type="function",function=new{name=tool,arguments=JsonSerializer.Serialize(arguments)}}}},finish_reason="tool_calls"}}})+"\n\ndata: [DONE]\n\n":AssistantChecks.Answer("Synthetic result.");break;
            }
            return new(HttpStatusCode.OK){Content=new StringContent(content)};
        }
    }
}
