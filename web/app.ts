type ServiceId = 'comfy' | 'coding';
type ServerState = 'Offline'|'Running'|'Busy'|'UnknownQueue'|'Loading'|'Unreachable'|'Foreign';
interface Definition { kind:string; installDirectory:string; port:number; model:string; contextSize:number; gpuLayers:string }
interface Settings { comfyUI:Definition; coding:Definition; projectDirectory:string }
interface Service { id:ServiceId; name:string; url:string; state:ServerState; running:number; pending:number; loadedModel:string|null; healthy:boolean; queueKnown:boolean; operating:boolean; starting:boolean; stopScheduled:boolean; error:string; models:string[] }
interface Snapshot { type:'snapshot'; settings:Settings; services:Service[]; notice:string }
interface WebBridge { postMessage(message:unknown):void; addEventListener(type:'message',listener:(event:{data:any})=>void):void }
const bridge = (window as unknown as {chrome?:{webview?:WebBridge}}).chrome?.webview;
const element = <T extends HTMLElement>(id:string):T => document.getElementById(id) as T;
const escapeHtml = (value:string):string => value.replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]!));
let state:Snapshot|null=null;
let page='overview';
let settingsDirty=false;
let stopping:ServiceId|null=null;
const renderedCards=new Map<string,string>();
function send(type:string,extra:Record<string,unknown>={}):void { bridge?.postMessage({type,...extra}); }
function service(id:ServiceId):Service|undefined { return state?.services.find(s=>s.id===id); }
function description(s:Service):{label:string;detail:string;color:string} {
 if(s.operating) return {label:s.starting?'Starting …':'Stopping …',detail:s.starting?'Preparing the model. Please wait a moment.':'The server is stopping.',color:'amber'};
 if(s.stopScheduled) return {label:'Stop scheduled',detail:s.queueKnown?`${s.running} active · ${s.pending} queued. Waiting for all jobs.`:'Waiting until the server is reliably idle.',color:'amber'};
 switch(s.state) {
  case 'Offline': return {label:'Stopped',detail:'Ready to start.',color:''};
  case 'Running':return {label:'Running',detail:s.id==='comfy'?'Ready for images and videos.':'Ready for chat and coding assistants.',color:'green'};
  case 'Busy':return {label:s.id==='comfy'?'Rendering':'Generating response',detail:`${s.running} active · ${s.pending} queued`,color:'green'};
  case 'Loading':return {label:'Loading model …',detail:'The server has started and is preparing the model.',color:'amber'};
  case 'UnknownQueue':return {label:'Running · workload unknown',detail:'The server responds, but its workload cannot yet be determined reliably.',color:'amber'};
  case 'Foreign':return {label:'Port in use',detail:'Another application responds on this port.',color:'red'};
  default:return {label:'Checking status',detail:'The server is not responding reliably yet.',color:'amber'};
 }
}
function card(s:Service):string {
 const status=description(s),other=state?.services.find(o=>o.id!==s.id);
 const canStart=!s.operating&&!s.stopScheduled&&s.state==='Offline'&&other?.state==='Offline'&&!other.operating;
 const canStop=!s.operating&&s.state!=='Offline'&&s.state!=='Foreign';
 const selected=state?.settings.coding.model??'';
 const model=s.id==='coding'?`<label class="model-label" for="model-${page}">Model for the next start</label><select id="model-${page}" class="model-select" data-model ${s.state!=='Offline'||s.operating?'disabled':''}>${s.models.map(m=>`<option value="${escapeHtml(m)}" ${m===selected?'selected':''}>${escapeHtml(m)}</option>`).join('')}</select>`:'';
 const tip=!canStart&&s.state==='Offline'&&other?.state!=='Offline'?` Stop ${other?.name??'the other server'} first.`:'';
 return `<article class="service-card" data-service="${s.id}"><div class="card-header"><span class="service-glyph ${s.id==='coding'?'coding-glyph':''}" aria-hidden="true">${s.id==='coding'?'&lt;/&gt;':'◈'}</span><div><h2>${escapeHtml(s.name)}</h2><p>${s.id==='comfy'?'Image & video generation':'Local language models · llama.cpp'}</p></div></div><p class="status"><span class="dot ${status.color}"></span>${status.label}</p><p class="detail">${escapeHtml(status.detail+tip)}</p>${model}${s.error?`<p class="error" role="alert">${escapeHtml(s.error)}</p>`:''}<div class="card-actions"><div class="actions"><button class="primary" data-action="start" ${canStart?'':'disabled'}>Start</button><button class="secondary" data-action="openWorkspace" ${s.healthy?'':'disabled'}>Open in app</button><button class="danger" data-action="${s.stopScheduled?'cancelStop':'stop'}" ${canStop?'':'disabled'}>${s.stopScheduled?'Cancel stop':'Stop'}</button></div><div class="card-footer"><button class="text-button" data-action="openBrowser" ${s.healthy?'':'disabled'}>Open in browser ↗</button><button class="text-button" data-action="openLog">Log</button><button class="text-button" data-action="openFolder">Folder</button></div></div></article>`;
}
function render():void {
 if(!state)return;
 element('notice').hidden=!state.notice; element('notice').textContent=state.notice;
 const running=state.services.filter(s=>s.healthy).length;
 element('server-count').textContent=`${running} of ${state.services.length} services running`;
 const targets:{id:string;services:Service[]}[]=[{id:'overview-cards',services:state.services},{id:'comfy-card',services:state.services.filter(s=>s.id==='comfy')},{id:'coding-card',services:state.services.filter(s=>s.id==='coding')}];
 for(const target of targets) {
  if(element(target.id).closest<HTMLElement>('.page')?.hidden)continue;
  const html=target.services.map(card).join('');
  if(renderedCards.get(target.id)!==html){element(target.id).innerHTML=html;renderedCards.set(target.id,html);}
 }
 element('project-path').textContent=state.settings.projectDirectory;
 element('api-address').textContent=`${service('coding')?.url??''}/v1`;
 if(!settingsDirty) {
  element<HTMLInputElement>('comfy-root').value=state.settings.comfyUI.installDirectory;
  element<HTMLInputElement>('comfy-port').value=String(state.settings.comfyUI.port);
  element<HTMLInputElement>('coding-root').value=state.settings.coding.installDirectory;
  element<HTMLInputElement>('coding-port').value=String(state.settings.coding.port);
  element<HTMLSelectElement>('context').value=String(state.settings.coding.contextSize);
  element<HTMLSelectElement>('gpu').value=state.settings.coding.gpuLayers;
 }
}
document.querySelectorAll<HTMLButtonElement>('[data-page]').forEach(button=>button.addEventListener('click',()=>{
 page=button.dataset.page!;
 document.querySelectorAll<HTMLElement>('.page').forEach(section=>section.hidden=section.id!==`page-${page}`);
 document.querySelectorAll('[data-page]').forEach(item=>item.classList.toggle('active',item===button)); render();
}));
document.addEventListener('click',event=>{
 const button=(event.target as HTMLElement).closest<HTMLButtonElement>('button[data-action]'); if(!button||button.disabled)return;
 const id=button.closest<HTMLElement>('[data-service]')?.dataset.service as ServiceId; const current=service(id); if(!current)return;
 const action=button.dataset.action!;
 if(action==='stop'&&(!current.queueKnown||current.running+current.pending>0)) {
  stopping=id; element('stop-title').textContent=`Stop ${current.name}?`;
  element('stop-detail').textContent=current.queueKnown?`${current.running} active and ${current.pending} queued jobs. Let them finish or cancel immediately.`:'The workload is unknown. Wait until the server is reliably idle or explicitly stop it now.';
  element<HTMLDialogElement>('stop-dialog').showModal();return;
 }
 send(action,{serviceId:id,...(action==='stop'?{mode:'wait'}:{})});
});
document.addEventListener('change',event=>{
 const target=event.target as HTMLSelectElement;
 if(target.matches('[data-model]'))send('selectModel',{model:target.value});
});
element<HTMLDialogElement>('stop-dialog').addEventListener('close',()=>{
 const dialog=element<HTMLDialogElement>('stop-dialog'); const mode=dialog.returnValue;
 if(stopping&&(mode==='wait'||mode==='force'))send('stop',{serviceId:stopping,mode}); stopping=null; dialog.returnValue='';
});
element('project').addEventListener('click',()=>send('chooseProject'));
element('editor').addEventListener('click',()=>send('openEditor'));
element('repository').addEventListener('click',()=>send('openRepository'));
element('copy-api').addEventListener('click',()=>send('copyApi'));
document.querySelectorAll<HTMLButtonElement>('.browse').forEach(button=>button.addEventListener('click',()=>send('browseFolder',{field:button.dataset.field})));
element('settings-form').addEventListener('input',()=>{settingsDirty=true;element('settings-result').textContent='';});
element('settings-form').addEventListener('submit',event=>{
 event.preventDefault();if(!state)return;
 const updated:Settings={...state.settings,
  comfyUI:{...state.settings.comfyUI,installDirectory:element<HTMLInputElement>('comfy-root').value,port:Number(element<HTMLInputElement>('comfy-port').value)},
  coding:{...state.settings.coding,installDirectory:element<HTMLInputElement>('coding-root').value,port:Number(element<HTMLInputElement>('coding-port').value),contextSize:Number(element<HTMLSelectElement>('context').value),gpuLayers:element<HTMLSelectElement>('gpu').value}};
 send('saveSettings',{settings:updated});
});
bridge?.addEventListener('message',event=>{
 if(event.data.type==='snapshot'){state=event.data as Snapshot;render();}
 if(event.data.type==='settingsSaved'){settingsDirty=false;element('settings-result').textContent='Saved';}
 if(event.data.type==='folderChosen'){
  const field=event.data.field as string;
  if(field==='comfy'||field==='coding'){element<HTMLInputElement>(`${field}-root`).value=event.data.path as string;settingsDirty=true;}
 }
 if(event.data.type==='apiCopied'){element('copy-api').textContent='Copied ✓';}
});
if(bridge)send('ready');else{element('notice').hidden=false;element('notice').textContent='Interface preview · Server controls are available only in the Windows app.';}
