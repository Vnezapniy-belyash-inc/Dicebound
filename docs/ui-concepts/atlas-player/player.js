'use strict';
const $=s=>document.querySelector(s),escapeText=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const btn=(s,a,v='',active=false)=>`<button type="button" data-action="${a}" data-value="${escapeText(v)}" class="${active?'active':''}">${s}</button>`;
const initial=()=>({tool:'select',shape:'circle',effect:'paint',eraser:false,menu:true,dice:false,panel:null,selected:null,collapsed:false,tab:'Характеристики',note:'Заметки игрока — только в памяти прототипа.',request:null,marks:[],ruler:null,logs:['Анна · d20 = 18','Мастер · d20 = 15'],tokens:[{id:1,name:'Эллиан',own:true,x:32,y:40,hp:'24 / 32',ac:'16',conditions:'Нет публичных состояний'},{id:2,name:'Мира',own:false,x:44,y:65,hp:'20 / 26',ac:null,conditions:'Нет публичных состояний'},{id:3,name:'Гоблин 1',own:false,x:62,y:43,hp:null,ac:null,conditions:'Испуган'}]});
let state=initial(),gesture=null,lastFocus=null,suppressClick=false;
const labels={select:'Выбор / перемещение',ruler:'Линейка',area:'Области',paint:'Отметки / ластик'};
const chosen=()=>state.tokens.find(t=>t.id===state.selected);
function say(s){$('#status').textContent=s;}
function render(){
 $('#playerMenu').classList.toggle('closed',!state.menu);$('#menuToggle').setAttribute('aria-expanded',state.menu);
 $('.mapwrap').classList.toggle('menuopen',state.menu);
 document.querySelectorAll('[data-tool]').forEach(el=>{const active=el.dataset.tool===state.tool;el.classList.toggle('active',active);el.setAttribute('aria-pressed',active);});
 document.querySelectorAll('nav [data-panel]').forEach(el=>el.classList.toggle('active',el.dataset.panel===state.panel));
 $('#rulerOptions').hidden=state.tool!=='ruler';$('#areaOptions').hidden=state.tool!=='area';$('#paintOptions').hidden=state.tool!=='paint';$('#diceOptions').hidden=!state.dice;
 $('#rulerOptions').innerHTML='<p>Протяните от начальной клетки до цели. Только измерение, токены не двигаются.</p>'+btn('Убрать измерение','clearRuler')+btn('Завершить · Esc','finish');
 $('#areaOptions').innerHTML='<p>Форма области</p><div class="row">'+btn('Круг','shape','circle',state.shape==='circle')+btn('Квадрат','shape','square',state.shape==='square')+btn('Конус','shape','cone',state.shape==='cone')+'</div><p>Клик по карте — пример размещения. Эффект и размер в игре задаются существующим инструментом.</p>'+btn('Убрать демообласти','clearAreas')+btn('Завершить · Esc','finish');
 $('#paintOptions').innerHTML='<p>Отметки на клетках</p><div class="row">'+btn('Огонь','effect','paint',!state.eraser&&state.effect==='paint')+btn('Вода','effect','water',!state.eraser&&state.effect==='water')+btn('Природа','effect','nature',!state.eraser&&state.effect==='nature')+btn('Ластик','eraser','',state.eraser)+'</div>'+btn('Очистить мои…','clearOwn')+btn('Завершить · Esc','finish');
 $('#diceOptions').innerHTML='<div class="row">'+['d4','d6','d8','d10','d12','d20','d100'].map(d=>btn(d,'roll',d)).join('')+'</div><p>Демо: случайный результат сразу попадает в журнал. В игре — физические кубики.</p>'+btn('Убрать мои кубики…','clearDice');
 const chips=state.tokens.map((t,i)=>`<button type="button" class="turnchip ${i===0?'current':''} ${i===1?'next':''}" data-action="inspect" data-value="${t.id}">${[18,12,8][i]} ${escapeText(t.name)}</button>`).join('');
 $('#initiative').innerHTML=state.collapsed?'<span>Ход: Эллиан · раунд 3</span>'+btn('Очередь ▾','initiative'):'<strong class="round">Раунд 3</strong><div class="order">'+chips+'</div>'+btn('Свернуть ▴','initiative');
 $('#activeLabel').textContent=labels[state.tool];renderMap();renderPanel();
}
function renderMap(){
 $('#tokens').innerHTML=state.tokens.map(t=>`<button type="button" class="token ${t.own?'own':'other'} ${t.id===state.selected?'selected':''}" data-token="${t.id}" style="left:${t.x}%;top:${t.y}%" aria-label="${escapeText(t.name)}${t.own?', ваш токен':', чужой токен'}"><span class="portrait">${escapeText(t.name[0])}</span><span class="name">${escapeText(t.name)}${t.own?' · ваш':''}</span></button>`).join('');
 const t=chosen();$('#info').hidden=!t||state.tool!=='select';if(t){$('#info').style.left=`min(${t.x+4}%,calc(100% - 165px))`;$('#info').style.top=`max(8px,calc(${t.y}% - 62px))`;}
 $('#marks').innerHTML=state.marks.map(m=>`<span class="mark ${escapeText(m.type)}" style="left:${m.x}%;top:${m.y}%">${m.type==='area cone'?'':''}</span>`).join('');
 if(state.ruler){const r=$('#map').getBoundingClientRect(),m=state.ruler,dx=(m.x2-m.x)*r.width/100,dy=(m.y2-m.y)*r.height/100;$('#marks').innerHTML+=`<span class="rulerline" style="left:${m.x}%;top:${m.y}%;width:${Math.hypot(dx,dy)}px;rotate:${Math.atan2(dy,dx)}rad"></span><span class="distance" style="left:${Math.min(75,m.x2)}%;top:${Math.min(85,m.y2)}%">≈ ${Math.round(Math.hypot(dx,dy)/40*5)} фт · демомасштаб</span>`;}
 $('#map').classList.toggle('paint',state.tool!=='select');$('.mapwrap').classList.toggle('panelopen',!!state.panel);$('#waiting').hidden=!state.request;
}
function panelHeader(s){return `<div class="panelhead"><h2>${escapeText(s)}</h2><button type="button" data-action="close" aria-label="Закрыть панель">×</button></div>`;}
function renderPanel(){
 const p=$('#panel');p.hidden=!state.panel;p.classList.toggle('wide',state.panel==='sheet');if(!state.panel){p.innerHTML='';return;}let h='';const t=chosen();
 if(state.panel==='token'&&t)h=panelHeader(t.name)+`<p class="readonly">${t.own?'Ваш токен · перемещение разрешено':'Чужой токен · только просмотр'}</p><p>ХП: ${t.hp?escapeText(t.hp):'не опубликованы мастером'}</p><p>КД: ${t.ac?escapeText(t.ac):'не опубликован мастером'}</p><p>Состояния: ${escapeText(t.conditions)}</p>`+(t.id===3?'<div class="item"><strong>Публичный статблок: Гоблин</strong><p>Маленький гуманоид · скорость 30 фт</p><p class="muted">Показаны только демонстрационные публичные поля. Закрытых ХП и КД в данных страницы нет.</p></div>':'')+(t.own?btn('Открыть мой лист','sheet'):'')+'<p class="muted">Редактирование боевых параметров токена доступно мастеру. Локальный лист персонажа — отдельные данные.</p>';
 if(state.panel==='sheet')h=panelHeader('Мой персонаж')+'<p>Эллиан · локальный лист Анны</p><div class="tabs">'+['Характеристики','Атаки','Способности','Снаряжение','Заметки'].map(s=>btn(s,'sheetTab',s,state.tab===s)).join('')+'</div>'+(state.tab==='Заметки'?`<label for="sheetNote">Личные заметки</label><textarea id="sheetNote">${escapeText(state.note)}</textarea><p class="muted">Ввод сохраняется сразу в памяти страницы, до её обновления.</p>`:`<p>${({'Характеристики':'СИЛ 12 · ЛОВ 16 · ТЕЛ 14 · ИНТ 10 · МДР 13 · ХАР 8','Атаки':'Длинный лук · пример текстового описания атаки.','Способности':'Второе дыхание · пример описания способности.','Снаряжение':'Лук, колчан, кожаный доспех.'})[state.tab]}</p>` )+'<p class="muted">Это прототип входа в существующий локальный лист. Импорт/экспорт и полный редактор будут подключаться отдельно. Автоматических атак здесь нет.</p>';
 if(state.panel==='journal')h=panelHeader('Журнал бросков')+state.logs.slice().reverse().map(s=>`<div class="logline">${escapeText(s)}</div>`).join('')+'<p class="muted">Деморезультаты. Сетевой журнал игры не подключён.</p>';
 if(state.panel==='reference')h=panelHeader('Справочник')+'<label for="referenceSearch">Поиск по доступным записям</label><input id="referenceSearch" class="filter" placeholder="Например, обитель"><div id="referenceResults">'+referenceHTML('')+'</div><p class="muted">Доступные игроку записи — только для чтения. Мастерских записей в этом примере нет.</p>';
 if(state.panel==='players')h=panelHeader('Участники')+'<p>Мастер · ведущий</p><p>Анна · вы</p><p>Олег · игрок</p><p class="muted">Управление чужими правами игроку не показывается.</p>';
 if(state.panel==='help')h=panelHeader('Управление')+'<p>ЛКМ по токену → «Информация». Перетаскивать можно только свой демонстрационный токен.</p><p>Линейка: протянуть ЛКМ от начала до цели. Области и отметки: кликнуть по карте.</p><p>Esc завершает активный инструмент, затем закрывает панель, затем снимает выделение. При вводе сначала снимает фокус поля. ПКМ при активном инструменте завершает его.</p><p class="muted">На узком окне меню закрывается после выбора инструмента. Его настройки остаются слева при повторном открытии меню.</p>';
 p.innerHTML=h;
}
function referenceHTML(q){const entries=[{title:'Старая обитель',body:'Руины монастыря у лесной дороги.'},{title:'Укрытие',body:'Пример опубликованной справочной заметки мастера.'}];return entries.filter(e=>(e.title+' '+e.body).toLowerCase().includes(q.toLowerCase())).map(e=>`<div class="item"><strong>${e.title}</strong><p>${e.body}</p></div>`).join('')||'<p>Ничего не найдено.</p>';}
function narrow(){return window.innerWidth<=850;}
function openPanel(name){state.panel=state.panel===name?null:name;if(narrow())state.menu=false;render();$('#panel button')?.focus();}
function inspect(id){state.selected=Number(id);state.panel='token';if(narrow())state.menu=false;render();$('#panel button')?.focus();}
function changeTool(name){if(!Object.hasOwn(labels,name))return;state.tool=name;if(narrow())state.menu=false;gesture=null;render();say(name==='select'?'Выберите токен. Перемещение разрешено только для вашего токена.':`${labels[name]} · ${name==='ruler'?'протяните по карте':'нажмите по карте'}. Параметры находятся слева.`);}
function modal(html){lastFocus=document.activeElement;$('#modalBody').innerHTML=html;$('#modal').hidden=false;$('#modalBody button')?.focus();}
function closeModal(){ $('#modal').hidden=true;if(lastFocus?.isConnected)lastFocus.focus();}
function act(a,v){switch(a){
 case 'reset':state=initial();gesture=null;closeModal();render();say('Пример игрока сброшен.');break;
 case 'menu':state.menu=!state.menu;render();break;
 case 'finish':changeTool('select');break;
 case 'shape':state.shape=v;render();break;
 case 'effect':state.effect=v;state.eraser=false;render();break;
 case 'eraser':state.eraser=true;render();break;
 case 'clearRuler':state.ruler=null;renderMap();break;
 case 'clearAreas':state.marks=state.marks.filter(m=>!m.type.startsWith('area'));renderMap();break;
 case 'clearOwn':modal('<h2>Очистить мои отметки?</h2><p>Удалятся только отметки этого демонстрационного игрока.</p>'+btn('Отмена','modalClose')+btn('Очистить','clearOwnConfirm'));break;
 case 'clearOwnConfirm':state.marks=state.marks.filter(m=>m.type.startsWith('area'));closeModal();renderMap();say('Ваши демоотметки очищены.');break;
 case 'dice':state.dice=!state.dice;render();break;
 case 'roll':{const n=1+Math.floor(Math.random()*Number(v.slice(1)));state.logs.push(`Анна · ${v} = ${n}`);if(state.panel==='journal')renderPanel();say(`Демо: ${v} = ${n}. Результат добавлен в журнал.`);break;}
 case 'clearDice':modal('<h2>Убрать мои кубики?</h2><p>Пример подтверждения. Физические кубики в прототипе не создаются, журнал сохранится.</p>'+btn('Отмена','modalClose')+btn('Убрать (демо)','clearDiceConfirm'));break;
 case 'clearDiceConfirm':closeModal();say('Просмотрено подтверждение удаления своих кубиков. Журнал сохранён.');break;
 case 'initiative':state.collapsed=!state.collapsed;render();break;
 case 'inspect':inspect(v);break;
 case 'info':if(chosen())inspect(state.selected);break;
 case 'sheet':openPanel('sheet');break;
 case 'sheetTab':state.tab=v;renderPanel();break;
 case 'close':state.panel=null;render();$('#map').focus();break;
 case 'requestDemo':modal('<h2>Проверить перемещение</h2><p>Демонстрационный рискованный путь. До решения мастера токен остаётся на месте, карта не раскрывается.</p><p class="muted">Это контрольный сценарий прототипа, не отдельная команда меню игры.</p>'+btn('Отмена','modalClose')+btn('Подтвердить перемещение','sendRequest'));break;
 case 'sendRequest':state.request={token:1};closeModal();renderMap();say('Демо: ожидается решение мастера. Игрок не может разрешить запрос самому себе.');break;
 case 'cancelRequest':state.request=null;renderMap();say('Демонстрационный запрос отменён.');break;
 case 'modalClose':closeModal();break;
 }}
function point(e){const r=$('#map').getBoundingClientRect();return{x:Math.max(3,Math.min(95,(e.clientX-r.left)/r.width*100)),y:Math.max(10,Math.min(90,(e.clientY-r.top)/r.height*100))};}
function mapClick(e){if(!$('#modal').hidden)return;const p=point(e);if(state.tool==='select'){state.selected=null;state.panel=null;render();return;}if(state.tool==='area'){state.marks.push({...p,type:'area '+(state.shape==='circle'?'':state.shape)});renderMap();say('Пример области: без автоматического урона и расчёта целей.');}if(state.tool==='paint'){if(state.eraser)state.marks=state.marks.filter(m=>m.type.startsWith('area')||Math.hypot(m.x-p.x,m.y-p.y)>8);else state.marks.push({...p,type:state.effect});renderMap();}}
document.addEventListener('click',e=>{const el=e.target.closest('button');if(!el)return;e.preventDefault();if(!$('#modal').hidden&&!el.closest('#modal'))return;if(el.dataset.tool){changeTool(el.dataset.tool);return;}if(el.dataset.panel){openPanel(el.dataset.panel);return;}if(el.dataset.action){act(el.dataset.action,el.dataset.value);return;}if(el.dataset.token){if(suppressClick){suppressClick=false;return;}if(state.tool!=='select'){mapClick(e);return;}state.selected=Number(el.dataset.token);state.panel=null;render();say(chosen().own?'Ваш токен выбран. «Информация» — публичные данные; перетащите для перемещения.':'Чужой токен: доступна только публичная информация.');}});
document.addEventListener('input',e=>{if(e.target.id==='sheetNote')state.note=e.target.value;if(e.target.id==='referenceSearch')$('#referenceResults').innerHTML=referenceHTML(e.target.value);});
$('#map').addEventListener('click',e=>{if(e.target.closest('button'))return;if(suppressClick){suppressClick=false;return;}mapClick(e);});
$('#map').addEventListener('contextmenu',e=>{e.preventDefault();if(state.tool!=='select'){changeTool('select');return;}const t=e.target.closest('[data-token]');if(t)inspect(t.dataset.token);});
$('#map').addEventListener('pointerdown',e=>{if(e.button!==0||!$('#modal').hidden)return;const p=point(e);if(state.tool==='ruler'){gesture={kind:'ruler',start:p};state.ruler={...p,x2:p.x,y2:p.y};return;}if(state.tool!=='select')return;const el=e.target.closest('[data-token]');if(!el)return;const t=state.tokens.find(t=>t.id===Number(el.dataset.token));if(!t?.own){say('Чужой токен: перемещение недоступно.');return;}if(state.request){say('Дождитесь решения мастера или отмените запрос.');return;}gesture={kind:'token',id:t.id,start:p,sx:e.clientX,sy:e.clientY,moved:false};});
document.addEventListener('pointermove',e=>{if(!gesture)return;const p=point(e);if(gesture.kind==='ruler'){state.ruler={...gesture.start,x2:p.x,y2:p.y};renderMap();return;}if(Math.hypot(e.clientX-gesture.sx,e.clientY-gesture.sy)<7)return;gesture.moved=true;const t=state.tokens.find(t=>t.id===gesture.id);t.x=p.x;t.y=p.y;const el=document.querySelector(`[data-token="${t.id}"]`);if(el){el.style.left=t.x+'%';el.style.top=t.y+'%';}});
document.addEventListener('pointerup',()=>{if(gesture){suppressClick=gesture.kind==='ruler'||gesture.moved;if(gesture.kind==='token'&&gesture.moved){state.selected=gesture.id;state.panel=null;renderMap();renderPanel();say('Ваш токен перемещён только в демосхеме. Проверка маршрута игры не подключена.');}gesture=null;}});
document.addEventListener('pointercancel',()=>{gesture=null;});
document.addEventListener('keydown',e=>{if(e.key==='Tab'&&!$('#modal').hidden){const els=[...$('#modal').querySelectorAll('button,input')];const first=els[0],last=els.at(-1);if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}return;}if(e.key!=='Escape')return;e.preventDefault();if(!$('#modal').hidden){closeModal();return;}if(document.activeElement.matches('input,textarea,select')){document.activeElement.blur();return;}if(state.tool!=='select'){changeTool('select');return;}if(state.panel){act('close');return;}if(narrow()&&state.menu){state.menu=false;render();return;}if(state.selected){state.selected=null;renderMap();}});
render();
