const {test,expect}=require('@playwright/test');
async function setup(page,override={}){
 await page.addInitScript(({override})=>{
  window.sent=[];const snapshot={type:'snapshot',notice:'',settings:{comfyUI:{kind:'ComfyUI',installDirectory:'C:\\ComfyUI',port:8188,model:'',contextSize:32768,gpuLayers:'auto'},coding:{kind:'Llama',installDirectory:'C:\\LocalAI',port:8080,model:'coder.gguf',contextSize:32768,gpuLayers:'auto'},projectDirectory:'C:\\Coding'},services:[{id:'comfy',name:'ComfyUI',url:'http://127.0.0.1:8188',state:'Running',running:0,pending:0,healthy:true,queueKnown:true,operating:false,starting:false,stopScheduled:false,error:'',models:[]},{id:'coding',name:'Coding server',url:'http://127.0.0.1:8080',state:'Offline',running:0,pending:0,healthy:false,queueKnown:false,operating:false,starting:false,stopScheduled:false,error:'',models:['coder.gguf','larger.gguf']}]};
  Object.assign(snapshot.services[0],override);window.snapshot=snapshot;window.listeners=[];window.listener=event=>window.listeners.forEach(fn=>fn(event));window.chrome.webview={postMessage:msg=>{window.sent.push(msg);if(msg.type==='ready')setTimeout(()=>window.listener({data:snapshot}),0);},addEventListener:(_,listener)=>window.listeners.push(listener)};
 },{override});await page.goto('http://127.0.0.1:17881');
}
test('overview shows real statuses and blocks shared GPU start',async({page})=>{await setup(page);await expect(page.locator('[data-service=comfy] .status')).toHaveText('Running');await expect(page.locator('[data-service=coding] button[data-action=start]')).toBeDisabled();await expect(page.locator('[data-service=coding] .detail')).toContainText('Stop ComfyUI first');});
test('idle stop requests guarded wait mode',async({page})=>{await setup(page);await page.locator('[data-service=comfy] button[data-action=stop]').click();await expect.poll(()=>page.evaluate(()=>window.sent.at(-1))).toEqual({type:'stop',serviceId:'comfy',mode:'wait'});});
test('busy stop offers waiting or explicit abort',async({page})=>{await setup(page,{state:'Busy',running:1,pending:2});await page.locator('[data-service=comfy] button[data-action=stop]').click();await expect(page.locator('#stop-dialog')).toBeVisible();await expect(page.locator('#stop-detail')).toContainText('2 queued');await page.getByRole('button',{name:'Stop now and cancel jobs'}).click();await expect.poll(()=>page.evaluate(()=>window.sent.at(-1))).toEqual({type:'stop',serviceId:'comfy',mode:'force'});});
test('unknown workload cannot silently stop',async({page})=>{await setup(page,{state:'UnknownQueue',queueKnown:false});await page.locator('[data-service=comfy] button[data-action=stop]').click();await expect(page.locator('#stop-dialog')).toBeVisible();await expect(page.locator('#stop-detail')).toContainText('unknown');await page.getByRole('button',{name:'Cancel',exact:true}).click();expect(await page.evaluate(()=>window.sent.some(m=>m.type==='stop'))).toBeFalsy();});
test('coding model selection and editor use typed bridge commands',async({page})=>{await setup(page);await page.locator('[data-page=coding]').click();await page.locator('[data-model]').filter({visible:true}).selectOption('larger.gguf');await expect.poll(()=>page.evaluate(()=>window.sent.at(-1))).toEqual({type:'selectModel',model:'larger.gguf'});await page.getByRole('button',{name:'Open in VS Code'}).click();await expect.poll(()=>page.evaluate(()=>window.sent.at(-1))).toEqual({type:'openEditor'});});
test('a missing configured model is shown honestly and cannot silently start another model',async({page})=>{
 await setup(page,{state:'Offline',healthy:false});await page.evaluate(()=>{window.snapshot.settings.coding.model='removed-coder.gguf';window.listener({data:window.snapshot});});await page.locator('[data-page=coding]').click();await expect(page.locator('#model-coding')).toHaveValue('removed-coder.gguf');await expect(page.locator('#model-coding option:checked')).toHaveText('Missing model: removed-coder.gguf');await expect(page.locator('[data-service=coding] button[data-action=start]').filter({visible:true})).toBeDisabled();await page.locator('#model-coding').selectOption('larger.gguf');expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'selectModel',model:'larger.gguf'});await page.evaluate(()=>{window.snapshot.settings.coding.model='larger.gguf';window.listener({data:window.snapshot});});await expect(page.locator('#model-coding')).toHaveValue('larger.gguf');await expect(page.locator('[data-service=coding] button[data-action=start]').filter({visible:true})).toBeEnabled();
});
test('server text is escaped and layout fits minimum window',async({page})=>{await setup(page,{error:'<img src=x onerror=alert(1)>'});await expect(page.locator('.error')).toContainText('<img');await expect(page.locator('.error img')).toHaveCount(0);await page.setViewportSize({width:920,height:610});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();});

test('custom context and speech settings survive typed save request',async({page})=>{
 await setup(page);await page.locator('[data-page=settings]').click();await page.locator('#context').fill('24576');await page.locator('#settings-form button[type=submit]').click();
 const request=await page.evaluate(()=>window.sent.at(-1));expect(request.type).toBe('saveSettings');expect(request.settings.coding.contextSize).toBe(24576);expect(request.settings.assistant.syncContinueContext).toBeTruthy();expect(request.settings.assistant.speechExecutable).toContain('whisper-cli.exe');
});
test('chat streams plain text and displays loaded rules without executing markup',async({page})=>{
 await setup(page);await page.evaluate(()=>{Object.assign(window.snapshot.services[1],{state:'Running',healthy:true});window.listener({data:window.snapshot});});await page.locator('[data-page=chat]').click();await page.locator('#chat-prompt').fill('Hello');await page.locator('#chat-send').click();
 await expect.poll(()=>page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatSend',prompt:'Hello',images:[]});
 await page.evaluate(()=>{window.listener({data:{type:'chatToken',value:'<img src=x onerror=alert(1)>'}});window.listener({data:{type:'rulesLoaded',value:['Global AGENTS.md','Project AGENTS.md']}});window.listener({data:{type:'chatDone',value:''}});});
 await expect(page.locator('#chat-history img')).toHaveCount(0);await expect(page.locator('#chat-history')).toContainText('<img');await expect(page.locator('#rules-list li')).toHaveCount(2);await expect(page.locator('#chat-send')).toBeEnabled();
});
test('exact file proposal requires explicit approval and can be denied',async({page})=>{
 await setup(page);await page.evaluate(()=>window.listener({data:{type:'toolReview',value:{id:'review-1',kind:'write_file',path:'test.ps1',before:'old',after:'new',warning:'Save?'}}}));
 await expect(page.locator('#review-before')).toHaveText('old');await expect(page.locator('#review-after')).toHaveText('new');await page.locator('#review-deny').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'reviewDecision',id:'review-1',approved:false});
});
test('transcription is inserted for review and never submitted automatically',async({page})=>{
 await setup(page);await page.locator('[data-page=chat]').click();await page.evaluate(()=>window.listener({data:{type:'speechResult',value:'Untersuche diesen Ordner'}}));await expect(page.locator('#chat-prompt')).toHaveValue('Untersuche diesen Ordner');expect(await page.evaluate(()=>window.sent.some(x=>x.type==='chatSend'))).toBeFalsy();
});

test('thinking shows measured progress and placeholder is replaced by the answer',async({page})=>{
 await setup(page);await page.evaluate(()=>{Object.assign(window.snapshot.services[1],{state:'Running',healthy:true});window.listener({data:window.snapshot});});await page.locator('[data-page=chat]').click();await page.locator('#chat-prompt').fill('Synthetic check');await page.locator('#chat-send').click();
 await expect(page.locator('#chat-history .assistant')).toContainText('Waiting for the model');
 await page.evaluate(()=>window.listener({data:{type:'chatProgress',value:{phase:'thinking',reasoningCharacters:1234,elapsedSeconds:17}}}));
 await expect(page.locator('#chat-status')).toContainText('reasoning characters');await expect(page.locator('#chat-status')).toContainText('17s');await expect(page.locator('#chat-history .assistant')).toContainText('no final answer yet');
 await page.evaluate(()=>{window.listener({data:{type:'chatToken',value:'Synthetic answer'}});window.listener({data:{type:'chatDone',value:''}});});await expect(page.locator('#chat-history .assistant pre')).toHaveText('Synthetic answer');await expect(page.locator('#chat-send')).toBeEnabled();
});

test('reasoning error ends the pending state and permits another request',async({page})=>{
 await setup(page);await page.evaluate(()=>{Object.assign(window.snapshot.services[1],{state:'Running',healthy:true});window.listener({data:window.snapshot});});await page.locator('[data-page=chat]').click();await page.locator('#chat-prompt').fill('Synthetic check');await page.locator('#chat-send').click();
 await page.evaluate(()=>window.listener({data:{type:'chatError',value:'The model exhausted its response budget while thinking.'}}));
 await expect(page.locator('#chat-history .assistant')).toContainText('No final answer was returned');await expect(page.locator('#chat-history .activity')).toContainText('while thinking');await expect(page.locator('#chat-send')).toBeEnabled();await expect(page.locator('#chat-cancel')).toBeHidden();
});
test('image attachment is previewed and can be removed',async({page})=>{
 await setup(page);await page.locator('[data-page=chat]').click();await page.locator('#image-files').setInputFiles({name:'sample.png',mimeType:'image/png',buffer:Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aB9sAAAAASUVORK5CYII=','base64')});await expect(page.locator('#chat-attachments img')).toHaveCount(1);await page.getByRole('button',{name:'Remove sample.png'}).click();await expect(page.locator('#chat-attachments img')).toHaveCount(0);
});

test('local recording produces bounded mono PCM and releases the microphone',async({page})=>{
 await setup(page);
 await page.evaluate(()=>{
  window.audioReleased=false;window.audioClosed=false;
  navigator.mediaDevices.getUserMedia=async()=>({getTracks:()=>[{stop:()=>{window.audioReleased=true;}}]});
  window.AudioContext=class{
   sampleRate=48000;destination={};async resume(){}async close(){window.audioClosed=true;}
   createMediaStreamSource(){return{connect(){},disconnect(){}};}
   createScriptProcessor(){const processor={disconnect(){},onaudioprocess:null,connect(){queueMicrotask(()=>processor.onaudioprocess({inputBuffer:{getChannelData:()=>new Float32Array(4800).fill(0.5)}}));}};return processor;}
  };
 });
 await page.locator('[data-page=chat]').click();await page.locator('#chat-mic').click();
 await expect(page.locator('#chat-mic')).toContainText('Stop recording');await page.locator('#chat-mic').click();
 await expect.poll(()=>page.evaluate(()=>window.sent.at(-1).type)).toBe('transcribe');
 const result=await page.evaluate(()=>({request:window.sent.at(-1),released:window.audioReleased,closed:window.audioClosed}));
 const audio=Buffer.from(result.request.audio,'base64');expect(audio.toString('ascii',0,4)).toBe('RIFF');expect(audio.readUInt32LE(24)).toBe(16000);expect(audio.readUInt16LE(22)).toBe(1);expect(audio.length).toBe(44+1600*2);expect(audio.readInt16LE(44)).toBe(16384);expect(result.released&&result.closed).toBeTruthy();
 expect(await page.evaluate(()=>window.sent.some(x=>x.type==='chatSend'))).toBeFalsy();
});
function session(id,title,status='Ready',text='Saved answer'){
 return {id,title,status,workspace:'C:\\Coding\\'+id,model:'coder.gguf',access:'ReadOnly',allowScripts:false,compaction:'Automatic',replyReserve:2048,automaticReplyBudget:false,entries:[{role:'user',text:'Saved question',images:[]},{role:'assistant',text,images:[]}],memory:{usage:{used:12000,capacity:16384,reserve:2048,exact:true,source:'Measured working context',percent:73.24}},checkpoints:[{id:'checkpoint-1',created:'2026-10-07T10:00:00Z',before:12000,after:4000}]};
}
async function sessions(page,current,activeId=null){await page.evaluate(({current,activeId})=>{Object.assign(window.snapshot.services[1],{state:'Running',healthy:true});window.listener({data:window.snapshot});window.listener({data:{type:'chatSessions',selectedId:current.id,activeId,sessions:[{id:'one',title:'First chat',status:activeId==='one'?'Running':'Ready'},{id:'two',title:'Second chat',status:'Ready'}],current}});},{current,activeId});}
test('saved sessions restore history, display measured context and keep drafts separate',async({page})=>{
 await setup(page);await sessions(page,session('one','First chat'));await page.locator('[data-page=chat]').click();await expect(page.locator('#chat-history')).toContainText('Saved answer');await expect(page.locator('#context-usage')).toContainText('12,000 / 16,384');await expect(page.locator('#context-source')).toContainText('2,048');await page.locator('#chat-prompt').fill('First draft');
 await page.locator('#chat-sessions button').filter({hasText:'Second chat'}).click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatSelect',sessionId:'two'});
 await sessions(page,session('two','Second chat'));await expect(page.locator('#chat-prompt')).toHaveValue('');await page.locator('#chat-prompt').fill('Second draft');await sessions(page,session('one','First chat'));await expect(page.locator('#chat-prompt')).toHaveValue('First draft');
});
test('switching from a running chat allows a second request to queue without mixing streamed output',async({page})=>{
 await setup(page);await sessions(page,session('one','First chat','Running'),'one');await page.locator('[data-page=chat]').click();await expect(page.locator('#chat-send')).toBeDisabled();await expect(page.locator('#chat-new')).toBeEnabled();
 await sessions(page,session('two','Second chat'),'one');await expect(page.locator('#chat-send')).toHaveText('Queue request');await page.evaluate(()=>window.listener({data:{type:'chatToken',sessionId:'one',value:'OTHER CHAT SECRET TEXT'}}));await expect(page.locator('#chat-history')).not.toContainText('OTHER CHAT SECRET TEXT');
 await page.locator('#chat-prompt').fill('Queued synthetic prompt');await page.locator('#chat-send').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatSend',sessionId:'two',prompt:'Queued synthetic prompt',images:[]});
});
test('context controls scope compaction and restore to the selected chat',async({page})=>{
 await setup(page);await sessions(page,session('two','Second chat'));await page.locator('[data-page=chat]').click();await page.locator('#context-settings > summary').click();await page.locator('#context-mode').selectOption('Manual');expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatContextOptions',sessionId:'two',mode:'Manual',reserve:2048,automaticReplyBudget:false});
 await page.locator('#context-compact').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatCompact',sessionId:'two'});await page.locator('.context-panel details').last().locator('summary').click();await page.locator('#context-restore').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatRestore',sessionId:'two',id:'checkpoint-1'});
 await page.evaluate(()=>window.listener({data:{type:'contextCompacted',sessionId:'two',value:{before:12000,after:4000}}}));await expect(page.locator('#chat-history')).toContainText('Original transcript and a restore checkpoint');
});
test('renaming and deletion require explicit UI actions and use a chat identity',async({page})=>{
 await setup(page);await sessions(page,session('two','Second chat'));await page.locator('[data-page=chat]').click();await page.locator('#chat-rename').click();await page.locator('#chat-name').fill('Renamed chat');await page.locator('#chat-name-form button[type=submit]').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatRename',sessionId:'two',title:'Renamed chat'});
 await page.locator('#chat-delete').click();expect(await page.evaluate(()=>window.sent.some(m=>m.type==='chatDelete'))).toBeFalsy();await page.locator('#chat-delete-confirm').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatDelete',sessionId:'two'});
});
test('review approval and dictation remain tied to their original chat',async({page})=>{
 await setup(page);await sessions(page,session('one','First chat'));await page.locator('[data-page=chat]').click();await page.evaluate(()=>window.listener({data:{type:'toolReview',sessionId:'one',value:{id:'review-one',kind:'write_file',path:'sample.txt',before:null,after:'Synthetic',warning:'Save?'}}}));await page.locator('#review-deny').click();expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'reviewDecision',sessionId:'one',id:'review-one',approved:false});
 await sessions(page,session('two','Second chat'));await page.evaluate(()=>window.listener({data:{type:'speechResult',sessionId:'one',value:'Original chat dictation'}}));await expect(page.locator('#chat-prompt')).toHaveValue('');await sessions(page,session('one','First chat'));await expect(page.locator('#chat-prompt')).toHaveValue('Original chat dictation');expect(await page.evaluate(()=>window.sent.some(m=>m.type==='chatSend'))).toBeFalsy();
});
test('session interface fits the minimum window without horizontal scrolling',async({page})=>{
 await setup(page);await sessions(page,session('two','Second chat'));await page.locator('[data-page=chat]').click();await page.setViewportSize({width:920,height:610});expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
});

test('legacy chats default to automatic output budget from free active context',async({page})=>{
 await setup(page);const current=session('one','First chat');delete current.automaticReplyBudget;current.memory.usage={used:8000,capacity:258048,reserve:2048,exact:true,source:'Measured working context',percent:3.1};await sessions(page,current);await page.locator('[data-page=chat]').click();await page.locator('#context-settings > summary').click();await expect(page.locator('#context-budget-mode')).toHaveValue('Automatic');await expect(page.locator('#context-reserve-label')).toBeHidden();await expect(page.locator('#context-source')).toContainText('249,792 tokens');
});
test('custom output budgets larger than the former cap reach the host unchanged',async({page})=>{
 await setup(page);const current=session('two','Second chat');current.automaticReplyBudget=true;await sessions(page,current);await page.locator('[data-page=chat]').click();await page.locator('#context-settings > summary').click();await page.locator('#context-budget-mode').selectOption('Custom');await expect(page.locator('#context-reserve-label')).toBeVisible();await page.locator('#context-reserve').fill('131072');await page.locator('#context-reserve').dispatchEvent('change');expect(await page.evaluate(()=>window.sent.at(-1))).toEqual({type:'chatContextOptions',sessionId:'two',mode:'Automatic',reserve:131072,automaticReplyBudget:false});
});
test('limited responses retain partial text and allow a new request',async({page})=>{
 await setup(page);await sessions(page,session('one','First chat'));await page.locator('[data-page=chat]').click();await page.locator('#chat-prompt').fill('Synthetic check');await page.locator('#chat-send').click();await page.evaluate(()=>{window.listener({data:{type:'chatToken',sessionId:'one',value:'Partial synthetic answer'}});window.listener({data:{type:'chatIncomplete',sessionId:'one',value:'Output budget reached; partial response retained.'}});});await expect(page.locator('#chat-history .assistant').last().locator('pre')).toHaveText('Partial synthetic answer');await expect(page.locator('#chat-status')).toContainText('Incomplete');await expect(page.locator('#chat-send')).toBeEnabled();await expect(page.locator('#chat-cancel')).toBeHidden();
});
