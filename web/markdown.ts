// Render a small Markdown subset using DOM nodes only. Model output is never HTML.
function chatInline(target:HTMLElement,text:string):void {
 const pattern=/(`[^`\n]+`|\*\*[^*\n]+\*\*)/g;let at=0;
 for(const match of text.matchAll(pattern)){target.append(document.createTextNode(text.slice(at,match.index)));const node=document.createElement(match[0].startsWith('`')?'code':'strong');node.textContent=match[0].startsWith('`')?match[0].slice(1,-1):match[0].slice(2,-2);target.append(node);at=match.index!+match[0].length;}
 target.append(document.createTextNode(text.slice(at)));
}
function renderChatMarkdown(body:HTMLElement,text:string):void {
 if(!/(^|\n)(#{1,6} |```|[-*] |\d+\. |\|)|\*\*[^*]+\*\*|`[^`]+`/.test(text))return;
 body.replaceChildren();body.classList.add('formatted');const lines=text.split('\n');let index=0;
 const blockStart=(line:string)=>/^(#{1,6} |```|[-*] |\d+\. |\|)/.test(line);
 while(index<lines.length){
  const line=lines[index];if(!line.trim()){index++;continue;}
  if(line.startsWith('```')){
   const language=line.slice(3).trim().slice(0,40),codeLines:string[]=[];index++;while(index<lines.length&&!lines[index].startsWith('```'))codeLines.push(lines[index++]);if(index<lines.length)index++;
   const section=document.createElement('section');section.className='code-block';const heading=document.createElement('div');heading.className='code-heading';const label=document.createElement('span');label.textContent=language||'Code';const copy=document.createElement('button');copy.type='button';copy.textContent='Copy';copy.onclick=async()=>{try{await navigator.clipboard.writeText(codeLines.join('\n'));copy.textContent='Copied';}catch{copy.textContent='Copy unavailable';}};heading.append(label,copy);const pre=document.createElement('pre'),code=document.createElement('code');code.textContent=codeLines.join('\n');pre.append(code);section.append(heading,pre);body.append(section);continue;
  }
  const heading=/^(#{1,6})\s+(.+)$/.exec(line);if(heading){const node=document.createElement('h'+Math.min(4,heading[1].length+1));chatInline(node,heading[2]);body.append(node);index++;continue;}
  if(line.trim().startsWith('|')&&index+1<lines.length&&/^\s*\|?\s*:?-{3}/.test(lines[index+1])){
   const wrapper=document.createElement('div');wrapper.className='table-scroll';const table=document.createElement('table');let first=true;
   while(index<lines.length&&lines[index].trim().startsWith('|')){const row=lines[index++];if(/^\s*\|?\s*:?-{3}/.test(row))continue;const tr=document.createElement('tr');for(const value of row.trim().replace(/^\|/,'').replace(/\|$/,'').split('|')){const cell=document.createElement(first?'th':'td');chatInline(cell,value.trim());tr.append(cell);}table.append(tr);first=false;}
   wrapper.append(table);body.append(wrapper);continue;
  }
  if(/^[-*] |^\d+\. /.test(line)){
   const ordered=/^\d+\. /.test(line),list=document.createElement(ordered?'ol':'ul');while(index<lines.length&&(ordered?/^\d+\. /:/^[-*] /).test(lines[index])){const item=document.createElement('li');chatInline(item,lines[index++].replace(/^([-*]|\d+\.) /,''));list.append(item);}body.append(list);continue;
  }
  const paragraph=document.createElement('p');let content=line;index++;while(index<lines.length&&lines[index].trim()&&!blockStart(lines[index]))content+='\n'+lines[index++];chatInline(paragraph,content);body.append(paragraph);
 }
}
