'use strict';
// Standalone demo. No game imports, storage, network calls or persistence.
const $ = s => document.querySelector(s);
const esc = s => String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const initial = () => ({combat:true,collapsed:false,round:3,turn:0,tool:'select',sub:'reveal',panel:null,tab:'parameters',selected:null,dirty:false,preview:false,fog:true,paused:false,dice:true,requests:[{id:1,name:'Мира',cell:'8, 5'},{id:2,name:'Эллиан',cell:'10, 7'}],scene:0,scenes:['Старая обитель','Лесная дорога','Подземелье'],tokens:[{id:1,name:'Эллиан',hp:24,max:32,ac:16,x:32,y:37,owner:'Анна',enemy:false},{id:2,name:'Гоблин 1',hp:9,max:12,ac:13,x:62,y:58,owner:'Мастер',enemy:true},{id:3,name:'Мира',hp:20,max:26,ac:15,x:25,y:70,owner:'Олег',enemy:false}],logs:['Анна · d20 = 18','Мастер · d20 = 15'],marks:[],history:[],libraryTab:'blocks'});
let state=initial(), pending=null, lastFocus=null, drag=null, skipClick=false;
const toolNames={select:'Выбор',token:'+ Токен',ruler:'Линейка',area:'Области',paint:'Отметки',geometry:'Геометрия',fog:'Туман',map:'Карта',transitions:'Переходы'};
function b(text,action,value='',active=false){return `<button type="button" data-action="${action}" data-value="${esc(value)}" class="${active?'active':''}">${text}</button>`;}
function notice(s){$('#status').textContent=s;}
function token(){return state.tokens.find(t=>t.id===state.selected);}
function remember(){state.history.push(JSON.stringify(state.marks));}
function render(){
 $('#sceneTitle').textContent=state.scenes[state.scene]+' ▾';$('.maptitle').textContent=state.scenes[state.scene]+' · демонстрационная карта';
 $('#combatToggle').textContent=state.combat?'Бой включён':'Вне боя';$('#combatToggle').setAttribute('aria-pressed',state.combat);
 const order=state.tokens.map((t,i)=>`<button class="turnchip ${i===state.turn?'current':''} ${i===(state.turn+1)%state.tokens.length?'next':''}" data-action="inspect" data-value="${t.id}">${[18,15,12][i]||8} ${esc(t.name)}</button>`).join('');
 $('#initiative').innerHTML=state.combat&&!state.collapsed?`<strong class="round">Раунд ${state.round}</strong><div class="order">${order}</div>${b('Следующий ход →','next')}${b('Список','initiative')}${b('▴','collapse')}`:`<span>${state.combat?'Ход: '+esc(state.tokens[state.turn].name):'Инициатива · вне боя'}</span>${state.combat?b('Раскрыть','expand'):b('Включить бой','combat')}`;
 $('#tools').innerHTML=Object.entries(toolNames).map(([k,v])=>`<button data-tool="${k}" class="${state.tool===k?'active':''}" aria-pressed="${state.tool===k}">${v}</button>`).join('');
 renderContext();renderMap();renderPanel();
 $('#diceDock').hidden=!state.dice;$('#diceDock').innerHTML=['d4','d6','d8','d10','d12','d20','d100'].map(d=>b(d,'roll',d)).join('');
 $('#requests').textContent=`Перемещения · ${state.requests.length}`;
 document.querySelectorAll('nav [data-panel]').forEach(el=>el.classList.toggle('active',el.dataset.panel===state.panel));
}
function renderContext(){
 const c=$('#context');c.hidden=state.tool==='select';
 let html='';
 const sub=(label,v)=>b(label,'sub',v,state.sub===v);
 if(state.tool==='fog') html=b(state.fog?'Туман включён':'Туман выключен','fogEnabled')+sub('Раскрыть','reveal')+sub('Закрыть','close')+b(state.paused?'Продолжить раскрытие':'Пауза раскрытия','pause')+b(state.preview?'Вид мастера':'Глазами игроков','preview')+b('Отменить','undo')+'<span class="hint">Демо кисти: нажимайте по свободной карте. Круги обозначают локальные правки, это не расчёт видимости игры.</span>';
 if(state.tool==='geometry') html=['Стена','Дверь','Открыть / закрыть','Препятствие','Колонна','Стереть'].map((s,i)=>sub(s,['wall','door','toggleDoor','obstacle','column','erase'][i])).join('')+b('Отменить','undo')+'<span class="hint">Один клик — демонстрационная отметка инструмента; игровая геометрия не меняется.</span>';
 if(state.tool==='area') html=['Круг','Квадрат','Конус'].map((s,i)=>sub(s,['circle','square','cone'][i])).join('')+'<span class="hint">Клик показывает пример области. Расчёт клеток и эффектов — в игре.</span>';
 if(state.tool==='paint') html=sub('Отметка','mark')+sub('Ластик','erase')+b('Очистить демоотметки','clearMarks');
 if(state.tool==='ruler') html='<span>Линейка: нажмите на карту для демонстрационной метки расстояния.</span>';
 if(state.tool==='map') html=b('Загрузить изображение…','demo','Файловый выбор изображения будет подключаться на этапе функций.')+b('Каталог карт…','demo','Здесь будет существующий каталог карт сессии.')+'<label>Масштаб <input id="mapScale" type="number" value="1" min="0.1" step="0.1" style="width:65px"></label>'+b('Повернуть 90°','rotateMap')+b('Сбросить','resetMap');
 if(state.tool==='transitions') html='<span>Переход «В подземелье»</span>'+b('Перенести выбранный токен…','transfer')+'<span class="hint">Демонстрируется подтверждение. Данные игры не переносятся.</span>';
 c.innerHTML=html+b('Завершить · Esc','finish');
}
function renderMap(){
 $('#fog').hidden=!state.fog;$('#previewBanner').hidden=!state.preview;$('#map').classList.toggle('paint',state.tool!=='select');
 $('#tokens').innerHTML=state.tokens.map(t=>`<button class="token ${t.enemy?'enemy':''} ${t.id===state.selected?'selected':''}" data-token="${t.id}" style="left:${t.x}%;top:${t.y}%" aria-label="Токен ${esc(t.name)}"><span class="portrait">${esc(t.name.charAt(0))}</span><span class="name">${esc(t.name)}</span></button>`).join('');
 const t=token(),info=$('#info');info.hidden=!t||state.tool!=='select'||state.preview;if(t){info.style.left=`min(${t.x+4}%,calc(100% - 160px))`;info.style.top=`max(8px,calc(${t.y}% - 65px))`;}
 $('#marks').innerHTML=state.marks.map(m=>`<span class="mark ${esc(m.kind)}" style="left:${m.x}%;top:${m.y}%">${esc(m.label||'')}</span>`).join('');
 $('.mapwrap').classList.toggle('panelopen',!!state.panel);
}
function header(title){return `<div class="panelhead"><h2>${esc(title)}</h2><button data-action="close" aria-label="Закрыть панель">×</button></div>`;}
function renderPanel(){
 const p=$('#panel');p.hidden=!state.panel;p.classList.toggle('wide',['library','sheet'].includes(state.panel));if(!state.panel){p.innerHTML='';return;}
 let h='';const t=token();
 if(state.panel==='token'&&t){
  h=header(t.name)+`<div class="tabs">${b('Параметры','tab','parameters',state.tab==='parameters')}${b('Доступ','tab','access',state.tab==='access')}${b('Статблок','tab','block',state.tab==='block')}</div>`;
  if(state.tab==='parameters')h+=`<form id="hpForm"><div class="field"><label for="hp">ХП / максимум</label><input id="hp" type="number" value="${t.hp}" aria-label="Текущее ХП"><input id="maxhp" type="number" value="${t.max}" aria-label="Максимум ХП"></div><div class="field"><label for="ac">КД</label><input id="ac" type="number" value="${t.ac}"></div><div class="field"><label for="delta">Изменение ХП</label>${b('−5','delta',-5)}<input id="delta" type="number" value="0" aria-label="Изменение ХП">${b('+5','delta',5)}</div><p class="muted">Дельта добавится к ХП при применении. Кнопки ±5 заполняют дельту, не применяют её.</p><button class="primary" type="submit">Применить</button><p class="error" id="hpError" role="alert"></p></form><div class="row">${b('Состояния…','conditions')}${b('В инициативу','addInitiative')}</div>`;
  if(state.tab==='access')h+=`<p>Владелец: ${esc(t.owner)}</p><p>Тип: ${t.enemy?'NPC':'Герой'}</p><p class="muted">Предпросмотр существующих настроек: владелец, общее перемещение, скрытие, обзор, публичность ХП/КД. Реальные права в этом этапе не подключены.</p>${b('Скрыть / показать (демо)','hideToken')}`;
  if(state.tab==='block')h+=`<p>${t.enemy?'Гоблин · КД 13 · ХП 12':'Статблок не назначен'}</p><p class="muted">Назначение статблока и публичные поля будут подключаться отдельным этапом.</p>${b('Открыть библиотеку','openLibrary')}`;
  h+='<p class="muted small">Демонстрационный токен. Сохранение только в памяти этой страницы.</p>';
 }
 if(state.panel==='scenes')h=header('Сцены')+state.scenes.map((s,i)=>`<div class="item">${b((i===state.scene?'● ':'')+esc(s),'scene',i,i===state.scene)}</div>`).join('')+'<p class="muted">Переключение демонстрирует сброс инструмента и закрытие карточки. В демо используется одна схема карты; файлы сцен и синхронизация не загружаются.</p>';
 if(state.panel==='tokens')h=header('Токены')+state.tokens.map(t=>`<div class="item"><strong>${esc(t.name)}</strong><p>ХП ${t.hp}/${t.max} · ${esc(t.owner)}</p>${b('Информация','inspect',t.id)}</div>`).join('')+b('+ Создать демотокен','createToken');
 if(state.panel==='createToken')h=header('Создать токен')+'<form id="createForm"><div class="field"><label for="tokenName">Имя</label><input class="long" id="tokenName" maxlength="64" required value="Гоблин 2"></div><button type="submit" class="primary">Создать демотокен</button></form><p class="muted">Добавится только в текущую схему прототипа.</p>';
 if(state.panel==='library')h=header('Библиотека')+`<div class="tabs">${b('Статблоки','libraryTab','blocks',state.libraryTab==='blocks')}${b('Справочник','libraryTab','reference',state.libraryTab==='reference')}</div>`+(state.libraryTab==='blocks'?'<div class="item"><strong>Гоблин</strong><p>КД 13 · ХП 12 · Скорость 30 фт</p><p>Действия и публичные поля — существующие данные статблока.</p></div>':'<div class="item"><strong>Обитель</strong><p>Категория: место · теги: руины</p><p>Демонстрационная запись справочника.</p></div>')+'<p class="muted">Прототип проверяет вход и переключение вкладок. Редактор, импорт и публикация записей пока не подключены.</p>';
 if(state.panel==='sheet')h=header('Мой лист')+'<p><strong>Локальный лист персонажа</strong></p><div class="tabs">'+['Характеристики','Атаки','Способности','Снаряжение','Заметки'].map(s=>b(s,'sheetTab',s,state.sheetTab===s||(!state.sheetTab&&s==='Характеристики'))).join('')+'</div><p id="sheetContent">'+esc(sheetCopy(state.sheetTab||'Характеристики'))+'</p><p class="muted">Отдельный локальный лист, не лист выбранного токена. Вход предложен в этапе 1; импорт/экспорт LSS здесь не подключены.</p>';
 if(state.panel==='players')h=header('Участники')+'<p>Мастер · вы</p><p>Анна · Эллиан</p><p>Олег · Мира</p><p class="muted">Код: DEMO · подключения к сети нет.</p>';
 if(state.panel==='journal')h=header('Журнал бросков')+state.logs.map(s=>`<div class="logline">${esc(s)}</div>`).reverse().join('')+'<p class="muted">В прототипе результат генерируется случайно при нажатии dN; физические кубики игры не подключены.</p>';
 p.innerHTML=h;
}
function sheetCopy(s){return ({'Характеристики':'СИЛ 12 · ЛОВ 16 · ТЕЛ 14 · ИНТ 10 · МДР 13 · ХАР 8','Атаки':'Пример текста атак. Без автоматических бросков и урона.','Способности':'Пример описания особенностей персонажа.','Снаряжение':'Пример списка снаряжения.','Заметки':'Локальные заметки — демонстрационный текст.'})[s];}
function guarded(fn){if(!state.dirty){fn();return;}showModal('<h2>Есть неприменённые изменения</h2><p>Применить ХП и КД перед переходом?</p>'+b('Применить и продолжить','dirtyApply')+b('Отбросить','dirtyDiscard')+b('Остаться','modalClose'),fn);}
function showModal(html,fn=null){lastFocus=document.activeElement;pending=fn;$('#modalBody').innerHTML=html;$('#modal').hidden=false;$('#modalBody button')?.focus();}
function closeModal(){const f=lastFocus;$('#modal').hidden=true;pending=null;if(f?.isConnected)f.focus();}
function openPanel(name){guarded(()=>{state.dirty=false;state.panel=state.panel===name?null:name;if(name==='requests'){state.panel=null;showRequests();return;}render();$('#panel button')?.focus();});}
function inspect(id){guarded(()=>{state.selected=Number(id);state.panel='token';state.tab='parameters';state.dirty=false;render();$('#panel button')?.focus();});}
function applyHp(){
 const t=token();if(!t||!$('#hp'))return false;
 const raw=['hp','maxhp','ac','delta'].map(id=>$('#'+id).value.trim());const [hp,max,ac,delta]=raw.map(Number);
 if(raw.some(s=>s==='')||![hp,max,ac,delta].every(Number.isSafeInteger)||max<=0||hp<0||ac<0||hp+delta<0||hp+delta>max){$('#hpError').textContent='Нужны целые числа: максимум > 0, ХП с дельтой от 0 до максимума, КД ≥ 0.';return false;}
 t.hp=hp+delta;t.max=max;t.ac=ac;state.dirty=false;notice(`Демо: ${t.name}, ХП ${t.hp}/${max}, КД ${ac}.`);render();return true;
}
function showRequests(){showModal('<h2>Запросы перемещения · демо</h2><p class="muted">Решение действует один раз. Сроки запросов и реальные пути в прототипе не рассчитываются.</p>'+state.requests.map(r=>`<div class="item"><p>${esc(r.name)} → клетка ${esc(r.cell)}</p>${b('Разрешить','resolve',r.id+':yes')}${b('Отклонить','resolve',r.id+':no')}</div>`).join('')+(state.requests.length?'':'<p>Нет ожидающих запросов.</p>')+b('Закрыть · Esc','modalClose'));}
function changeTool(name){guarded(()=>{state.tool=name;state.preview=false;state.dirty=false;state.sub=({fog:'reveal',geometry:'wall',area:'circle',paint:'mark'})[name]||'';if(name==='token'){state.tool='select';state.panel='createToken';}render();notice(name==='select'?'Выбор: нажмите токен, затем «Информация».':`Инструмент: ${toolNames[name]}. Esc — завершить.`);});}
function act(a,v){
 switch(a){
 case 'reset':guarded(()=>{state=initial();$('#plan').style.transform='';$('#plan').style.scale='';render();notice('Демонстрация сброшена.');});break;
 case 'combat':guarded(()=>{state.combat=!state.combat;state.collapsed=!state.combat;render();notice(state.combat?'Демо: бой включён, инициатива раскрыта.':'Вне боя: инициатива свёрнута. Инструменты доступны.');});break;
 case 'next':guarded(()=>{state.turn=(state.turn+1)%state.tokens.length;if(!state.turn)state.round++;render();notice('Следующий ход · демо.');});break;
 case 'collapse':guarded(()=>{state.collapsed=true;render();});break;
 case 'expand':guarded(()=>{state.collapsed=false;if(!state.combat){notice('Вне боя: включите «Бой» для очереди ходов.');return;}render();});break;
 case 'initiative':showModal('<h2>Инициатива · демонстрация</h2>'+state.tokens.map((t,i)=>`<p>${[18,15,12][i]||8} · ${esc(t.name)} · ХП ${t.hp}/${t.max}</p>`).join('')+'<p class="muted">Полное редактирование и назначение d20 будут подключаться отдельным блоком.</p>'+b('Закрыть','modalClose'));break;
 case 'home':guarded(()=>{state.panel=null;state.selected=null;state.dirty=false;changeTool('select');});break;
 case 'info':if(token())inspect(state.selected);break;
 case 'inspect':inspect(v);break;
 case 'close':guarded(()=>{state.panel=null;state.dirty=false;render();$('#map').focus();});break;
 case 'tab':guarded(()=>{state.tab=v;state.dirty=false;renderPanel();});break;
 case 'delta':$('#delta').value=Number($('#delta').value||0)+Number(v);state.dirty=true;break;
 case 'finish':changeTool('select');break;
 case 'sub':state.sub=v;renderContext();notice('Выбран инструмент: '+v+' · демонстрация.');break;
 case 'fogEnabled':guarded(()=>{state.fog=!state.fog;render();});break;
 case 'pause':state.paused=!state.paused;renderContext();notice('Пауза автоматического раскрытия: '+(state.paused?'включена':'выключена')+'. В прототипе автоматическое раскрытие не рассчитывается.');break;
 case 'preview':guarded(()=>{state.preview=!state.preview;render();notice('Предпросмотр — демонстрация блокировки редактирования; реальная видимость не рассчитывается.');});break;
 case 'undo':if(state.history.length){state.marks=JSON.parse(state.history.pop());renderMap();notice('Последняя деморазметка отменена.');}else notice('Нет деморазметки для отмены.');break;
 case 'clearMarks':remember();state.marks=[];renderMap();break;
 case 'scene':guarded(()=>{state.scene=Number(v);state.tool='select';state.preview=false;state.selected=null;state.panel=null;state.dirty=false;state.marks=[];state.history=[];render();notice('Выбрана демосцена. Игровая загрузка и синхронизация не выполняются.');});break;
 case 'createToken':openPanel('createToken');break;
 case 'libraryTab':state.libraryTab=v;renderPanel();break;
 case 'openLibrary':openPanel('library');break;
 case 'sheetTab':state.sheetTab=v;renderPanel();break;
 case 'diceDock':guarded(()=>{state.dice=!state.dice;render();});break;
 case 'roll':{const sides=Number(v.slice(1)),n=1+Math.floor(Math.random()*sides);state.logs.push(`Мастер · ${v} = ${n}`);if(state.panel==='journal')renderPanel();notice(`Демо броска: ${v} = ${n}. Результат в журнале.`);break;}
 case 'conditions':showModal('<h2>Состояния · пример</h2><p>В игре здесь открывается каталог состояний токена.</p><p class="muted">Редактор состояний не входит в проверку этого прототипа.</p>'+b('Закрыть','modalClose'));break;
 case 'hideToken':notice('Демо: здесь будет переключение видимости токена с существующей проверкой прав.');break;
 case 'addInitiative':notice('Демо: токен уже представлен в примере инициативы.');break;
 case 'transfer':showModal('<h2>Перенести выбранный токен?</h2><p>Пример подтверждения перехода в подземелье. Перенос данных не выполняется.</p>'+b('Отмена','modalClose')+b('Подтвердить (демо)','transferConfirm'));break;
 case 'transferConfirm':closeModal();notice('Подтверждение просмотрено. Токены и данные игры не перенесены.');break;
 case 'resolve':{const [id,result]=v.split(':');const r=state.requests.find(r=>r.id===Number(id));state.requests=state.requests.filter(r=>r.id!==Number(id));render();showRequests();notice(`Демо: ${r?.name}, запрос ${result==='yes'?'разрешён':'отклонён'}. Игровой токен не перемещён.`);break;}
 case 'modalClose':closeModal();break;
 case 'dirtyDiscard':{const fn=pending;state.dirty=false;closeModal();fn?.();break;}
 case 'dirtyApply':{const fn=pending;if(applyHp()){closeModal();fn?.();}else{closeModal();notice('Переход отменён: исправьте значения ХП/КД в карточке.');}break;}
 case 'rotateMap':{const p=$('#plan');state.rotation=((state.rotation||0)+90)%360;p.style.transform=`rotate(${state.rotation}deg)`;notice('Поворот схемы карты · только демонстрация.');break;}
 case 'resetMap':state.rotation=0;$('#plan').style.transform='';$('#plan').style.scale='';$('#mapScale').value='1';notice('Демокарта сброшена.');break;
 case 'demo':notice(v+' Это демонстрационный элемент.');break;
 }
}
document.addEventListener('click',e=>{
 const el=e.target.closest('button');if(!el)return;if(el.closest('#hpForm')&&el.type==='submit')return;e.preventDefault();
 if(!$('#modal').hidden&&!el.closest('#modal'))return;
 if(el.dataset.panel){openPanel(el.dataset.panel);return;}
 if(el.dataset.tool){changeTool(el.dataset.tool);return;}
 if(el.dataset.action){if(el.dataset.action==='delta')e.stopPropagation();act(el.dataset.action,el.dataset.value);return;}
 if(el.dataset.token){if(skipClick){skipClick=false;return;}if(state.preview){notice('В предпросмотре токены недоступны для редактирования.');return;}if(state.tool!=='select'){mapAction(e);return;}guarded(()=>{state.selected=Number(el.dataset.token);state.panel=null;state.dirty=false;render();notice('Токен выбран. Нажмите «Информация» или ПКМ.');});}
});
document.addEventListener('submit',e=>{e.preventDefault();if(e.target.id==='hpForm')applyHp();if(e.target.id==='createForm'){const name=$('#tokenName').value.trim();if(!name)return;const id=Math.max(...state.tokens.map(t=>t.id))+1;state.tokens.push({id,name,hp:12,max:12,ac:13,x:50,y:45,owner:'Мастер',enemy:true});state.selected=id;state.panel='token';state.tab='parameters';render();notice('Создан демонстрационный токен.');}});
document.addEventListener('input',e=>{if(e.target.closest('#hpForm'))state.dirty=true;});
document.addEventListener('change',e=>{if(e.target.id==='mapScale'){const n=Number(e.target.value);if(n>0&&n<=3){$('#plan').style.scale=n;notice('Масштаб схемы · демонстрация.');}else {e.target.value=1;notice('Для демонстрации используйте масштаб от 0,1 до 3.');}}});
function mapAction(e){
 if(state.preview){notice('Предпросмотр: действия карты заблокированы.');return;}
 const r=$('#map').getBoundingClientRect(),x=(e.clientX-r.left)/r.width*100,y=(e.clientY-r.top)/r.height*100;
 if(state.tool==='select'){guarded(()=>{state.selected=null;state.panel=null;state.dirty=false;render();});return;}
 if(['map','transitions'].includes(state.tool))return;
 remember();if(state.sub==='erase'){state.marks=state.marks.filter(m=>Math.hypot(m.x-x,m.y-y)>8);}else{let kind=state.tool==='fog'?state.sub:state.tool==='geometry'?'wall':state.tool==='ruler'?'ruler':'';state.marks.push({x,y,kind,label:state.tool==='ruler'?'Пример: 15 фт':state.tool==='area'?({circle:'○',square:'□',cone:'△'})[state.sub]:state.tool==='geometry'&&state.sub!=='wall'?({door:'Д',toggleDoor:'↔',obstacle:'П',column:'●'})[state.sub]:''});}renderMap();notice('Локальная демонстрационная разметка добавлена. UI и игра не затронуты.');
}
$('#map').addEventListener('click',e=>{if(e.target.closest('button'))return;if(skipClick){skipClick=false;return;}mapAction(e);});
$('#map').addEventListener('contextmenu',e=>{e.preventDefault();if(state.preview)return;if(state.tool!=='select'){changeTool('select');return;}const t=e.target.closest('[data-token]');if(t)inspect(t.dataset.token);});
$('#map').addEventListener('pointerdown',e=>{const t=e.target.closest('[data-token]');if(!t||state.tool!=='select'||state.preview||state.dirty||e.button!==0)return;drag={id:Number(t.dataset.token),sx:e.clientX,sy:e.clientY,moved:false};});
document.addEventListener('pointermove',e=>{if(!drag)return;if(Math.hypot(e.clientX-drag.sx,e.clientY-drag.sy)<7)return;drag.moved=true;const r=$('#map').getBoundingClientRect(),t=state.tokens.find(t=>t.id===drag.id);t.x=Math.max(5,Math.min(95,(e.clientX-r.left)/r.width*100));t.y=Math.max(12,Math.min(88,(e.clientY-r.top)/r.height*100));const el=document.querySelector(`[data-token="${t.id}"]`);el.style.left=t.x+'%';el.style.top=t.y+'%';});
document.addEventListener('pointerup',()=>{if(drag?.moved){state.selected=drag.id;skipClick=true;renderMap();notice('Токен перемещён только в демонстрации.');}drag=null;});
document.addEventListener('keydown',e=>{
 if(e.key==='Tab'&&!$('#modal').hidden){const focus=[...$('#modal').querySelectorAll('button,input,select')].filter(el=>!el.disabled);if(!focus.length)return;const first=focus[0],last=focus.at(-1);if(e.shiftKey&&document.activeElement===first){e.preventDefault();last.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first.focus();}return;}
 if(e.key!=='Escape')return;e.preventDefault();
 if(!$('#modal').hidden){closeModal();return;}
 if(document.activeElement.matches('input,textarea,select')){document.activeElement.blur();notice('Ввод завершён. Изменения требуют «Применить».');return;}
 if(state.tool!=='select'){changeTool('select');return;}
 if(state.panel){act('close');return;}
 if(state.selected){state.selected=null;renderMap();notice('Выделение снято.');}
});
render();
