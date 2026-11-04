// State/DOM-adapter checks, not a browser or pixel-layout test.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const nodes=new Map(),handlers={};
class El{
 constructor(id){this.id=id;this.hidden=false;this.style={};this.dataset={};this.value='';this.isConnected=true;this.attrs={};this.listeners={};this.classes=new Set();this.classList={toggle:(n,v)=>v?this.classes.add(n):this.classes.delete(n)};}
 set innerHTML(s){this.html=s;for(const m of s.matchAll(/<input\b([^>]*)>/g)){const id=/id="([^"]+)"/.exec(m[1]);if(id){const e=get('#'+id[1]);e.value=/value="([^"]*)"/.exec(m[1])?.[1]||'';}}}
 get innerHTML(){return this.html||'';}
 setAttribute(k,v){this.attrs[k]=v;}
 focus(){doc.activeElement=this;}
 blur(){doc.activeElement=get('body');}
 matches(s){return ['hp','maxhp','ac','delta','mapScale'].includes(this.id.slice(1))&&s.includes('input');}
 closest(){return null;}
 addEventListener(k,fn){this.listeners[k]=fn;}
 getBoundingClientRect(){return {left:0,top:0,width:1000,height:600};}
 querySelectorAll(){return [];}
}
function get(id){if(!nodes.has(id))nodes.set(id,new El(id));return nodes.get(id);}
const doc={querySelector:get,querySelectorAll:()=>[],activeElement:get('body'),addEventListener:(k,fn)=>handlers[k]=fn};
const sandbox=vm.createContext({document:doc,window:{innerWidth:1440},console,Math,Number,JSON,String,Object});
get('#modal').hidden=true;
vm.runInContext(fs.readFileSync(__dirname+'/player.js','utf8'),sandbox);
const run=s=>vm.runInContext(s,sandbox),checks=[];
function check(name,fn){fn();checks.push(name);}
check('Selection default; all sidebar tool contexts hidden',()=>{assert.equal(run('state.tool'),'select');for(const s of ['#rulerOptions','#areaOptions','#paintOptions'])assert.equal(get(s).hidden,true);});
check('Ruler and areas expand inline, exclusive active tool',()=>{run("changeTool('ruler')");assert.equal(get('#rulerOptions').hidden,false);run("changeTool('area');act('shape','cone')");assert.equal(get('#rulerOptions').hidden,true);assert.equal(get('#areaOptions').hidden,false);assert.equal(run('state.shape'),'cone');run("changeTool('select')");assert.equal(get('#areaOptions').hidden,true);});
check('Only public stats in token cards; no HP editor',()=>{run('inspect(3)');assert.match(get('#panel').innerHTML,/не опубликованы/);assert.match(get('#panel').innerHTML,/Публичный статблок/);assert.doesNotMatch(get('#panel').innerHTML,/<input|Применить|Разрешить/);assert.equal(run('chosen().hp'),null);assert.equal(run('chosen().ac'),null);});
check('Own token exposes local sheet, other token does not',()=>{run('inspect(1)');assert.match(get('#panel').innerHTML,/Открыть мой лист/);run('inspect(2)');assert.doesNotMatch(get('#panel').innerHTML,/Открыть мой лист/);});
check('One content panel; sheet notes retained in memory',()=>{run("openPanel('sheet');act('sheetTab','Заметки')");handlers.input({target:{id:'sheetNote',value:'Новая заметка'}});run("openPanel('reference');openPanel('sheet')");assert.equal(run('state.panel'),'sheet');assert.match(get('#panel').innerHTML,/Новая заметка/);assert.match(run("referenceHTML('обитель')"),/Старая обитель/);assert.doesNotMatch(run("referenceHTML('обитель')"),/Укрытие/);});
check('Player initiative is read-only, collapse works',()=>{assert.doesNotMatch(get('#initiative').innerHTML,/Следующий ход|data-action="next"/);run("act('initiative')");assert.equal(run('state.collapsed'),true);assert.match(get('#initiative').innerHTML,/Ход: Эллиан/);run("act('next')");assert.equal(run('state.collapsed'),true);});
check('Area and marks; eraser retains area; own clear retains area',()=>{run("changeTool('area');mapClick({clientX:400,clientY:200});changeTool('paint');mapClick({clientX:200,clientY:150})");assert.equal(run('state.marks.length'),2);run("act('clearOwn');act('clearOwnConfirm')");assert.equal(run('state.marks.length'),1);assert.match(run('state.marks[0].type'),/^area/);run("act('clearAreas')");assert.equal(run('state.marks.length'),0);});
check('Dice logs result, clearing dice preserves journal',()=>{run("act('roll','d6');openPanel('journal')");const log=run('state.logs.at(-1)');assert.match(log,/Анна · d6 = [1-6]$/);const n=run('state.logs.length');run("act('clearDice');act('clearDiceConfirm')");assert.equal(run('state.logs.length'),n);});
check('Player request confirmation has no master approval action',()=>{run("act('requestDemo')");assert.doesNotMatch(get('#modalBody').innerHTML,/Разрешить|Отклонить/);run("act('sendRequest')");assert.equal(run('state.request.token'),1);assert.equal(get('#waiting').hidden,false);run("act('cancelRequest')");assert.equal(run('state.request'),null);});
check('Pointer blocks foreign token; own token drag allowed unless awaiting',()=>{run("changeTool('select')");function event(id){return{button:0,clientX:300,clientY:240,target:{closest:()=>({dataset:{token:String(id)}})}};}get('#map').listeners.pointerdown(event(2));assert.equal(run('gesture'),null);get('#map').listeners.pointerdown(event(1));assert.equal(run('gesture.id'),1);run('gesture=null;state.request={token:1}');get('#map').listeners.pointerdown(event(1));assert.equal(run('gesture'),null);run('state.request=null');});
check('Ruler produces non-destructive demo measurement',()=>{run("changeTool('ruler')");get('#map').listeners.pointerdown({button:0,clientX:100,clientY:100,target:{closest:()=>null}});handlers.pointermove({clientX:300,clientY:200});handlers.pointerup();assert.ok(run('state.ruler.x2>state.ruler.x'));assert.match(get('#marks').innerHTML,/демомасштаб/);assert.equal(run('state.tokens[0].x'),32);});
check('Esc handles modal, tool, panel separately',()=>{run("inspect(1);changeTool('paint');act('requestDemo')");const e={key:'Escape',preventDefault(){}};handlers.keydown(e);assert.equal(get('#modal').hidden,true);assert.equal(run('state.tool'),'paint');handlers.keydown(e);assert.equal(run('state.tool'),'select');assert.equal(run('state.panel'),'token');handlers.keydown(e);assert.equal(run('state.panel'),null);});
check('Narrow layout closes sidebar on tool/panel and can reopen',()=>{run("window.innerWidth=700;state.menu=true;changeTool('area')");assert.equal(run('state.menu'),false);run("act('menu')");assert.equal(run('state.menu'),true);assert.equal(get('#areaOptions').hidden,false);run("openPanel('sheet')");assert.equal(run('state.menu'),false);});
check('Modal blocks delegated underlying actions',()=>{run("act('requestDemo')");const before=run('state.tool');handlers.click({target:{closest:()=>({dataset:{tool:'ruler'},closest:()=>null})},preventDefault(){}});assert.equal(run('state.tool'),before);run('closeModal()');});
const html=fs.readFileSync(__dirname+'/index.html','utf8'),css=fs.readFileSync(__dirname+'/player.css','utf8'),js=fs.readFileSync(__dirname+'/player.js','utf8');
check('No horizontal toolbar, no GM navigation, no network calls',()=>{assert.doesNotMatch(html,/class="toolbox"|data-panel="scenes"|data-tool="fog"|data-tool="geometry"/);assert.match(html,/aria-label="Меню игрока"/);assert.match(css,/\.options/);assert.doesNotMatch(js,/\bfetch\s*\(|WebSocket|XMLHttpRequest|localStorage/);});
console.log(JSON.stringify({result:'PASS',count:checks.length,checks,limitations:['State and DOM-adapter checks, not a browser render or real pointer/CSS test.']},null,2));
