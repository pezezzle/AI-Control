let chatBusy=false, currentAnswer:HTMLElement|null=null, reviewId:string|null=null;
let attachments:{name:string;data:string}[]=[];
function showChat():void { element<HTMLButtonElement>('page-chat').hidden=false; document.querySelector<HTMLButtonElement>('[data-page="chat"]')?.click();send('loadRules'); }
function chatStatus(text:string):void { element('chat-status').textContent=text; }
function pendingAnswer(text:string):void {
 if(currentAnswer && (!currentAnswer.textContent || currentAnswer.dataset.pending==='true')){currentAnswer.textContent=text;currentAnswer.dataset.pending='true';}
}
function setChatBusy(busy:boolean):void {
 chatBusy=busy;
 for(const id of ['chat-new','chat-project','chat-access','chat-scripts','chat-send'])element<HTMLButtonElement>(id).disabled=busy;
 element('chat-cancel').hidden=!busy;
}
function addMessage(role:string,text:string):HTMLElement {
 const history=element('chat-history');history.querySelector('.chat-empty')?.remove();
 const article=document.createElement('article');article.className=`chat-message ${role}`;
 const label=document.createElement('strong');label.textContent=role==='user'?'You':role==='assistant'?'Assistant':'Activity';article.append(label);
 const body=document.createElement('pre');body.textContent=text;article.append(body);history.append(article);return body;
}
function renderAttachments():void {
 const holder=element('chat-attachments');holder.replaceChildren();
 attachments.forEach((image,index)=>{const item=document.createElement('div');const img=document.createElement('img');img.src=image.data;img.alt=image.name;const button=document.createElement('button');button.type='button';button.className='text-button';button.textContent=`Remove ${image.name}`;button.onclick=()=>{attachments.splice(index,1);renderAttachments();};item.append(img,button);holder.append(item);});
}
async function attachImages(files:FileList|File[]):Promise<void> {
 for(const file of Array.from(files)){
  if(attachments.length>=4){chatStatus('Attach up to four images.');break;}
  if(!['image/png','image/jpeg','image/webp'].includes(file.type)||file.size>5*1024*1024){chatStatus('Use PNG, JPEG or WebP images of up to 5 MB.');continue;}
  const data=await new Promise<string>((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result));reader.onerror=()=>reject(new Error('Image could not be loaded.'));reader.readAsDataURL(file);});
  attachments.push({name:file.name,data});
 }
 renderAttachments();
}
element('chat-images').onclick=()=>element<HTMLInputElement>('image-files').click();
element<HTMLInputElement>('image-files').onchange=async()=>{try{await attachImages(element<HTMLInputElement>('image-files').files??[]);}catch(error){chatStatus(String(error));}element<HTMLInputElement>('image-files').value='';};
element('chat-prompt').addEventListener('paste',event=>{const clip=(event as ClipboardEvent).clipboardData;const files=Array.from(clip?.items??[]).filter(i=>i.kind==='file').map(i=>i.getAsFile()).filter((f):f is File=>f!==null);if(files.length){event.preventDefault();void attachImages(files).catch(error=>chatStatus(String(error)));}});
element('chat-project').onclick=()=>send('chooseProject');
element('global-rules-edit').onclick=()=>send('editGlobalRules');
document.querySelector('[data-page="chat"]')?.addEventListener('click',()=>send('loadRules'));
function permissions():void {send('assistantPermissions',{access:element<HTMLSelectElement>('chat-access').value,allowScripts:element<HTMLInputElement>('chat-scripts').checked});}
element('chat-access').onchange=permissions;element('chat-scripts').onchange=permissions;
element('chat-new').onclick=()=>send('chatClear');element('chat-cancel').onclick=()=>send('chatCancel');
element('chat-form').onsubmit=event=>{
 event.preventDefault();if(chatBusy)return;const input=element<HTMLTextAreaElement>('chat-prompt');const prompt=input.value.trim();if(!prompt&&!attachments.length)return;
 if(!service('coding')?.healthy){chatStatus('Start the coding server first.');return;}
 addMessage('user',prompt+(attachments.length?`\n[${attachments.length} image attachments]`:''));currentAnswer=addMessage('assistant','');pendingAnswer('Waiting for the model response…');setChatBusy(true);chatStatus('Preparing request…');
 send('chatSend',{prompt,images:attachments.map(i=>i.data)});input.value='';attachments=[];renderAttachments();
};
element('chat-prompt').addEventListener('keydown',event=>{const key=event as KeyboardEvent;if(key.key==='Enter'&&(key.ctrlKey||key.metaKey)){key.preventDefault();element<HTMLFormElement>('chat-form').requestSubmit();}});
function reviewDecision(approved:boolean):void {if(reviewId)send('reviewDecision',{id:reviewId,approved});element<HTMLDialogElement>('review-dialog').close();reviewId=null;}
element('review-approve').onclick=()=>reviewDecision(true);element('review-deny').onclick=()=>reviewDecision(false);
element('review-dialog').addEventListener('cancel',event=>{event.preventDefault();reviewDecision(false);});
bridge?.addEventListener('message',event=>{
 const {type,value}=event.data;
 if(type==='snapshot'){
  const snapshot=event.data as Snapshot;const coding=snapshot.settings.coding;
  element('chat-path').textContent=snapshot.settings.projectDirectory;
  element('chat-model').textContent=`${coding.model} · ${coding.contextSize.toLocaleString()} context tokens`;
  element('global-rules-path').textContent=`Global rules: ${snapshot.globalRulesPath??'Local AI Control AGENTS.md'}`;
  element<HTMLSelectElement>('chat-access').value=snapshot.settings.assistant?.access??'ReadOnly';
  element<HTMLInputElement>('chat-scripts').checked=snapshot.settings.assistant?.allowScriptExecution??false;
 }
 if(type==='openChat')showChat();
 if(type==='chatStatus'){chatStatus(String(value));if(String(value).startsWith('Using '))pendingAnswer('The assistant is using workspace tools. See activity below.');}
 if(type==='chatProgress'){
  const seconds=Math.max(0,Number(value.elapsedSeconds)||0),characters=Math.max(0,Number(value.reasoningCharacters)||0);
  if(value.phase==='thinking'){chatStatus(`Thinking · ${characters.toLocaleString()} reasoning characters · ${seconds}s for this response`);pendingAnswer('The model is thinking. Its progress appears below; no final answer yet.');}
  else if(value.phase==='answering')chatStatus(`Writing answer · ${seconds}s for this response`);
 }
 if(type==='chatToken'){if(!currentAnswer)currentAnswer=addMessage('assistant','');if(currentAnswer.dataset.pending==='true'){currentAnswer.textContent='';delete currentAnswer.dataset.pending;}currentAnswer.textContent+=String(value);}
 if(type==='chatDone'){pendingAnswer('Completed without a text response. See activity below.');setChatBusy(false);chatStatus('Finished · all processing stays local.');currentAnswer=null;}
 if(type==='chatError'){pendingAnswer('No final answer was returned. See the explanation below.');addMessage('activity',String(value));setChatBusy(false);chatStatus(String(value));currentAnswer=null;}
 if(type==='chatCleared'){element('chat-history').replaceChildren();currentAnswer=null;setChatBusy(false);chatStatus('New chat · instructions will be loaded again.');send('loadRules');}
 if(type==='rulesLoaded'){const list=element('rules-list');list.replaceChildren();for(const path of value as string[]){const item=document.createElement('li');item.textContent=path;list.append(item);}}
 if(type==='toolResult')addMessage('activity',String(value));
 if(type==='toolReview'){
  reviewId=value.id;chatStatus('Waiting for your approval…');element('review-title').textContent=`${value.kind==='run_script'?'Run script':'Save file'}: ${value.path}`;
  element('review-warning').textContent=value.warning;element('review-before').textContent=value.before??'(New file)';element('review-after').textContent=value.after;
  element<HTMLDialogElement>('review-dialog').showModal();
 }
 if(type==='reviewClosed'){reviewId=null;element<HTMLDialogElement>('review-dialog').close();}
 if(type==='bridgeError'){chatStatus(String(value));if(!state?.chatBusy)setChatBusy(false);}
 if(type==='speechStatus'){chatStatus(String(value));element('speech-cancel').hidden=false;element<HTMLButtonElement>('chat-mic').disabled=true;}
 if(type==='speechResult'){
  const input=element<HTMLTextAreaElement>('chat-prompt');input.value+=(input.value?'\n':'')+String(value);input.focus();chatStatus('Transcribed locally. Review the text, then choose Send.');finishSpeech();
 }
 if(type==='speechError'){chatStatus(String(value));finishSpeech();}
});
let recording: {stream:MediaStream;context:AudioContext;processor:ScriptProcessorNode;source:MediaStreamAudioSourceNode;chunks:Float32Array[];timer:number;rate:number}|null=null;
function finishSpeech():void {element('speech-cancel').hidden=true;element<HTMLButtonElement>('chat-mic').disabled=false;element('chat-mic').textContent='● Dictate';}
element('speech-cancel').onclick=()=>send('speechCancel');
element('chat-mic').onclick=async()=>{
 if(recording){await stopRecording();return;}
 let stream:MediaStream|null=null, context:AudioContext|null=null;
 try{
  stream=await navigator.mediaDevices.getUserMedia({audio:{channelCount:1},video:false});context=new AudioContext();await context.resume();
  const source=context.createMediaStreamSource(stream),processor=context.createScriptProcessor(4096,1,1),chunks:Float32Array[]=[];
  processor.onaudioprocess=event=>{chunks.push(new Float32Array(event.inputBuffer.getChannelData(0)));};source.connect(processor);processor.connect(context.destination);
  recording={stream,context,source,processor,chunks,timer:window.setTimeout(()=>void stopRecording(),120000),rate:context.sampleRate};
  element('chat-mic').textContent='■ Stop recording';chatStatus('Recording locally… stop to transcribe (maximum two minutes).');
 }catch(error){stream?.getTracks().forEach(t=>t.stop());await context?.close();chatStatus('Microphone unavailable or permission denied: '+String(error));finishSpeech();}
};
async function stopRecording():Promise<void>{
 const rec=recording;if(!rec)return;recording=null;window.clearTimeout(rec.timer);rec.processor.disconnect();rec.source.disconnect();rec.stream.getTracks().forEach(t=>t.stop());await rec.context.close();
 const input=new Float32Array(rec.chunks.reduce((n,c)=>n+c.length,0));let offset=0;for(const chunk of rec.chunks){input.set(chunk,offset);offset+=chunk.length;}
 const count=Math.min(120*16000,Math.floor(input.length*16000/rec.rate));const buffer=new ArrayBuffer(44+count*2),view=new DataView(buffer);
 const word=(at:number,value:string)=>{for(let i=0;i<value.length;i++)view.setUint8(at+i,value.charCodeAt(i));};
 word(0,'RIFF');view.setUint32(4,36+count*2,true);word(8,'WAVE');word(12,'fmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,1,true);view.setUint32(24,16000,true);view.setUint32(28,32000,true);view.setUint16(32,2,true);view.setUint16(34,16,true);word(36,'data');view.setUint32(40,count*2,true);
 for(let i=0;i<count;i++){const pos=i*rec.rate/16000,index=Math.floor(pos),fraction=pos-index;const sample=input[index]*(1-fraction)+(input[Math.min(index+1,input.length-1)]??0)*fraction;view.setInt16(44+i*2,Math.round(Math.max(-1,Math.min(1,sample))*32767),true);}
 const bytes=new Uint8Array(buffer);let binary='';for(let i=0;i<bytes.length;i+=8192)binary+=String.fromCharCode(...bytes.subarray(i,i+8192));
 finishSpeech();element<HTMLButtonElement>('chat-mic').disabled=true;chatStatus('Transcribing locally…');send('transcribe',{audio:btoa(binary)});
}
window.addEventListener('beforeunload',()=>recording?.stream.getTracks().forEach(t=>t.stop()));
