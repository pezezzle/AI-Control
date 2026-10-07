let uiZoom=1;
try{document.body.classList.toggle('sidebar-hidden',localStorage.getItem('ai-control.sidebar-visible')==='false');const saved=Number(localStorage.getItem('ai-control.zoom')??1);if(Number.isFinite(saved)&&saved>=.75&&saved<=1.5)uiZoom=saved;}catch{/* Keep window defaults. */}
function notifyViewPreferences():void {
 send('viewPreferences',{sidebar:!document.body.classList.contains('sidebar-hidden'),context:!element('context-panel').hidden,zoom:uiZoom});
}
function uiAction(action:string,text?:string):void {
 if(['overview','comfy','coding','chat','settings'].includes(action)){document.querySelector<HTMLButtonElement>(`[data-page="${action}"]`)?.click();return;}
 if(action==='newChat'){send('chatNew');showChat();return;}
 if(action==='chooseWorkspace'){send('chooseProject');return;}
 if(action==='toggleSidebar'){const visible=document.body.classList.toggle('sidebar-hidden')===false;try{localStorage.setItem('ai-control.sidebar-visible',String(visible));}catch{}notifyViewPreferences();return;}
 if(action==='toggleContext'){contextVisibility(element('context-panel').hidden);return;}
 if(action==='zoomIn'||action==='zoomOut'||action==='resetZoom'){uiZoom=action==='resetZoom'?1:Math.min(1.5,Math.max(.75,Math.round((uiZoom+(action==='zoomIn'?.1:-.1))*100)/100));try{localStorage.setItem('ai-control.zoom',String(uiZoom));}catch{}notifyViewPreferences();return;}
 if(action.startsWith('help')){const kind=action.slice(4).toLowerCase();if(['guide','keys','about'].includes(kind)){const dialog=element<HTMLDialogElement>('help-dialog');for(const section of Array.from(dialog.querySelectorAll<HTMLElement>('[data-help]')))section.hidden=section.dataset.help!==kind;dialog.showModal();}return;}
 const field=document.activeElement as HTMLInputElement|HTMLTextAreaElement|null;
 const editable=field instanceof HTMLTextAreaElement||field instanceof HTMLInputElement&&field.selectionStart!==null;
 if(action==='copy'||action==='cut'){
  const selected=editable?field!.value.slice(field!.selectionStart??0,field!.selectionEnd??0):window.getSelection()?.toString()??'';
  if(selected){send('copyText',{text:selected});if(action==='cut'&&editable&&!field!.readOnly&&!field!.disabled)document.execCommand('delete');}return;
 }
 if(action==='paste'&&text!==undefined&&editable&&!field!.readOnly&&!field!.disabled){if(!document.execCommand('insertText',false,text)){field!.setRangeText(text,field!.selectionStart??0,field!.selectionEnd??0,'end');field!.dispatchEvent(new Event('input',{bubbles:true}));}return;}
 if(action==='undo'||action==='redo'||action==='selectAll')document.execCommand(action);
}
bridge?.addEventListener('message',event=>{if(event.data.type==='menuAction')uiAction(String(event.data.value),event.data.text);});
document.addEventListener('DOMContentLoaded',()=>{element('context-toggle').setAttribute('aria-expanded',String(!element('context-panel').hidden));notifyViewPreferences();});
document.addEventListener('keydown',event=>{
 if(event.altKey||!event.ctrlKey||event.isComposing)return;
 let action:string|undefined;
 if(event.key.toLowerCase()==='n'&&!event.shiftKey)action='newChat';
 if(event.key.toLowerCase()==='o'&&event.shiftKey)action='chooseWorkspace';
 if(event.key===','&&!event.shiftKey)action='settings';
 if(event.key.toLowerCase()==='b'&&!event.shiftKey)action='toggleSidebar';
 if(event.key.toLowerCase()==='k'&&event.shiftKey)action='toggleContext';
 if(event.key==='+'||event.key==='=')action='zoomIn';if(event.key==='-')action='zoomOut';if(event.key==='0')action='resetZoom';
 if(action){event.preventDefault();uiAction(action);}
});
element('help-close').onclick=()=>element<HTMLDialogElement>('help-dialog').close();
