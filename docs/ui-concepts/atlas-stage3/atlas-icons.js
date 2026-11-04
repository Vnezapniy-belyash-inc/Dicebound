'use strict';
// Decorative inline SVG only. No changes to actions, labels, state or permissions.
(() => {
 const paths={
  select:'M4 3l6 17 3-7 7-3L4 3z',
  token:'M12 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8M4 21v-2a8 8 0 0 1 16 0v2M18 4h5M20.5 1.5v5',
  ruler:'M3 16 16 3l5 5L8 21zM7 12l3 3M11 8l3 3M15 4l3 3',
  area:'M12 3a9 9 0 1 0 9 9M12 3v3M21 12h-3M12 18v3M3 12h3M17 5l2 2',
  paint:'M15 3l6 6-10 10-6-6zM7 17l-4 4h7M14 5l5 5',
  geometry:'M3 20V5h8v6h10v9M7 5V2M21 11V8',
  fog:'M5 18a4 4 0 0 1-1-8 6 6 0 0 1 12-2 5 5 0 1 1 3 10z',
  map:'M3 5l6-2 6 2 6-2v16l-6 2-6-2-6 2zM9 3v16M15 5v16',
  transitions:'M4 7h15l-4-4M19 7l-4 4M20 17H5l4-4M5 17l4 4',
  scenes:'M3 7l9-5 9 5-9 5zM3 12l9 5 9-5M3 17l9 5 9-5',
  tokens:'M12 3a4 4 0 1 0 0 8 4 4 0 0 0 0-8M4 21v-2a8 8 0 0 1 16 0v2',
  library:'M3 4h7a3 3 0 0 1 2 1 3 3 0 0 1 2-1h7v16h-7a3 3 0 0 0-2 1 3 3 0 0 0-2-1H3zM12 5v16',
  sheet:'M6 3h9l4 4v14H6zM15 3v5h4M9 12h7M9 16h7',
  journal:'M5 3h14v18H5zM8 7h8M8 11h8M8 15h5',
  players:'M9 3a3 3 0 1 0 0 6 3 3 0 0 0 0-6M3 20v-3a6 6 0 0 1 12 0v3M17 4a3 3 0 0 1 0 6M18 13a5 5 0 0 1 3 5v2',
  help:'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18M9 9a3 3 0 0 1 6 0c0 2-3 2-3 5M12 17h.01',
  dice:'M12 2l9 5v10l-9 5-9-5V7zM3 7l9 5 9-5M12 12v10',
  home:'M3 4h18v16H3zM3 10h18M9 4v16M15 4v16',
  undo:'M8 5 3 10l5 5M3 10h11a6 6 0 0 1 0 12',
  menu:'M4 5h16M4 12h16M4 19h16',
  requests:'M6 3a3 3 0 0 1 3 3v4H3V6a3 3 0 0 1 3-3M18 12a3 3 0 0 1 3 3v4h-6v-4a3 3 0 0 1 3-3M3 13h6M15 22h6'
 };
 function decorate(){
  document.querySelectorAll('button').forEach(el=>{
   if(el.querySelector('svg.ui-icon'))return;
   const key=el.dataset.tool||el.dataset.panel||({home:'home',dice:'dice',diceDock:'dice',menu:'menu',undo:'undo',clearRuler:'ruler'})[el.dataset.action];
   const path=paths[key==='reference'?'library':key];if(!path)return;
   el.dataset.atlasIcon='true';
   const text=[...el.childNodes].find(n=>n.nodeType===3&&n.textContent.trim());
   if(text)text.textContent=text.textContent.replace(/^[\s◈▤♙▥▧♧↖↔◯▦◇≡?☰]+(?=\s|$)/u,'').trimStart();
   const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');svg.setAttribute('viewBox','0 0 24 24');svg.setAttribute('class','ui-icon');svg.setAttribute('aria-hidden','true');svg.setAttribute('focusable','false');
   const p=document.createElementNS('http://www.w3.org/2000/svg','path');p.setAttribute('d',path);svg.append(p);el.prepend(svg);
  });
 }
 decorate();new MutationObserver(decorate).observe(document.body,{childList:true,subtree:true});
})();
