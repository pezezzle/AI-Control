using AIControl;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

internal static class TemplateChecks
{
    private static JsonObject Message(string role,string text)=>new(){["role"]=role,["content"]=text};
    public static async Task RunLive(string evidencePath)
    {
        var root=Path.Combine(Path.GetTempPath(),"AIControl-live-template-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var rules=Path.Combine(root,"synthetic-rules.md");File.WriteAllText(rules,"Synthetic read-only template fixture. Do not execute tools.");
            using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(20)};
            var props=JsonNode.Parse(await http.GetStringAsync("http://127.0.0.1:8080/props"))!;
            var settings=new ControlSettings{ProjectDirectory=root};settings=settings with{Coding=settings.Coding with{Model=Path.GetFileName(props["model_path"]!.GetValue<string>())}};
            using var chat=new AssistantChat(http,rules);chat.ImportMemory(new(){History=[Message("system","Synthetic rules"),Message("user","Synthetic template task"),Message("assistant","Synthetic action receipts"),Message("assistant","Synthetic action receipts")]});
            var original=chat.ExportMemory().History.Select(m=>m.ToJsonString()).ToArray();
            var usage=await chat.RefreshContextAsync(settings,16384,CancellationToken.None);
            var unchanged=original.SequenceEqual(chat.ExportMemory().History.Select(m=>m.ToJsonString()));
            if(!usage.Exact||usage.Used is not >0||!unchanged)throw new InvalidDataException("Live synthetic template check failed.");
            var evidence=new{Model=settings.Coding.Model,Usage=usage,OriginalHistoryPreserved=unchanged,InferenceRequests=0};
            File.WriteAllText(evidencePath,JsonSerializer.Serialize(evidence,SettingsStore.Json));Console.WriteLine(JsonSerializer.Serialize(evidence,SettingsStore.Json));
        }
        finally{Directory.Delete(root,true);}
    }
    public static async Task Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"AIControl-template-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var rules=Path.Combine(root,"synthetic-rules.md");File.WriteAllText(rules,"Synthetic mandatory rules.");var settings=new ControlSettings{ProjectDirectory=root};
        try
        {
            var memory=new ChatMemory{History=[Message("system","Synthetic rules"),Message("user","Synthetic task"),Message("assistant","Synthetic receipts"),Message("assistant","Synthetic receipts")]};
            var backend=new Backend();using var chat=new AssistantChat(new HttpClient(backend),rules);chat.ImportMemory(memory);var before=chat.ExportMemory().History.Select(m=>m.ToJsonString()).ToArray();
            var usage=await chat.RefreshContextAsync(settings,16384,CancellationToken.None);
            check(usage.Exact&&backend.TemplateRequests.Single()["messages"]!.AsArray().Count==3&&chat.ExportMemory().History.Select(m=>m.ToJsonString()).SequenceEqual(before),"Legacy adjacent assistant messages render through the strict server template without changing original memory");
            await chat.RunAsync(settings,"Continue the synthetic request",[],(_,_)=>Task.FromResult(false),(_,_)=>{},CancellationToken.None);
            check(backend.ModelRequests.Single()["messages"]!.AsArray().Count==4&&backend.ModelRequests[0]["messages"]![2]!["content"]!.GetValue<string>()=="Synthetic receipts\n\nSynthetic receipts","Generation and measurement use the same normalized copy with all original assistant content retained");
            backend.Error=true;bool diagnosed=false;
            try{await chat.RefreshContextAsync(settings,16384,CancellationToken.None);}catch(InvalidDataException ex){diagnosed=ex.Message.Contains("/apply-template")&&ex.Message.Contains("HTTP 400")&&ex.Message.Contains("Cannot have 2")&&!ex.Message.Contains("Start the selected model");}
            check(diagnosed,"HTTP 400 reports the actual template endpoint and server cause without claiming an offline model");
            foreach(var malformed in new[]{"[]","{\"error\":[]}"})
            {
                backend.ErrorBody=malformed;bool safe=false;
                try{await chat.RefreshContextAsync(settings,16384,CancellationToken.None);}catch(InvalidDataException ex){safe=ex.Message.Contains("/apply-template")&&ex.Message.Contains("HTTP 400");}
                check(safe,"Malformed template error shape retains an actionable endpoint/status diagnostic: "+malformed);
            }
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class Backend:HttpMessageHandler
    {
        public bool Error;public string ErrorBody="{\"error\":{\"message\":\"Cannot have 2 or more assistant messages at the end of the list.\"}}";public List<JsonObject> TemplateRequests=[],ModelRequests=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var input=request.Content is null?null:JsonNode.Parse(await request.Content.ReadAsStringAsync(token))!.AsObject();var path=request.RequestUri!.AbsolutePath;string body;
            if(path=="/props")body="{\"default_generation_settings\":{\"n_ctx\":32768}}";
            else if(path=="/apply-template")
            {
                var messages=input!["messages"]!.AsArray();TemplateRequests.Add(input);
                if(Error||messages.Count>1&&messages[^1]!["role"]!.ToString()=="assistant"&&messages[^2]!["role"]!.ToString()=="assistant")return new(HttpStatusCode.BadRequest){Content=new StringContent(ErrorBody)};
                body=JsonSerializer.Serialize(new{prompt=input.ToJsonString()});
            }
            else if(path=="/tokenize")body="{\"tokens\":[1,2,3]}";
            else{ModelRequests.Add(input!);body=AssistantChecks.Answer("Synthetic completion");}
            return new(HttpStatusCode.OK){Content=new StringContent(body)};
        }
    }
}
