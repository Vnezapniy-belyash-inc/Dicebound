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
const sandbox=vm.createContext({document:doc,console,Math,Number,JSON,String});
vm.runInContext(fs.readFileSync(__dirname+'/prototype.js','utf8'),sandbox);
const run=s=>vm.runInContext(s,sandbox),checks=[];
function check(name,fn){fn();checks.push(name);}
function reset(){run('state=initial();render()');get('#modal').hidden=true;}
check('Default selection hides context, combat initiative visible',()=>{assert.equal(get('#context').hidden,true);assert.match(get('#initiative').innerHTML,/Раунд 3/);});
check('Combat toggles collapse; tool access survives',()=>{run("act('combat')");assert.equal(run('state.combat'),false);assert.match(get('#initiative').innerHTML,/вне боя/);assert.match(get('#tools').innerHTML,/Геометрия/);run("act('combat')");assert.match(get('#initiative').innerHTML,/Раунд 3/);});
check('Next turn wraps and increments round',()=>{run("act('next');act('next');act('next')");assert.equal(run('state.turn'),0);assert.equal(run('state.round'),4);});
check('D/E share one active tool; context disappears for selection',()=>{run("changeTool('fog')");assert.equal(run('state.tool'),'fog');assert.equal(get('#context').hidden,false);assert.match(get('#context').innerHTML,/Раскрыть/);run("changeTool('geometry')");assert.equal(run('state.tool'),'geometry');assert.match(get('#context').innerHTML,/Стена/);run("changeTool('select')");assert.equal(get('#context').hidden,true);});
check('HP draft applies once and validates without mutation',()=>{run('inspect(1)');get('#delta').value='-5';run('state.dirty=true');assert.equal(run('applyHp()'),true);assert.equal(run('token().hp'),19);assert.equal(run('applyHp()'),true);assert.equal(run('token().hp'),19);get('#hp').value='-9';assert.equal(run('applyHp()'),false);assert.equal(run('token().hp'),19);assert.match(get('#hpError').textContent,/Нужны целые/);get('#hp').value='19';});
check('Delta buttons are not form submits',()=>{assert.match(run("b('−5','delta',-5)"),/type="button"/);run("act('delta','-5')");assert.equal(get('#delta').value,-5);assert.equal(run('token().hp'),19);});
check('Dirty navigation offers stay/discard and preserves draft',()=>{run("openPanel('scenes')");assert.equal(get('#modal').hidden,false);assert.equal(run('state.panel'),'token');run("act('modalClose')");assert.equal(run('state.dirty'),true);assert.equal(get('#delta').value,-5);run("openPanel('scenes');act('dirtyDiscard')");assert.equal(run('state.panel'),'scenes');assert.equal(run('state.dirty'),false);});
check('One panel at a time and local sheet tabs',()=>{run("openPanel('tokens');openPanel('library')");assert.equal(run('state.panel'),'library');assert.doesNotMatch(get('#panel').innerHTML,/Гоблин 1/);run("openPanel('sheet');act('sheetTab','Заметки')");assert.equal(run('state.panel'),'sheet');assert.match(get('#panel').innerHTML,/Локальные заметки/);});
check('Map drawing does not change HP; undo restores marks',()=>{run("changeTool('fog');mapAction({clientX:300,clientY:250})");assert.equal(run('state.marks.length'),1);assert.equal(run('state.tokens[0].hp'),19);run("act('undo')");assert.equal(run('state.marks.length'),0);});
check('Preview blocks map actions',()=>{run("act('preview');mapAction({clientX:100,clientY:100})");assert.equal(run('state.preview'),true);assert.equal(run('state.marks.length'),0);run("act('preview')");});
check('Scene switch exits tool, selection and panel',()=>{run("inspect(1);changeTool('geometry');act('scene','1')");assert.equal(run('state.scene'),1);assert.equal(run('state.tool'),'select');assert.equal(run('state.selected'),null);assert.equal(run('state.panel'),null);});
check('Roll creates result in correct range and journal',()=>{const n=run('state.logs.length');run("act('roll','d20');openPanel('journal')");assert.equal(run('state.logs.length'),n+1);assert.match(get('#panel').innerHTML,/Мастер · d20 =/);const result=Number(run('state.logs.at(-1)').split(' = ')[1]);assert.ok(result>=1&&result<=20);});
check('Movement request resolves once, no token movement',()=>{const before=run('JSON.stringify(state.tokens)');run("openPanel('requests');act('resolve','1:yes')");assert.equal(run('state.requests.length'),1);assert.equal(run('JSON.stringify(state.tokens)'),before);run("act('modalClose')");});
check('Esc closes modal, then tool, then panel, one level each',()=>{run("inspect(1);changeTool('geometry');showModal('<h2>Пример</h2>')");const e={key:'Escape',preventDefault(){}};handlers.keydown(e);assert.equal(get('#modal').hidden,true);assert.equal(run('state.tool'),'geometry');handlers.keydown(e);assert.equal(run('state.tool'),'select');assert.equal(run('state.panel'),'token');handlers.keydown(e);assert.equal(run('state.panel'),null);});
check('Dirty dock toggle protects HP form',()=>{run("inspect(1);state.dirty=true;act('diceDock')");assert.equal(get('#modal').hidden,false);assert.equal(run('state.dice'),true);run("act('dirtyDiscard')");assert.equal(run('state.dice'),false);});
check('Input focus consumes first Esc without discarding draft',()=>{reset();run('inspect(1)');get('#hp').value='18';run('state.dirty=true');doc.activeElement=get('#hp');handlers.keydown({key:'Escape',preventDefault(){}});assert.equal(run('state.panel'),'token');assert.equal(run('state.dirty'),true);assert.equal(get('#hp').value,'18');});
check('Document click delegates +5 without submitting',()=>{reset();run('inspect(1)');const el={dataset:{action:'delta',value:'5'},type:'button',closest:s=>s==='#hpForm'?get('#hpForm'):null};handlers.click({target:{closest:()=>el},preventDefault(){},stopPropagation(){}});assert.equal(get('#delta').value,5);assert.equal(run('token().hp'),24);});
const html=fs.readFileSync(__dirname+'/index.html','utf8'),css=fs.readFileSync(__dirname+'/prototype.css','utf8'),js=fs.readFileSync(__dirname+'/prototype.js','utf8');
check('Local-only assets, viewport and narrow-screen input shield',()=>{assert.match(html,/name="viewport"/);assert.match(css,/@media\(max-width:850px\)/);assert.match(css,/\.mapwrap\.panelopen #map\{pointer-events:none\}/);assert.doesNotMatch(js,/\bfetch\s*\(|WebSocket|XMLHttpRequest|localStorage/);});
console.log(JSON.stringify({result:'PASS',count:checks.length,checks,limitations:['DOM-adapter logic checks only; not a browser render, real click or CSS layout test.']},null,2));
