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
