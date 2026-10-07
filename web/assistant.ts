let chatBusy=false, currentAnswer:HTMLElement|null=null, reviewId:string|null=null;
interface Usage {used:number|null;capacity:number;reserve:number;exact:boolean;source:string;percent:number|null}
interface TaskProgress {round:number;phase:string;completedTools:number;savedFiles:number;lastAction:string;recoveries:number;thinkingEnabled:boolean;request?:string}
interface Session {id:string;title:string;workspace:string;model:string;access:string;allowScripts:boolean;compaction:string;replyReserve:number;automaticReplyBudget?:boolean;thinkingEnabled?:boolean;progress?:TaskProgress|null;status:string;entries:{role:string;text:string;images:string[];round?:number}[];memory:{usage:Usage|null};checkpoints:{id:string;created:string;before:number;after:number}[]}
let selectedSession:Session|null=null,selectedId:string|null=null,activeId:string|null=null,reviewSession:string|null=null,speechTarget:string|null=null;
const measuredSessions=new Set<string>();
let taskProgress:TaskProgress|null=null;
let menuSession:{id:string;title:string;status:string}|null=null,renameTarget:string|null=null,deleteTarget:string|null=null,menuOrigin:HTMLButtonElement|null=null;
try{element('context-panel').hidden=localStorage.getItem('ai-control.context-visible')==='false';}catch{/* WebView storage can be unavailable. */}
function contextVisibility(visible:boolean):void {
 element('context-panel').hidden=!visible;element('context-toggle').setAttribute('aria-expanded',String(visible));
 try{localStorage.setItem('ai-control.context-visible',String(visible));}catch{/* Keep the current window preference. */}
 notifyViewPreferences();
}
function contextCommand(input:HTMLTextAreaElement):boolean {
 const match=/^\/context(?:\s+(show|hide))?$/i.exec(input.value.trim());if(!match)return false;
 const panel=element('context-panel');contextVisibility(match[1]?.toLowerCase()==='hide'?false:match[1]?.toLowerCase()==='show'?true:panel.hidden);
 input.value='';resizeComposer();element('chat-command-hint').textContent=`Context ${panel.hidden?'hidden':'shown'} · Enter to send · Shift+Enter for a new line.`;return true;
}
function resizeComposer():void {const input=element<HTMLTextAreaElement>('chat-prompt');input.style.height='auto';input.style.height=Math.min(180,Math.max(64,input.scrollHeight))+'px';}
function closeChatMenu():void {element('chat-menu').hidden=true;menuSession=null;}
function openChatMenu(session:{id:string;title:string;status:string},x:number,y:number,origin:HTMLButtonElement):void {
 menuSession=session;menuOrigin=origin;const menu=element('chat-menu');menu.hidden=false;
 menu.style.left=Math.max(8,Math.min(x,innerWidth-180))+'px';menu.style.top=Math.max(8,Math.min(y,innerHeight-menu.offsetHeight-8))+'px';
 element<HTMLButtonElement>('chat-delete').disabled=['Queued','Running','Compacting','Waiting for approval'].includes(session.status);element('chat-rename').focus();
}
function renderTask(progress:TaskProgress|null,status=selectedSession?.status??'Ready'):void {
 taskProgress=progress;
 element('task-phase').textContent=progress?`${progress.phase}${progress.round?' · round '+progress.round+' / 16':''}${progress.recoveries?' · recovery '+progress.recoveries+' / 2':''}`:status;
 element('task-actions').textContent=progress?`${progress.completedTools} ${progress.completedTools===1?'tool':'tools'} completed · ${progress.savedFiles} ${progress.savedFiles===1?'file':'files'} saved · Thinking ${progress.thinkingEnabled?'on':'off'}`:'Tool actions and saved files appear in Activity. Internal thinking does not create files.';
 element('task-last-action').textContent=progress?`Last action: ${progress.lastAction}`:'No task started.';
 element('task-request').textContent=progress?.request?`Request: ${progress.request}`:'';element('task-request').hidden=!progress?.request;
}
function recordedTask(session:Session):TaskProgress|null {
 const latest=session.entries.map(entry=>entry.role).lastIndexOf('user');if(latest<0)return null;
 const tools=session.entries.slice(latest+1).filter(entry=>entry.role==='activity'&&/^(list_files|read_file|search_text|write_file|run_script)(?: |\n)/.test(entry.text));
 return {round:0,phase:session.status,completedTools:tools.length,savedFiles:tools.filter(entry=>/^write_file[^\n]*\nSaved /.test(entry.text)).length,lastAction:tools.at(-1)?.text.split('\n')[0]??'No workspace tools completed yet.',recoveries:0,thinkingEnabled:session.thinkingEnabled!==false,request:session.entries[latest].text.slice(0,200)};
}
function tryMeasure():void {
 if(selectedSession&&selectedSession.memory?.usage?.used==null&&!chatBusy&&!state?.chatBusy&&service('coding')?.healthy&&selectedSession.model===state?.settings.coding.model&&!measuredSessions.has(selectedSession.id)){measuredSessions.add(selectedSession.id);chatSend('chatMeasure');}
}
const drafts=new Map<string,{text:string;images:{name:string;data:string}[]}>();
function chatSend(type:string,extra:Record<string,unknown>={}):void {send(type,{...(selectedId?{sessionId:selectedId}:{}),...extra});}
function renderUsage(usage:Usage|null):void {
 const progress=element<HTMLProgressElement>('context-progress');
 progress.max=usage?.capacity??state?.settings.coding.contextSize??100;progress.value=usage?.used??0;
 progress.classList.toggle('warning',(usage?.percent??0)>=70);
 element('context-usage').textContent=usage?.used!=null?`${Math.max(0,100-(usage.percent??100*usage.used/usage.capacity)).toFixed(0)}% left (${usage.exact?'':'≈ '}${usage.used.toLocaleString()} / ${usage.capacity.toLocaleString()} tokens)`:`Not measured · ${usage?'active':'configured'} ${(usage?.capacity??state?.settings.coding.contextSize)?.toLocaleString()??'—'} tokens`;
 element('context-ring').setAttribute('stroke-dasharray',`${Math.min(100,Math.max(0,usage?.percent??0))} 100`);element('context-toggle').classList.toggle('warning',(usage?.percent??0)>=70);element('context-toggle').title=element('context-usage').textContent??'Context status';
 element('context-usage').title='Last measured context. Streaming thinking is included in the final server count; character counts are not token counts.';
 element('context-source').textContent=usage?`${usage.source} · ${selectedSession?.automaticReplyBudget===false?'Custom':'Automatic'} output budget ${usage.used==null?'computed before the next request':'up to '+(selectedSession?.automaticReplyBudget===false?Math.min(selectedSession.replyReserve,Math.max(0,usage.capacity-usage.used-256)):Math.max(0,usage.capacity-usage.used-256)).toLocaleString()+' tokens'}, plus 256 safety tokens. Last measured; refreshed before each request.`:'Tokens are counted before the next request. Start the selected model or refresh when idle; rules and tools are included.';
}
function renderModels():void {
 const list=element<HTMLSelectElement>('chat-model-select'),models=service('coding')?.models??[];const desired=selectedSession?.model??state?.settings.coding.model??'';
 if(list.dataset.models!==models.join('|')+'|'+desired){list.replaceChildren();for(const name of [...new Set([...models,desired])].filter(Boolean)){const option=document.createElement('option');option.value=name;option.textContent=name.replace(/\.gguf$/i,'').replace(/-(?:UD-)?(?:IQ|Q)\d.*$/i,'');list.append(option);}list.value=desired;list.title=desired;list.dataset.models=models.join('|')+'|'+desired;}
}
function renderSession(session:Session):void {
 const changed=selectedId!==session.id;
 const history=element('chat-history'),atBottom=history.scrollHeight-history.scrollTop-history.clientHeight<80;
 if(changed){
  if(selectedId)drafts.set(selectedId,{text:element<HTMLTextAreaElement>('chat-prompt').value,images:attachments});
  const draft=drafts.get(session.id);element<HTMLTextAreaElement>('chat-prompt').value=draft?.text??'';attachments=draft?.images??[];renderAttachments();resizeComposer();
 }
 selectedId=session.id;selectedSession=session;currentAnswer=null;if(changed){element('rules-list').replaceChildren();chatSend('loadRules');}
 element('chat-title').textContent=session.title;element('chat-title').title=session.title;element('chat-path').textContent=session.workspace;element('context-session').textContent=session.id;
 element('chat-model').textContent=`${session.model} · ${session.status}`;
 element<HTMLSelectElement>('chat-access').value=session.access;renderAccess();element<HTMLInputElement>('chat-scripts').checked=session.allowScripts;
 element<HTMLSelectElement>('context-mode').value=session.compaction;element<HTMLInputElement>('context-reserve').value=String(session.replyReserve);element<HTMLSelectElement>('context-budget-mode').value=session.automaticReplyBudget===false?'Custom':'Automatic';element('context-reserve-label').hidden=session.automaticReplyBudget!==false;
 element<HTMLInputElement>('chat-thinking').checked=session.thinkingEnabled!==false;renderTask(session.progress??recordedTask(session),session.status);
 element('chat-history').replaceChildren();let afterUser=false;
 for(const entry of session.entries){const body=addMessage(entry.role,entry.text);if(entry.role==='assistant'&&(!['Running','Queued'].includes(session.status)||entry.round!==session.progress?.round))renderChatMarkdown(body,entry.text);for(const data of entry.images??[]){const img=document.createElement('img');img.src=data;img.alt='Saved image attachment';body.parentElement?.append(img);}if(entry.role==='user'){afterUser=true;currentAnswer=null;}if(entry.role==='assistant'&&afterUser&&entry.round===session.progress?.round)currentAnswer=body;}
 const busy=['Queued','Running','Compacting','Waiting for approval'].includes(session.status);setChatBusy(busy);
 if(busy&&!currentAnswer&&session.status!=='Compacting'){currentAnswer=addMessage('assistant','');pendingAnswer(session.status==='Queued'?'Queued · waiting for the other chat…':'Waiting for the model response…');}
 if(!busy)currentAnswer=null;
 if(changed||atBottom)history.scrollTop=history.scrollHeight;
 renderUsage(session.memory?.usage??null);renderModels();tryMeasure();
 const checkpoints=element<HTMLSelectElement>('context-checkpoints');checkpoints.replaceChildren();
 for(const point of [...session.checkpoints].reverse()){const option=document.createElement('option');option.value=point.id;option.textContent=`${new Date(point.created).toLocaleString()} · ${point.before.toLocaleString()} → ${point.after.toLocaleString()} tokens`;checkpoints.append(option);}
 element<HTMLButtonElement>('context-restore').disabled=busy||!session.checkpoints.length;chatStatus(session.status);
 if(!session.entries.length){const empty=document.createElement('p');empty.className='chat-empty';empty.textContent='What would you like to work on?';element('chat-history').append(empty);}
}
let attachments:{name:string;data:string}[]=[];
function showChat():void { element<HTMLButtonElement>('page-chat').hidden=false; document.querySelector<HTMLButtonElement>('[data-page="chat"]')?.click();chatSend('loadRules'); }
function chatStatus(text:string):void { element('chat-status').textContent=text; }
function pendingAnswer(text:string):void {
 if(currentAnswer && (!currentAnswer.textContent || currentAnswer.dataset.pending==='true')){currentAnswer.textContent=text;currentAnswer.dataset.pending='true';}
}
function setChatBusy(busy:boolean):void {
 chatBusy=busy;
 for(const id of ['chat-project','chat-access','chat-scripts','chat-thinking','chat-send','chat-model-select','context-mode','context-budget-mode','context-reserve','context-compact','context-refresh','context-restore'])element<HTMLButtonElement>(id).disabled=busy;
 element('chat-cancel').hidden=!busy;
 element('chat-send').hidden=busy;const sendLabel=activeId&&activeId!==selectedId?'Queue request':'Send';element('chat-send').setAttribute('aria-label',sendLabel);element('chat-send').title=sendLabel;
}
function addMessage(role:string,text:string):HTMLElement {
 const history=element('chat-history');history.querySelector('.chat-empty')?.remove();
 const article=document.createElement(role==='activity'?'details':'article');article.className=`chat-message ${role}`;article.setAttribute('aria-label',role==='user'?'You':role==='assistant'?'Assistant':'Activity');
 const label=document.createElement(role==='activity'?'summary':'strong');label.textContent=role==='activity'?text.split('\n')[0].slice(0,180):role==='user'?'You':'Assistant';if(role!=='activity')label.className='sr-only';article.append(label);
 if(role==='activity'&&/error|failed|denied|not be counted/i.test(text))(article as HTMLDetailsElement).open=true;
 const body=document.createElement('div');body.className='message-body';body.textContent=text;article.append(body);const atBottom=history.scrollHeight-history.scrollTop-history.clientHeight<80;history.append(article);if(atBottom)history.scrollTop=history.scrollHeight;return body;
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
document.querySelector('[data-page="chat"]')?.addEventListener('click',()=>{chatSend('loadRules');requestAnimationFrame(()=>{const history=element('chat-history');history.scrollTop=history.scrollHeight;});});
function renderAccess():void {const select=element<HTMLSelectElement>('chat-access');select.title=select.value==='WorkspaceWrite'?'Create and change files in this folder without asking. Previous versions are backed up; scripts still require approval.':'File access within the selected workspace';select.classList.toggle('automatic-write',select.value==='WorkspaceWrite');}
function permissions():void {renderAccess();chatSend('assistantPermissions',{access:element<HTMLSelectElement>('chat-access').value,allowScripts:element<HTMLInputElement>('chat-scripts').checked});}
element('chat-access').onchange=permissions;element('chat-scripts').onchange=permissions;
element('chat-new').onclick=()=>send('chatNew');element('chat-cancel').onclick=()=>chatSend('chatCancel');
element('chat-form').onsubmit=event=>{
 event.preventDefault();const input=element<HTMLTextAreaElement>('chat-prompt');if(contextCommand(input))return;if(chatBusy)return;const prompt=input.value.trim();if(!prompt&&!attachments.length)return;
 if(!service('coding')?.healthy){chatStatus('Start the coding server first.');return;}
 addMessage('user',prompt+(attachments.length?`\n[${attachments.length} image attachments]`:''));currentAnswer=addMessage('assistant','');pendingAnswer('Waiting for the model response…');setChatBusy(true);chatStatus('Preparing request…');
 chatSend('chatSend',{prompt,images:attachments.map(i=>i.data)});input.value='';attachments=[];renderAttachments();
 resizeComposer();
};
element('chat-prompt').addEventListener('input',resizeComposer);
element('chat-prompt').addEventListener('keydown',event=>{const key=event as KeyboardEvent;if(key.key==='Enter'&&!key.shiftKey&&!key.isComposing){key.preventDefault();if(!contextCommand(element<HTMLTextAreaElement>('chat-prompt'))&&!chatBusy)element<HTMLFormElement>('chat-form').requestSubmit();}});
function reviewDecision(approved:boolean):void {if(reviewId)send('reviewDecision',{id:reviewId,...(reviewSession?{sessionId:reviewSession}:{}),approved});element<HTMLDialogElement>('review-dialog').close();reviewId=null;}
element('review-approve').onclick=()=>reviewDecision(true);element('review-deny').onclick=()=>reviewDecision(false);
element('review-dialog').addEventListener('cancel',event=>{event.preventDefault();reviewDecision(false);});
bridge?.addEventListener('message',event=>{
 const {type,value,sessionId}=event.data;
 if(type==='chatSessions'){
  activeId=event.data.activeId;
  const list=element('chat-sessions');list.replaceChildren();
  closeChatMenu();for(const session of event.data.sessions as Session[]){const button=document.createElement('button');button.className='session-item'+(session.id===event.data.selectedId?' selected':'');button.title=session.title+' · '+session.status;const title=document.createElement('span');title.textContent=session.title;const status=document.createElement('i');status.className=['Running','Compacting','Waiting for approval'].includes(session.status)?'session-running':session.status==='Queued'?'session-queued':'';status.title=session.status;button.append(title,status);button.onclick=()=>{showChat();send('chatSelect',{sessionId:session.id});};button.oncontextmenu=event=>{event.preventDefault();openChatMenu(session,event.clientX,event.clientY,button);};button.onkeydown=event=>{if(event.key==='ContextMenu'||event.key==='F10'&&event.shiftKey){event.preventDefault();const rect=button.getBoundingClientRect();openChatMenu(session,rect.left+30,rect.bottom,button);}};list.append(button);}
  if(event.data.current)renderSession(event.data.current);return;
 }
 if(type==='speechResult'&&sessionId&&sessionId!==selectedId){const draft=drafts.get(sessionId)??{text:'',images:[]};draft.text+=(draft.text?'\n':'')+String(value);drafts.set(sessionId,draft);chatStatus('Dictation added to the original chat draft.');finishSpeech();return;}
 if(sessionId&&selectedId&&sessionId!==selectedId&&!type.startsWith('speech'))return;
 if(type==='contextUsage'){renderUsage(value as Usage);if(selectedSession)selectedSession.memory.usage=value;}
 if(type==='taskProgress'){renderTask(value as TaskProgress);if(selectedSession)selectedSession.progress=value;}
 if(type==='modelRound'){if(currentAnswer?.dataset.pending==='true')currentAnswer.parentElement?.remove();else if(currentAnswer)renderChatMarkdown(currentAnswer,currentAnswer.textContent??'');currentAnswer=null;}
 if(type==='taskRecovery'){if(currentAnswer?.dataset.pending==='true')currentAnswer.parentElement?.remove();else if(currentAnswer)renderChatMarkdown(currentAnswer,currentAnswer.textContent??'');currentAnswer=null;addMessage('activity',String(value));chatStatus('Saving checkpoint and resuming…');}
 if(type==='contextCompacted')addMessage('activity',`Context compacted: ${value.before.toLocaleString()} → ${value.after.toLocaleString()} tokens. Original transcript and a restore checkpoint were saved.`);
 if(type==='compactionProgress')chatStatus('Compacting working context locally…');
 if(type==='snapshot'){
  const snapshot=event.data as Snapshot;const coding=snapshot.settings.coding;
  if(!selectedSession){element('chat-path').textContent=snapshot.settings.projectDirectory;element('chat-model').textContent=`${coding.model} · ${coding.contextSize.toLocaleString()} context tokens`;element<HTMLSelectElement>('chat-access').value=snapshot.settings.assistant?.access??'ReadOnly';element<HTMLInputElement>('chat-scripts').checked=snapshot.settings.assistant?.allowScriptExecution??false;}
  element('global-rules-path').textContent=`Global rules: ${snapshot.globalRulesPath??'Local AI Control AGENTS.md'}`;renderModels();tryMeasure();
 }
 if(type==='openChat')showChat();
 if(type==='chatStatus'){chatStatus(String(value));if(String(value).startsWith('Using '))pendingAnswer('The assistant is using workspace tools. See activity below.');}
 if(type==='chatProgress'){
  if(taskProgress){renderTask({...taskProgress,phase:String(value.phase)});if(selectedSession)selectedSession.progress=taskProgress;}
  const seconds=Math.max(0,Number(value.elapsedSeconds)||0),characters=Math.max(0,Number(value.reasoningCharacters)||0);
  if(taskProgress)element('task-phase').textContent=`${value.phase==='thinking'?'Thinking · '+characters.toLocaleString()+' reasoning characters':value.phase==='answering'?'Writing answer':'Preparing'} · ${seconds}s · round ${taskProgress.round} / 16`;
  if(value.phase==='thinking'){chatStatus(`Thinking · ${characters.toLocaleString()} reasoning characters · ${seconds}s for this response`);pendingAnswer('The model is thinking. Its progress appears below; no final answer yet.');}
  else if(value.phase==='answering')chatStatus(`Writing answer · ${seconds}s for this response`);
 }
 if(type==='chatToken'){const history=element('chat-history'),atBottom=history.scrollHeight-history.scrollTop-history.clientHeight<80;if(!currentAnswer)currentAnswer=addMessage('assistant','');if(currentAnswer.dataset.pending==='true'){currentAnswer.textContent='';delete currentAnswer.dataset.pending;}const last=currentAnswer.lastChild;if(last?.nodeType===Node.TEXT_NODE)(last as Text).appendData(String(value));else currentAnswer.append(document.createTextNode(String(value)));if(atBottom)history.scrollTop=history.scrollHeight;}
 if(type==='chatDone'){pendingAnswer('Completed without a text response. See Activity.');if(currentAnswer)renderChatMarkdown(currentAnswer,currentAnswer.textContent??'');setChatBusy(false);chatStatus('Finished · all processing stays local.');currentAnswer=null;}
 if(type==='chatIncomplete'){pendingAnswer('No final answer was returned within this output budget.');if(currentAnswer)renderChatMarkdown(currentAnswer,currentAnswer.textContent??'');addMessage('activity',String(value));setChatBusy(false);chatStatus('Incomplete · partial response retained.');currentAnswer=null;}
 if(type==='contextWarning')addMessage('activity',String(value));
 if(type==='chatError'){pendingAnswer('No final answer was returned. See the explanation below.');addMessage('activity',String(value));setChatBusy(false);if(taskProgress)renderTask({...taskProgress,phase:'Stopped'});chatStatus(String(value));currentAnswer=null;}
 if(type==='chatCleared'){element('chat-history').replaceChildren();currentAnswer=null;setChatBusy(false);chatStatus('New chat · instructions will be loaded again.');chatSend('loadRules');}
 if(type==='rulesLoaded'){const list=element('rules-list');list.replaceChildren();for(const path of value as string[]){const item=document.createElement('li');item.textContent=path;list.append(item);}}
 if(type==='toolResult')addMessage('activity',String(value));
 if(type==='toolReview'){
  reviewId=value.id;reviewSession=sessionId??selectedId;chatStatus('Waiting for your approval…');element('review-title').textContent=`${selectedSession?.title?selectedSession.title+' · ':''}${value.kind==='run_script'?'Run script':'Save file'}: ${value.path}`;
  element('review-warning').textContent=value.warning;element('review-before').textContent=value.before??'(New file)';element('review-after').textContent=value.after;
  element<HTMLDialogElement>('review-dialog').showModal();
 }
 if(type==='reviewClosed'){reviewId=null;element<HTMLDialogElement>('review-dialog').close();}
 if(type==='bridgeError'){chatStatus(String(value));setChatBusy(selectedSession?['Queued','Running','Compacting','Waiting for approval'].includes(selectedSession.status):false);}
 if(type==='speechStatus'){chatStatus(String(value));element('speech-cancel').hidden=false;element<HTMLButtonElement>('chat-mic').disabled=true;}
 if(type==='speechResult'){
  const input=element<HTMLTextAreaElement>('chat-prompt');input.value+=(input.value?'\n':'')+String(value);input.focus();chatStatus('Transcribed locally. Review the text, then choose Send.');finishSpeech();
  resizeComposer();
 }
 if(type==='speechError'){chatStatus(String(value));finishSpeech();}
});
let recording: {stream:MediaStream;context:AudioContext;processor:ScriptProcessorNode;source:MediaStreamAudioSourceNode;chunks:Float32Array[];timer:number;rate:number}|null=null;
function micState(active:boolean):void {element('chat-mic').innerHTML=active?'<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="7" y="7" width="10" height="10" rx="1"/></svg>':'<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="3" width="6" height="12" rx="3"/><path d="M6 10v2a6 6 0 0 0 12 0v-2M12 18v3M9 21h6"/></svg>';element('chat-mic').setAttribute('aria-label',active?'Stop recording':'Dictate');element('chat-mic').title=active?'Stop recording':'Dictate';element('chat-mic').classList.toggle('recording',active);}
function finishSpeech():void {element('speech-cancel').hidden=true;element<HTMLButtonElement>('chat-mic').disabled=false;micState(false);}
element('speech-cancel').onclick=()=>send('speechCancel');
element('chat-mic').onclick=async()=>{
 if(recording){await stopRecording();return;}
 let stream:MediaStream|null=null, context:AudioContext|null=null;
 try{
  speechTarget=selectedId;stream=await navigator.mediaDevices.getUserMedia({audio:{channelCount:1},video:false});context=new AudioContext();await context.resume();
  const source=context.createMediaStreamSource(stream),processor=context.createScriptProcessor(4096,1,1),chunks:Float32Array[]=[];
  processor.onaudioprocess=event=>{chunks.push(new Float32Array(event.inputBuffer.getChannelData(0)));};source.connect(processor);processor.connect(context.destination);
  recording={stream,context,source,processor,chunks,timer:window.setTimeout(()=>void stopRecording(),120000),rate:context.sampleRate};
  micState(true);chatStatus('Recording locally… stop to transcribe (maximum two minutes).');
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
 finishSpeech();element<HTMLButtonElement>('chat-mic').disabled=true;chatStatus('Transcribing locally…');send('transcribe',{...(speechTarget?{sessionId:speechTarget}:{}),audio:btoa(binary)});
}
window.addEventListener('beforeunload',()=>recording?.stream.getTracks().forEach(t=>t.stop()));

element('chat-rename').onclick=()=>{if(!menuSession)return;renameTarget=menuSession.id;element<HTMLInputElement>('chat-name').value=menuSession.title;closeChatMenu();element<HTMLDialogElement>('chat-name-dialog').showModal();};
element('chat-name-cancel').onclick=()=>element<HTMLDialogElement>('chat-name-dialog').close();
element('chat-name-form').onsubmit=event=>{event.preventDefault();if(renameTarget)send('chatRename',{sessionId:renameTarget,title:element<HTMLInputElement>('chat-name').value});element<HTMLDialogElement>('chat-name-dialog').close();renameTarget=null;};
element('chat-delete').onclick=()=>{if(!menuSession)return;deleteTarget=menuSession.id;closeChatMenu();element<HTMLDialogElement>('chat-delete-dialog').showModal();};
element('chat-delete-cancel').onclick=()=>element<HTMLDialogElement>('chat-delete-dialog').close();
element('chat-delete-confirm').onclick=()=>{if(deleteTarget)send('chatDelete',{sessionId:deleteTarget});element<HTMLDialogElement>('chat-delete-dialog').close();deleteTarget=null;};
element('chat-model-select').onchange=()=>chatSend('chatModel',{model:element<HTMLSelectElement>('chat-model-select').value});
function saveContextOptions():void {const reserve=element<HTMLInputElement>('context-reserve');if(!reserve.reportValidity())return;chatSend('chatContextOptions',{mode:element<HTMLSelectElement>('context-mode').value,reserve:Number(reserve.value),automaticReplyBudget:element<HTMLSelectElement>('context-budget-mode').value==='Automatic'});}
element('context-budget-mode').onchange=()=>{element('context-reserve-label').hidden=element<HTMLSelectElement>('context-budget-mode').value==='Automatic';saveContextOptions();};element('context-mode').onchange=saveContextOptions;element('context-reserve').onchange=saveContextOptions;
element('context-refresh').onclick=()=>chatSend('chatMeasure');element('context-compact').onclick=()=>chatSend('chatCompact');
element('context-restore').onclick=()=>chatSend('chatRestore',{id:element<HTMLSelectElement>('context-checkpoints').value});
element('chat-thinking').onchange=()=>chatSend('chatThinking',{enabled:element<HTMLInputElement>('chat-thinking').checked});
element('context-close').onclick=()=>contextVisibility(false);element('context-toggle').onclick=()=>contextVisibility(element('context-panel').hidden);
document.addEventListener('pointerdown',event=>{if(!element('chat-menu').contains(event.target as Node))closeChatMenu();});
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!element('chat-menu').hidden){closeChatMenu();menuOrigin?.focus();}else if(!element('chat-menu').hidden&&(event.key==='ArrowDown'||event.key==='ArrowUp')){event.preventDefault();const items=Array.from(element('chat-menu').querySelectorAll<HTMLButtonElement>('button:not(:disabled)'));items[(items.indexOf(document.activeElement as HTMLButtonElement)+(event.key==='ArrowDown'?1:items.length-1))%items.length]?.focus();}});
window.addEventListener('resize',closeChatMenu);element('chat-sessions').addEventListener('scroll',closeChatMenu);
