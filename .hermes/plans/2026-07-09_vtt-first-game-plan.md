# План подготовки к первой игре — MeshokSGovnom VTT

> **Цель:** Закрыть все критические пробелы для проведения первой D&D-сессии в VTT.

**Архитектура:** Unity 6 URP + Netcode for GameObjects 2.x. CustomMessagingManager для чанковой передачи данных, NetworkVariable + RPC для синхронизации состояния.

---

## Фаза 1: Быстрые фиксы (минимум риска, максимум фундамента)

### Задача 1.1: Прозрачность сетки (#9)
**Суть:** Сейчас `gridColor = (0.15, 0.15, 0.15, 0.5)` — 50% прозрачности. Нужно 75% (альфа ~0.25).

**Файл:** `Assets/Scripts/GridManager.cs`
- Поменять `gridColor` в `Start()`: `new Color(0.15f, 0.15f, 0.15f, 0.25f)`
- Поменять дефолт в объявлении поля `gridColor`
- Пересоздать материал линий: `mat.color` берёт цвет из `gridColor` → линии станут прозрачнее
- Убедиться что `mat.SetFloat("_Surface", 1f)` (Transparent) уже есть — есть.

**Риски:** Нет. Чисто визуальное изменение.

---

### Задача 1.2: Стены вокруг всего геймборда 100×100 (#7)
**Суть:** Сейчас стены (`UpdateWalls`) создаются только когда карта загружена и только вокруг bounds карты. Нужно: всегда стены вокруг всей сетки 100×100, стены не двигаются.

**Файл:** `Assets/Scripts/GridManager.cs`

**План:**
1. В `Start()` после `GenerateFullGrid()` — всегда создавать стены вокруг 100×100:
   ```csharp
   Bounds defaultBounds = new Bounds(Vector3.zero, 
       new Vector3(gridWidth * cellSize, 0.1f, gridHeight * cellSize));
   UpdateWalls(defaultBounds);
   ```
2. В `SetBounds()` — убрать вызов `UpdateWalls()`. Стены создаются один раз и не пересоздаются.
3. Убрать родительскую привязку стен к `_mapBounds.center` — оставить в (0,0,0):
   ```csharp
   _wallsParent.transform.localPosition = Vector3.zero; // вместо b.center
   ```

**Риски:** Стены станут статичными. Если карта двигается через drag — стены останутся на месте (это ожидаемое поведение по задаче: «не двигались»).

---

### Задача 1.3: Фикс повторной загрузки карты (#4)
**Суть:** При загрузке второй карты `FitPlaneToTexture()` берёт текущий `localScale` (уже изменённый предыдущим масштабированием) как базовый размер — размеры ломаются.

**Файл:** `Assets/Scripts/MapController.cs`

**Корень проблемы:** В `FitPlaneToTexture()`:
```csharp
float baseSize = Mathf.Max(mapPlane.transform.localScale.x, mapPlane.transform.localScale.z);
```
После первой загрузки `localScale` уже = `_baseScale * scale`. При второй загрузке это значение используется как новый baseSize → экспоненциальный рост/сжатие.

**План:**
1. Сохранять «чистый» размер MapPlane (до масштабирования) один раз в `Start()`:
   ```csharp
   private Vector3 _originalPlaneScale; // в Start(): _originalPlaneScale = mapPlane.transform.localScale;
   ```
2. В `FitPlaneToTexture()` использовать `_originalPlaneScale` вместо текущего localScale:
   ```csharp
   float baseSize = Mathf.Max(_originalPlaneScale.x, _originalPlaneScale.z);
   ```
3. После `FitPlaneToTexture()` применить текущий `_netScale.Value`:
   ```csharp
   ApplyScale(_netScale.Value);
   ```

**Риски:** Минимальные. Механика та же, просто базовый размер не «уплывает».

---

## Фаза 2: UI-панели

### Задача 2.1: Экранные кнопки скрытия/показа панелей (#2)
**Суть:** Сейчас панели (Q/E/X/L) скрываются только клавиатурой. Нужны экранные кнопки-табы: маленькие полупрозрачные иконки у края экрана, которые возвращают панель.

**Файл:** `Assets/Scripts/Dice/DiceUI.cs`

**План:**
1. Добавить 4 метода: `ToggleLeftSidebar()`, `ToggleDebugPanel()`, `ToggleBottomPanel()`, `ToggleLogPanel()` (логика уже есть в `Update()` — вынести).
2. В каждом методе, где панель скрывается — создавать маленькую кнопку-таб у края экрана. Где показывается — убирать таб.
3. Таб: 24×60 px, приклеен к краю экрана с прозрачностью 60%, при клике показывает панель и скрывает таб.
4. Например: для левого сайдбара (Q) — таб у левого края на высоте центра. Для дебаг-панели (E) — таб у верхнего края по центру.

**Риски:** Низкие. Чисто UI, сеть не затрагивается.

---

## Фаза 3: Токены

### Задача 3.1: Токены не входят друг в друга (#1)
**Суть:** При `SnapToGrid()` несколько токенов могут занять одну клетку. Нужна проверка занятости.

**Файлы:**
- `Assets/Scripts/Networking/TokenController.cs` — SnapToGrid
- `Assets/Scripts/GridManager.cs` — реестр занятых клеток

**План:**
1. В `GridManager` добавить `HashSet<Vector2Int> _occupiedCells` и методы:
   - `bool IsCellOccupied(Vector2Int cell)` 
   - `bool TryOccupyCell(Vector2Int cell)` — возвращает false если занята
   - `void ReleaseCell(Vector2Int cell)`
2. В `TokenController.SnapToGrid()`:
   - Получить целевую клетку через `GetGridPosition()`
   - Если занята — искать ближайшую свободную (по спирали, до радиуса 3 клетки)
   - Если свободных нет — не двигать (оставить где был)
   - Занять новую клетку, освободить старую
3. При `OnDestroy()` / `OnDespawn()` — освободить клетку.
4. **Сетевая синхронизация:** `_occupiedCells` синхронизируется через `NetworkList<Vector2Int>` на сервере, клиенты обновляют локально. Или проще: сервер валидирует `SnapToGrid` и шлёт отказ через RPC если клетка занята.

**Риски:** Средние. Гонки при одновременном перемещении двух токенов на одну клетку. Решается серверной валидацией: клиент шлёт запрос на сдвиг, сервер проверяет и подтверждает/отклоняет.

---

### Задача 3.2: Удаление токенов (#6)
**Суть:** Возможность удалить токен с поля.

**Файлы:** Новый + `TokenController.cs`

**План:**
1. Добавить метод в `TokenController`:
   ```csharp
   public void RequestDespawn()
   {
       if (IsOwner)
           RequestDespawnServerRpc();
   }
   
   [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
   private void RequestDespawnServerRpc(RpcParams rpcParams = default)
   {
       // Только владелец или хост может удалить
       if (rpcParams.Receive.SenderClientId == OwnerClientId || IsHost)
           GetComponent<NetworkObject>().Despawn();
   }
   ```
2. В контекстном меню токена (`TokenContextMenu.cs`) добавить кнопку «Удалить».
3. При деспавне — освободить клетку в `GridManager` (см. задачу 3.1).

**Риски:** Низкие. NGO `Despawn()` убирает объект у всех клиентов.

---

## Фаза 4: Инструменты левой панели

### Задача 4.1: Линейка (#3.1)
**Суть:** Игрок активирует линейку, кликает на карту → тянет → все видят линию с длиной в футах.

**Файлы:** Новый `Assets/Scripts/Networking/MeasurementTool.cs` + доработка `DiceUI.cs`

**План:**
1. Новый `MeasurementTool : NetworkBehaviour`:
   - `LineRenderer` для отрисовки (или отдельный GameObject с линией)
   - При активации (только один игрок в момент времени): клик задаёт точку A, драг до точки B
   - Вычисление: `distanceFeet = Vector3.Distance(A, B) / cellSize * 5f` (1 клетка = 5 футов)
   - Синхронизация через `NetworkVariable<Vector3>` для точек A и B + `NetworkVariable<bool>` для активности
   - Или проще: `[Rpc(SendTo.Everyone)] UpdateRulerRpc(Vector3 a, Vector3 b, bool active)`
2. Кнопка в левом сайдбаре (иконка линейки) — заменяет один из «?» плейсхолдеров.
3. Цвет линии = цвет игрока (`PlayerColors.GetColor(clientId)`).

**Риски:** Средние. RPC-спам при драге — слать не чаще 10 раз/сек (каждые 100ms).

---

### Задача 4.2: Радиус (#3.2)
**Суть:** Игрок выбирает центр, тянет → круг с радиусом в футах.

**Файл:** `MeasurementTool.cs` (добавить режим)

**План:**
1. Режим Circle в `MeasurementTool`:
   - Точка A = центр, расстояние до B = радиус
   - Отрисовка: окружность через LineRenderer (32+ сегмента) или процедурный меш
   - Синхронизация та же что у линейки

---

### Задача 4.3: Куб/квадрат (#3.3)
**Суть:** Выделение квадратной области. Длина стороны в футах.

**Файл:** `MeasurementTool.cs` (добавить режим)

**План:**
1. Режим Square: клик — угол, драг — противоположный угол, привязанный к сетке.

---

### Задача 4.4: Конус (#3.4)
**Суть:** Конус из точки в направлении. Длина в футах.

**Файл:** `MeasurementTool.cs` (добавить режим)

**План:**
1. Режим Cone: клик — вершина, драг задаёт направление и длину. Отрисовка треугольника-сектора.

---

### Задача 4.5: Отметки областей (#3.5)
**Суть:** Текстурка на клетку (труднопроходимая местность, эффекты).

**Файлы:** Новый `AreaMarker.cs` + `GridManager.cs`

**План:**
1. `AreaMarker : NetworkBehaviour`:
   - Привязан к клетке (Vector2Int)
   - Тип отметки: enum (DifficultTerrain, Impassable, Silence, Web, ...)
   - Каждому типу — цвет/текстура (плейсхолдер: цветной полупрозрачный квадрат)
   - Спавнится как NetworkObject поверх клетки (плоский Quad)
   - `NetworkVariable<int>` для типа
2. UI: выбор типа из левого сайдбара → клик на клетку → спавн маркера
3. Хост может ставить/убирать любые маркеры (или все игроки — зависит от дизайна)

---

### Задача 4.6: Удаление отметок (#3.6)
**Суть:** Клик правой кнопкой по маркеру → удалить.

**Файл:** `AreaMarker.cs`

**План:**
1. Raycast по маркерам при правом клике
2. `RequestDespawn()` → `NetworkObject.Despawn()`

---

### Задача 4.7: Стены-рисовалка (#3.7)
**Суть:** Пользователь предполагает использовать #3.5 (чёрный квадрат) для стен. Так и сделаем — отдельный тип AreaMarker «Wall» (чёрный, высокий бокс).

**План:**
1. Добавить тип `Wall` в enum AreaMarker
2. Для стен создавать не плоский Quad, а вытянутый Cube (1×3×1 — высота как у стен GridManager)
3. Стены блокируют движение токенов (коллайдер)

**Риски:** Средние. Коллизия со стенами требует проверки при SnapToGrid токенов.

---

### Задача 4.8: Пинги на карте (#5)
**Суть:** Клик по карте (Alt+Click или отдельный инструмент) → появляется стрелочка цвета игрока, видимая всем, исчезает через 2-3 секунды.

**Файлы:** Новый `PingManager.cs`

**План:**
1. `PingManager : NetworkBehaviour`:
   - При Alt+Click (или выборе инструмента «пинг») — определение точки на карте
   - Отправка `[Rpc(SendTo.Everyone)] ShowPingRpc(Vector3 position, Vector3 colorRgb, ulong clientId)`
   - Спавн временного GameObject: стрелка вниз + круг
   - Автоуничтожение через 2.5 секунды
   - Цвет = `PlayerColors.GetColor(clientId)`
2. Кнопка в сайдбаре (иконка «пинг»)

**Риски:** Низкие. Без保存ения состояния — просто временный эффект.

---

## Фаза 5: Initiative Tracker

### Задача 5.1: Initiative Tracker (#8)
**Суть:** DM-управляемый трекер инициативы сверху экрана. Карточки с именами.

**Файлы:** Новый `InitiativeTracker.cs` + `InitiativeTrackerUI.cs`

**План:**
1. **Данные:** `List<InitiativeEntry>` где `InitiativeEntry = { string name, int initiative, ulong clientId }` (clientId=0 для NPC)
2. **Синхронизация:** `NetworkList<InitiativeEntry>` через кастомный `INetworkSerializable` или через JSON-строку в `NetworkVariable<string>`. Или проще: `NetworkVariable<FixedString4096Bytes>` с JSON.
3. **UI:** Canvas-панель сверху экрана. Карточки: имя + значение инициативы. Текущий ходящий подсвечен (первый в списке).
4. **DM-управление:**
   - Кнопка «+» — открывает панель ввода: имя + инициатива
   - Кнопка «End Turn» — перемещает первого в списке в конец, следующий становится активным
   - Только хост (IsHost/IsServer) может добавлять/двигать

**Сериализация InitiativeEntry для NetworkList:**
```csharp
struct InitiativeEntry : INetworkSerializable
{
    public FixedString64Bytes Name;
    public int Initiative;
    public ulong ClientId;
    
    public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
    {
        s.SerializeValue(ref Name);
        s.SerializeValue(ref Initiative);
        s.SerializeValue(ref ClientId);
    }
}
```

**Риски:** Средние. `NetworkList` с кастомным типом может быть нестабильным в NGO 2.x. Альтернатива: JSON в `NetworkVariable<FixedString4096Bytes>` с ручным парсингом.

---

## Фаза 6: Билд-фиксы

### Задача 6.1: Загрузка картинки в билде (#10)
**Суть:** `MapController.LoadImage()` и `DiceUI.OpenTexturePicker()` под `#if UNITY_EDITOR` — в билде ничего не происходит.

**Файлы:** `MapController.cs`, `DiceUI.cs`

**План:**
1. Вариант A: Использовать `UnityEngine.Windows.FileOpenDialog` — устарел, не работает в современных Unity
2. **Вариант B (рекомендуемый):** [SimpleFileBrowser](https://github.com/yasirkula/UnitySimpleFileBrowser) — бесплатный ассет. Импортировать и использовать.
3. Вариант C (без ассетов): `System.Windows.Forms.OpenFileDialog` — работает только в Windows Standalone, требует настройки Player Settings.
4. Для Windows-билда самый быстрый путь — C.

**Реализация (Вариант C):**
```csharp
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.Windows.Forms;
#endif

public void LoadImage()
{
    if (!IsOwner) return;
#if UNITY_EDITOR
    string path = EditorUtility.OpenFilePanel("Выберите изображение", "", "png,jpg,jpeg,bmp,tga");
#elif UNITY_STANDALONE_WIN
    string path = null;
    using (var dialog = new OpenFileDialog())
    {
        dialog.Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tga";
        if (dialog.ShowDialog() == DialogResult.OK)
            path = dialog.FileName;
    }
#endif
    if (string.IsNullOrEmpty(path)) return;
    byte[] data = File.ReadAllBytes(path);
    ApplyImage(data);
}
```

**Для токенов:** Та же проблема в `TokenController.LoadImage()` — вызывается из UI (контекстное меню токена). Нужно добавить `NativeFilePicker` или тот же `OpenFileDialog`.

**Риски:** Средние. `System.Windows.Forms` может не работать в IL2CPP-билде. Альтернатива — StandaloneFileBrowser из SimpleFileBrowser или кастомный плагин.

---

### Задача 6.2: Просвечивающиеся цифры в кубиках (#11)
**Суть:** В билде цифры на задних гранях кубика видны сквозь меш. Шейдер `MeshokSGovnom/FontFaceUnlit` отсутствует в билде (не добавлен в Always Included Shaders).

**Файлы:** `Dice/Dice.cs` (CreateFaceLabels), `DieMeshGenerator.cs`

**Диагностика:**
- В `Dice.CreateFaceLabels()` (строка 146) шейдер ищется через `Shader.Find("MeshokSGovnom/FontFaceUnlit")`
- Если шейдер не добавлен в Player Settings → `Shader.Find` возвращает null → `mr.material.shader = shader` не выполняется → используется дефолтный шейдер TextMesh (Unlit/Text) с ZWrite Off → цифры просвечивают

**План:**
1. **Быстрый фикс:** Заменить шейдер на встроенный с ZWrite On:
   ```csharp
   // Вместо Shader.Find("MeshokSGovnom/FontFaceUnlit")
   Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
   if (shader != null)
       mr.material.shader = shader;
   ```
   URP Unlit не имеет ZWrite по умолчанию, нужно вручную на материале после:
   ```csharp
   mr.material.SetFloat("_ZWrite", 1f);
   ```

2. **Правильный фикс:** Создать простой шейдер `FontFaceUnlit.shader` в `Assets/Shaders/` и добавить его в **Project Settings → Graphics → Always Included Shaders**.

3. Проверить что TextMesh рендерится после кубика (renderQueue > 2000 — основной меш на 2000):
   ```csharp
   mr.material.renderQueue = 2001;
   ```

**Риски:** Низкие. Шейдер либо есть в сборке либо нет. Добавление в Always Included гарантирует наличие.

---

## Итоговый порядок выполнения

| # | Задача | Сложность | Зависит от |
|---|--------|-----------|------------|
| 1 | 1.1 — Прозрачность сетки | ✓ | — |
| 2 | 1.2 — Стены 100×100 | ✓✓ | — |
| 3 | 1.3 — Фикс загрузки карты | ✓ | — |
| 4 | 2.1 — Кнопки UI панелей | ✓✓ | — |
| 5 | 3.1 — Коллизия токенов | ✓✓✓ | 1.3 |
| 6 | 3.2 — Удаление токенов | ✓ | 3.1 |
| 7 | 4.1 — Линейка | ✓✓✓ | 2.1 |
| 8 | 4.2 — Радиус | ✓✓ | 4.1 |
| 9 | 4.3 — Куб | ✓✓ | 4.1 |
| 10 | 4.4 — Конус | ✓✓ | 4.1 |
| 11 | 4.5 — Отметки областей | ✓✓✓ | — |
| 12 | 4.6 — Удаление отметок | ✓ | 4.5 |
| 13 | 4.7 — Стены-рисовалка | ✓✓ | 4.5 |
| 14 | 4.8 — Пинги | ✓✓ | — |
| 15 | 5.1 — Initiative Tracker | ✓✓✓ | 2.1 |
| 16 | 6.1 — Загрузка в билде | ✓✓ | — |
| 17 | 6.2 — Цифры кубиков | ✓ | — |

**Общий вектор:** Простые фиксы → UI → токены → инструменты → трекер → билд-фиксы.
