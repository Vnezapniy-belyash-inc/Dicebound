const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
let observer;
class Element{
 constructor(tag){this.tag=tag;this.attributes={};this.childNodes=[];this.dataset={};}
 setAttribute(k,v){this.attributes[k]=v;}
 append(el){this.childNodes.push(el);}
 prepend(el){this.childNodes.unshift(el);}
 querySelector(){return this.childNodes.find(n=>n.tag==='svg'&&n.attributes.class==='ui-icon')||null;}
}
function button(dataset,text){const el=new Element('button');el.dataset={...dataset};el.append({nodeType:3,textContent:text});return el;}
const buttons=[button({tool:'ruler'},'↔ Линейка'),button({panel:'scenes'},'Старая обитель ▾'),button({action:'menu'},'☰ Меню игрока'),button({action:'roll'},'d20')];
const doc={body:{},querySelectorAll:()=>buttons,createElementNS:(_,tag)=>new Element(tag)};
vm.runInNewContext(fs.readFileSync(__dirname+'/atlas-icons.js','utf8'),{document:doc,MutationObserver:class{constructor(fn){observer=fn;}observe(){}}});
assert.equal(buttons[0].childNodes[1].textContent,'Линейка');assert.equal(buttons[0].querySelector().attributes['aria-hidden'],'true');assert.equal(buttons[0].dataset.tool,'ruler');assert.equal(buttons[3].querySelector(),null);
observer();assert.equal(buttons[0].childNodes.filter(n=>n.tag==='svg').length,1);
buttons[1].childNodes=[{nodeType:3,textContent:'Лесная дорога ▾'}];observer();assert.ok(buttons[1].querySelector());assert.equal(buttons[1].childNodes[1].textContent,'Лесная дорога ▾');
for(const [role,script] of [['master','prototype.js'],['player','player.js']]){
 const original=path.join(__dirname,role==='master'?'../atlas-stage2':'../atlas-player',script);
 const actualCode=fs.readFileSync(path.join(__dirname,role,script),'utf8'),originalCode=fs.readFileSync(original,'utf8');
 // Master is unchanged. Player now demonstrates source-based area effect application.
 if(role==='master')assert.equal(actualCode,originalCode);
 else {assert.match(actualCode,/case 'areaEffect'/);assert.match(actualCode,/effects\.find/);assert.match(actualCode,/function effectPalette/);}
 const html=fs.readFileSync(path.join(__dirname,role,'index.html'),'utf8');
 assert.match(html,/\.\.\/atlas.css/);assert.match(html,/\.\.\/atlas-icons.js/);
}
const css=fs.readFileSync(__dirname+'/atlas.css','utf8');assert.match(css,/prefers-reduced-motion/);assert.match(css,/:focus-visible/);assert.match(css,/@media\(max-width:850px\)/);
console.log(JSON.stringify({result:'PASS',checks:['Decorative SVG preserves text and actions','Icons excluded from accessibility names','No duplicate icon on repeated mutation','Icon restored after dynamic text update','Master unchanged; player has explicit area effect palette/application','Both pages load local shared style and icons','Focus, reduced motion and narrow breakpoint styles present'],limitations:'Static and DOM-adapter checks; no browser rendering.'},null,2));
