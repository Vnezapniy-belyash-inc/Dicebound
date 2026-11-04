from pathlib import Path
from html import escape
import json

ROOT = Path(__file__).resolve().parent
C = {'bg':'#eee9de','paper':'#faf7ef','ink':'#183a31','muted':'#626c65','line':'#bdb7a8','accent':'#ad6736'}
parts=[]
def rect(x,y,w,h,fill='paper',stroke='line',r=7):
    parts.append(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{C.get(fill,fill)}" stroke="{C.get(stroke,stroke)}"/>')
def txt(x,y,s,size=18,color='ink',weight=400):
    parts.append(f'<text x="{x}" y="{y}" font-family="Segoe UI,Arial,sans-serif" font-size="{size}" font-weight="{weight}" fill="{C.get(color,color)}">{escape(s)}</text>')
def button(x,y,w,s,active=False):
    rect(x,y,w,36,'ink' if active else 'paper');txt(x+12,y+24,s,16,'paper' if active else 'ink')
def lines(x,y,ss,size=17,step=28):
    for i,s in enumerate(ss):txt(x,y+i*step,s,size)
def badge(x,y,s):
    rect(x,y,29,29,'accent','accent',14);txt(x+8,y+21,s,17,'paper',700)

rect(0,0,1600,1180,'bg','bg',0)
txt(30,38,'DICEBOUND · ЭТАП 1 / СХЕМА СТОЛА МАСТЕРА',25,weight=700)
txt(30,68,'Согласование компоновки и поведения · статическая схема · 04.11.2026',17,'muted')
rect(24,90,1552,810)
rect(24,90,1552,58,'ink','ink');txt(44,126,'Dicebound',24,'paper',700)
txt(228,125,'Старая обитель ▾',20,'paper');txt(650,125,'Мастер · Участники / код',17,'paper')
txt(1120,125,'Сессия / файл     Настройки',17,'paper');badge(1530,104,'A')
rect(24,148,118,752,'ink','ink')
for y,label in [(192,'Стол'),(262,'Сцены'),(332,'Токены'),(402,'Библиотека'),(472,'Мой лист*'),(542,'Игроки'),(862,'Выход')]:txt(36,y,label,16,'paper')
badge(36,790,'B')
rect(154,160,1410,48)
txt(170,191,'Раунд 3',18,weight=700)
for x,w,s,a in [(280,175,'18 Эллиан',True),(468,180,'15 Гоблин 1',False),(662,160,'12 Мира',False),(836,180,'8 Гоблин 2',False),(1028,48,'…',False),(1090,210,'Следующий ход →',False),(1312,140,'+ / список ▾',False)]:button(x,166,w,s,a)
badge(1520,170,'C')
rect(154,218,1410,48)
for x,w,s,a in [(164,110,'Выбор',False),(282,115,'+ Токен',False),(405,120,'Линейка',False),(533,120,'Области ▾',False),(661,125,'Отметки ▾',False),(794,145,'Геометрия ▾',False),(947,105,'Туман',True),(1060,110,'Карта ▾',False),(1178,145,'Переходы ▾',False)]:button(x,224,w,s,a)
badge(1520,228,'D')
rect(154,276,1410,49)
for x,w,s,a in [(164,168,'Туман включён',False),(340,123,'Раскрыть',True),(471,115,'Закрыть',False),(594,100,'Пауза',False),(702,205,'Глазами игроков ▾',False),(915,125,'Отменить',False),(1048,130,'Завершить',False)]:button(x,282,w,s,a)
badge(1520,286,'E')
rect(154,335,1410,495,'#e1e6df')
for x in range(174,1564,40):parts.append(f'<path d="M{x} 335V830" stroke="#c4cec4" stroke-width="1"/>')
for y in range(350,830,40):parts.append(f'<path d="M154 {y}H1564" stroke="#c4cec4" stroke-width="1"/>')
rect(1200,335,364,495,'#a8afa7','#a8afa7',0)
txt(1220,380,'НЕИССЛЕДОВАНО',17,weight=700)
parts.append('<path d="M230 415H690V740H380V640H230Z" fill="none" stroke="#79877b" stroke-width="9"/>')
txt(186,370,'F · КАРТА / ИГРОВОЕ ПОЛЕ',17,weight=700)
for x,y,s in [(480,485,'Эллиан'),(700,685,'Гоблин 1'),(370,720,'Мира')]:
    parts.append(f'<circle cx="{x}" cy="{y}" r="29" fill="#faf7ef" stroke="#ad6736" stroke-width="3"/>');txt(x-28,y+53,s,16)
button(522,450,150,'ⓘ Информация')
rect(790,370,350,390,'paper','accent')
txt(810,404,'G · Эллиан',22,weight=700);txt(1105,402,'×',22)
txt(810,439,'Параметры | Доступ | Статблок',16)
lines(810,481,['ХП: [24] / [32]      КД: [16]','Изменение ХП: [−5] [Δ] [+5]','[Применить]   ошибки у поля','Состояния…','Владелец: Анна'],17,36)
button(810,670,306,'Все свойства / изображение…')
txt(810,737,'Временная карточка, закрывается ×',15,'muted')
parts.append('<circle cx="1200" cy="590" r="43" fill="none" stroke="#183a31" stroke-dasharray="7 5" stroke-width="2"/>')
txt(1190,597,'+',25);txt(1210,660,'Кисть тумана',17)
rect(154,840,1410,48)
txt(168,871,'H · Кубики ▾   d4  d6  d8  d10  d12  d20  d100',16)
txt(778,871,'Перемещения · 2',16);txt(1090,871,'Журнал бросков ▴',16)
txt(169,812,'Активен: раскрыть туман · ЛКМ — кисть · Esc — завершить инструмент',16)
txt(30,940,'ПОСТОЯННО: A сессия · B разделы · C инициатива · D инструменты · H доки',20,weight=700)
lines(30,976,['ПО ЗАДАЧЕ: E параметры активного инструмента · G карточка выбранного токена.',
'В примере карточка открыта; кисть действует только на свободной карте. UI перехватывает свои клики.',
'Сцены / токены / библиотека / лист: одна временная панель за раз, вместо карточки G.',
'Модальное подтверждение или запрос перемещения блокирует карту; закрытие не завершает сетевой бой.',
'* «Мой лист» — предлагаемый вход к существующему локальному листу; это не лист выбранного токена.'],18,31)
txt(30,1150,'ПРЕДЛОЖЕНИЕ UX. Существующие функции и ограничения перечислены отдельно в specification.md.',17,'accent',600)
svg='<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1180" viewBox="0 0 1600 1180">'+''.join(parts)+'</svg>'
ROOT.mkdir(parents=True,exist_ok=True)
(ROOT/'gm-table-wireframe.svg').write_text(svg,encoding='utf-8')
(ROOT/'gm-table-wireframe.html').write_text('<!doctype html><html lang="ru"><meta charset="utf-8"><title>Dicebound — схема этапа 1</title><style>html,body{margin:0;background:#eee9de}svg{width:100%;height:auto;display:block}</style>'+svg+'</html>',encoding='utf-8')
print(ROOT/'gm-table-wireframe.svg')
