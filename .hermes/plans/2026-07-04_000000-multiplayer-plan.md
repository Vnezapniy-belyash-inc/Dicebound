# Multiplayer Implementation Plan — MeshokSGovnom (D&D Virtual Tabletop)

> **For Hermes:** Use this plan task-by-task. Each task is bite-sized (2–5 min work). TDD where possible.

**Goal:** Add online multiplayer (DM + 8 players = 9 total) with host/client model via Unity NGO + Unity Relay.

**Architecture:** Netcode for GameObjects (NGO) for state sync, Unity Transport (UTP) for transport layer, Unity Relay for internet NAT punch-through. Host-authoritative model: DM is host, owns game state; clients send inputs via RPCs. Physics dice synced via NetworkTransform. Map textures transferred from host to all clients. 9 concurrent users.

**Tech Stack:** Unity 6000.5.1f1, NGO (`com.unity.netcode.gameobjects`), UTP (`com.unity.transport`), Unity Relay + Lobby, ParrelSync for testing.

---

## Phase 0: Исследование и выбор стека

> **Важно:** в скилле написано «Mirror», но Mirror — стороннее решение. Для Unity 6 официальный стек: NGO + UTP + Relay. Это и будем использовать, как ты и хотел.

### Почему NGO, а не Mirror

| Критерий | NGO (Netcode for GameObjects) | Mirror |
|---|---|---|
| Поддержка Unity 6 | ✅ Официальная | ⚠️ Сторонняя |
| Unity Relay | ✅ Встроенная интеграция | ⚠️ Через адаптер |
| Unity Lobby | ✅ Встроенная интеграция | ❌ Нет |
| Документация | Официальная Unity | Community |
| NetworkVariable | ✅ | ✅ (SyncVar) |
| RPC | ✅ (ServerRpc/ClientRpc) | ✅ (Command/ClientRpc) |
| Бесплатно | ✅ | ✅ (но часть фич платная) |

**Решение: NGO + Unity Transport + Unity Relay + Unity Lobby.**

---

## Phase 1: Установка пакетов и настройка сервисов

### Task 1.1: Установить NGO пакеты

**Файлы:** `Packages/manifest.json`

**Действие:**
1. Открыть Unity, Package Manager → Unity Registry
2. Установить:
   - `Netcode for GameObjects` (`com.unity.netcode.gameobjects`)
   - `Unity Transport` (`com.unity.transport`) — если не подтянулся авто
   - `Unity Relay` (`com.unity.services.relay`)
   - `Unity Lobby` (`com.unity.services.lobby`)
   - `Multiplayer Play Mode` (`com.unity.multiplayer.playmode`) — аналог ParrelSync, но от Unity

**Альтернатива через manifest.json (если Package Manager глючит):**
```json
"com.unity.netcode.gameobjects": "2.4.0",
"com.unity.transport": "2.4.0",
"com.unity.services.relay": "1.1.0",
"com.unity.services.lobby": "1.3.0",
"com.unity.multiplayer.playmode": "1.4.0"
```
(Версии уточнить по последним доступным.)

**Проверка:** после установки в Package Manager не должно быть ошибок компиляции.

---

### Task 1.2: Настроить Unity Services (Relay + Lobby)

**Действия в Editor:**
1. Window → General → Services
2. Создать Project ID (или прилинковать существующий)
3. Включить:
   - **Relay** — создать allocation, скопировать API key
   - **Lobby** — включить

**Файлы:** создастся `Assets/Resources/UnityServicesProjectSettings.asset`

**Важно:** для Relay нужен Unity Cloud Dashboard → проект → Multiplayer → Relay. Лимиты: 50 CCU бесплатно. Для разработки хватит.

**Проверка:** в Services окне Relay и Lobby показывают зелёный статус «Enabled».

---

### Task 1.3: Установить Multiplayer Play Mode (если не ParrelSync)

**Опции:**
- **Multiplayer Play Mode** (Unity, `com.unity.multiplayer.playmode`) — запускает до 4 инстансов из одного Editor. Встроенная штука.
- **ParrelSync** (Asset Store) — клонирует проект, запускает два Editor.

**Рекомендация:** Multiplayer Play Mode — легче, без клонирования. ParrelSync — полезен если нужно видеть оба Editor одновременно.

Установи тот, который удобнее. Для начала хватит Multiplayer Play Mode.

**Проверка:** Window → Multiplayer → Multiplayer Play Mode → включить, настроить количество инстансов (2).

---

## Phase 2: Core Networking Setup

### Task 2.1: Добавить NetworkManager на сцену

**Файлы:**
- Создать: `Assets/Scripts/Networking/GameNetworkManager.cs`
- Изменить: сцена `test.unity`

```csharp
// GameNetworkManager.cs
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class GameNetworkManager : MonoBehaviour
{
    public static GameNetworkManager Instance { get; private set; }

    private NetworkManager _networkManager;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        _networkManager = GetComponent<NetworkManager>();
    }

    public void StartHost() => _networkManager.StartHost();
    public void StartClient() => _networkManager.StartClient();
    public void Shutdown() => _networkManager.Shutdown();

    public bool IsHost => _networkManager.IsHost;
    public bool IsClient => _networkManager.IsClient;
    public ulong LocalClientId => _networkManager.LocalClientId;
}
```

**Действия в Editor:**
1. Создать пустой GameObject «NetworkManager»
2. Добавить компонент `NetworkManager`
3. Добавить компонент `UnityTransport`
4. Добавить скрипт `GameNetworkManager`

**Проверка:** запустить сцену — нет ошибок в консоли.

---

### Task 2.2: Создать NetworkPlayerManager (подключение/отключение)

**Файлы:**
- Создать: `Assets/Scripts/Networking/NetworkPlayerManager.cs`

```csharp
// NetworkPlayerManager.cs
using Unity.Netcode;
using UnityEngine;

public class NetworkPlayerManager : NetworkBehaviour
{
    public static NetworkPlayerManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[Multiplayer] Client {clientId} connected");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"[Multiplayer] Client {clientId} disconnected");
    }

    public override void OnDestroy()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
        base.OnDestroy();
    }
}
```

**Действия в Editor:**
1. Повесить `NetworkPlayerManager` на тот же GameObject «NetworkManager»

**Проверка:** после добавления NGO — код компилируется без ошибок.

---

## Phase 3: Lobby System (Host / Join)

### Task 3.1: Создать UI лобби

**Файлы:**
- Создать: `Assets/Scripts/Networking/LobbyUI.cs`

```csharp
// LobbyUI.cs
using UnityEngine;
using UnityEngine.UI;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject lobbyPanel;
    public Button hostButton;
    public InputField joinCodeInput;
    public Button joinButton;
    public Text statusText;

    private void Start()
    {
        hostButton.onClick.AddListener(OnHostClicked);
        joinButton.onClick.AddListener(OnJoinClicked);
        lobbyPanel.SetActive(true);
    }

    private async void OnHostClicked()
    {
        statusText.text = "Creating lobby...";
        // TODO: Phase 3.2 — Relay allocation
        GameNetworkManager.Instance.StartHost();
        lobbyPanel.SetActive(false);
        statusText.text = "Host started!";
    }

    private async void OnJoinClicked()
    {
        string code = joinCodeInput.text.Trim();
        if (string.IsNullOrEmpty(code))
        {
            statusText.text = "Enter join code!";
            return;
        }
        statusText.text = "Joining...";
        // TODO: Phase 3.2 — Relay join
        GameNetworkManager.Instance.StartClient();
    }

    private void Update()
    {
        if (Keyboard.current?.escapeKey.wasPressedThisFrame ?? false)
        {
            lobbyPanel.SetActive(!lobbyPanel.activeSelf);
        }
    }
}
```

**Действия в Editor:**
1. Создать Canvas → Panel «LobbyPanel»
2. Добавить кнопки Host/Join, InputField, Text
3. Повесить LobbyUI

**Проверка:** панель появляется при старте, кнопки кликабельны.

---

### Task 3.2: Интегрировать Unity Relay + Lobby

**Файлы:**
- Создать: `Assets/Scripts/Networking/RelayManager.cs`

```csharp
// RelayManager.cs
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public class RelayManager : MonoBehaviour
{
    public static RelayManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <summary>Initialize Unity Services (call once at app start)</summary>
    public static async Task InitializeServices()
    {
        if (UnityServices.State == ServicesInitializationState.Initialized)
            return;
        await UnityServices.InitializeAsync();
    }

    /// <summary>Host: allocate relay and return join code</summary>
    public async Task<string> CreateRelayAllocation(int maxPlayers = 9)
    {
        await InitializeServices();

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetHostRelayData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData
        );

        Debug.Log($"[Relay] Host allocation created. Join code: {joinCode}");
        return joinCode;
    }

    /// <summary>Client: join relay with join code</summary>
    public async Task JoinRelayAllocation(string joinCode)
    {
        await InitializeServices();

        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetClientRelayData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.Key,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData
        );

        Debug.Log($"[Relay] Joined relay with code: {joinCode}");
    }
}
```

**Интеграция с LobbyUI:**

Обновить `LobbyUI.OnHostClicked`:
```csharp
private async void OnHostClicked()
{
    statusText.text = "Creating lobby...";
    string code = await RelayManager.Instance.CreateRelayAllocation(9);
    GameNetworkManager.Instance.StartHost();
    lobbyPanel.SetActive(false);
    statusText.text = $"Host started! Code: {code}";
    // TODO: копировать код в буфер обмена или показать крупно
}
```

Обновить `LobbyUI.OnJoinClicked`:
```csharp
private async void OnJoinClicked()
{
    string code = joinCodeInput.text.Trim();
    if (string.IsNullOrEmpty(code)) { statusText.text = "Enter join code!"; return; }
    statusText.text = "Joining...";
    await RelayManager.Instance.JoinRelayAllocation(code);
    GameNetworkManager.Instance.StartClient();
}
```

**Действия в Editor:**
1. Повесить `RelayManager` на GameObject «NetworkManager»
2. Обновить LobbyUI код

**Проверка:** собрать билд + запустить Editor → Host в Editor, Client в билде с кодом → оба коннектятся.

## Phase 5: Networked Dice (физика на клиенте-владельце)

> **Архитектура (вариант C):** физика кубика работает на машине того игрока, который его бросил. `NetworkTransform` (ClientNetworkTransform) реплицирует позицию/вращение всем остальным через сервер. Сервер не считает физику — только ретранслирует. Нагрузка распределена: каждый клиент считает только свои кубики.

### Task 5.1: Сетевой префаб кубика (NetworkDice)

**Файлы:**
- Создать: `Assets/Scripts/Networking/NetworkDice.cs`

```csharp
// NetworkDice.cs
using System;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkTransform))]
public class NetworkDice : NetworkBehaviour
{
    [Header("Physics")]
    public float rollForce = 8f;
    public float maxTorque = 12f;
    public float stopThreshold = 0.15f;
    public float settleTime = 0.6f;

    public DieType DieType { get; private set; }
    public int Result { get; private set; } = -1;
    public bool IsRolling { get; private set; }
    public bool HasResult { get; private set; }

    public event Action<NetworkDice> OnResultReady;

    private Rigidbody _rb;
    private DieFaceData[] _faces;
    private float _settleTimer;
    private bool _didInit;

    /// <summary>Инициализация (вызывается на ВЛАДЕЛЬЦЕ после спавна)</summary>
    public void Init(DieType type, int fontSize = 48)
    {
        DieType = type;
        var (mesh, faces) = DieMeshGenerator.Generate(type);
        _faces = faces;
        GetComponent<MeshFilter>().mesh = mesh;

        var renderer = GetComponent<MeshRenderer>();
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null)
        {
            Material mat = new Material(urpLit);
            mat.color = new Color(0.85f, 0.15f, 0.12f);
            mat.SetFloat("_Smoothness", 0f);
            renderer.material = mat;
        }

        MeshCollider mc = GetComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = true;

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = true;
        _rb.mass = 0.3f;
        _rb.angularDamping = 0.3f;
        _rb.linearDamping = 0.2f;

        CreateFaceLabels(fontSize);
        _didInit = true;
    }

    private void CreateFaceLabels(int fontSize)
    {
        for (int i = 0; i < _faces.Length; i++)
        {
            var fd = _faces[i];
            string label = FormatFaceValue(DieType, fd.value);
            GameObject labelObj = new GameObject($"FaceLabel{i}");
            labelObj.transform.SetParent(transform, false);
            labelObj.transform.localPosition = fd.center + fd.normal * 0.02f;
            labelObj.transform.localRotation = Quaternion.LookRotation(-fd.normal);
            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = label;
            tm.fontSize = fontSize;
            tm.color = Color.black;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.characterSize = 0.04f;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Владелец = тот кто бросил. Только владелец запускает физику.
        if (IsOwner)
        {
            _rb.isKinematic = false; // физика только у владельца
        }
        else
        {
            _rb.isKinematic = true; // остальные: кукла, только смотрят
        }
    }

    /// <summary>Бросок — вызывается ТОЛЬКО на владельце</summary>
    public void Roll()
    {
        if (!IsOwner) return;

        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        IsRolling = true;
        HasResult = false;
        Result = -1;
        _settleTimer = 0f;

        Vector3 forceDir = (Vector3.up * 0.8f + UnityEngine.Random.insideUnitSphere * 0.6f).normalized;
        float force = rollForce * UnityEngine.Random.Range(0.7f, 1.3f);
        _rb.AddForce(forceDir * force, ForceMode.Impulse);

        Vector3 torque = UnityEngine.Random.insideUnitSphere * maxTorque;
        _rb.AddTorque(torque, ForceMode.Impulse);
    }

    private void Update()
    {
        // ТОЛЬКО владелец определяет остановку и результат
        if (!IsOwner || !_didInit || !IsRolling || HasResult) return;

        bool isSettled = _rb.linearVelocity.magnitude < stopThreshold
                      && _rb.angularVelocity.magnitude < stopThreshold;

        if (isSettled)
        {
            _settleTimer += Time.deltaTime;
            if (_settleTimer >= settleTime)
                DetermineResult();
        }
        else
        {
            _settleTimer = 0f;
        }
    }

    private void DetermineResult()
    {
        if (_faces == null || _faces.Length == 0) return;

        float bestDot = float.MinValue;
        int bestIndex = 0;

        if (DieType == DieType.d4)
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot < bestDot) continue;
                bestDot = dot;
                bestIndex = i;
            }
        }
        else
        {
            for (int i = 0; i < _faces.Length; i++)
            {
                Vector3 worldNormal = transform.TransformDirection(_faces[i].normal);
                float dot = Vector3.Dot(worldNormal, Vector3.up);
                if (dot > bestDot) { bestDot = dot; bestIndex = i; }
            }
        }

        Result = _faces[bestIndex].value;
        HasResult = true;
        IsRolling = false;

        // Отправляем результат на сервер → всем
        ReportResultServerRpc(Result);
        OnResultReady?.Invoke(this);
    }

    /// <summary>Владелец сообщает результат серверу</summary>
    [ServerRpc]
    private void ReportResultServerRpc(int result)
    {
        // Сервер ретранслирует всем (кроме владельца — он уже знает)
        BroadcastResultClientRpc(DieType.ToString(), result, OwnerClientId);
    }

    [ClientRpc]
    private void BroadcastResultClientRpc(string dieType, int result, ulong throwerId)
    {
        // На владельце результат уже applied, но UI всё равно показываем
        DiceUI.Instance?.ShowResult(dieType, result, throwerId);
    }

    private static string FormatFaceValue(DieType type, int value)
    {
        return type switch
        {
            DieType.d100 when value == 0 => "00",
            DieType.d100 => value.ToString(),
            DieType.d10 when value == 0 => "0",
            _ => value.ToString(),
        };
    }
}
```

### Task 5.2: Создать NetworkDiceManager (спавн с владельцем)

**Файлы:**
- Создать: `Assets/Scripts/Networking/NetworkDiceManager.cs`

```csharp
// NetworkDiceManager.cs
using Unity.Netcode;
using UnityEngine;

public class NetworkDiceManager : NetworkBehaviour
{
    [Header("Prefabs")]
    public NetworkObject dicePrefab;

    [Header("Spawn")]
    public float spawnHeight = 2f;

    /// <summary>Любой клиент запрашивает бросок — хост спавнит с владельцем = отправитель</summary>
    [ServerRpc(RequireOwnership = false)]
    public void RollDiceServerRpc(DieType type, int count, Vector3 spawnPos, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = spawnPos + Random.insideUnitSphere * 0.5f;
            pos.y = spawnHeight;

            // Спавним с владельцем = отправитель
            NetworkObject netObj = Instantiate(dicePrefab, pos, Random.rotation);
            netObj.SpawnWithOwnership(senderId);

            var dice = netObj.GetComponent<NetworkDice>();
            dice.Init(type);

            // Говорим владельцу запустить физику
            dice.Roll(); // Roll() проверяет IsOwner — сработает только у senderId

            // Автоудаление через 5 секунд после остановки
            dice.OnResultReady += (d) =>
            {
                StartCoroutine(DestroyAfterDelay(d.gameObject, 5f));
            };
        }
    }

    private System.Collections.IEnumerator DestroyAfterDelay(GameObject go, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (go != null && go.TryGetComponent(out NetworkObject no))
            no.Despawn();
    }
}
```

### Task 5.3: Префаб NetworkDice

**Действия в Editor:**
1. Создать префаб «NetworkDice»:
   - Пустой GameObject
   - Добавить: `NetworkObject` + `NetworkTransform` + `NetworkDice` + `Rigidbody` + `MeshFilter` + `MeshRenderer` + `MeshCollider`
   - В `NetworkTransform`: использовать **ClientNetworkTransform** (владелец = источник Transform), Interpolate
   - Rigidbody: `isKinematic = true` по умолчанию (владелец включит физику в `OnNetworkSpawn`)
2. Добавить префаб в `NetworkManager.NetworkConfig.Prefabs`
3. Перетащить префаб в поле `dicePrefab` у `NetworkDiceManager`

### Как это работает (вариант C — client-authoritative physics)

```
Игрок A кидает d20:
1. A → RollDiceServerRpc → Хост
2. Хост: SpawnWithOwnership(A) → префаб появляется у ВСЕХ
3. Владелец (A): isKinematic=false → физика летит МГНОВЕННО
4. NetworkTransform (ClientNetworkTransform) → позиция/вращение A → сервер → всем
5. Остальные игроки: isKinematic=true → просто смотрят (кукла)
6. Кубик остановился на A → DetermineResult() → ReportResultServerRpc
7. Хост → BroadcastResultClientRpc → ВСЕ видят результат в UI
8. Через 5 секунд хост деспавнит
```

**Распределение нагрузки:**
| Компонент | Где считает | Трафик |
|---|---|---|
| Физика кубика | Машина кидающего | — |
| Transform sync | ClientNetworkTransform | ~2-5 KB/сек на кубик |
| Результат | Владелец → хост → все | ~50 байт |

**Проверка:** Client бросает d20 → мгновенный полёт у бросающего, все остальные видят полёт с задержкой ~пинг, результат у всех.

---

## Phase 7: Grid State (карта передаётся по сети)

### Task 7.1: Синхронизация размера сетки

**Файлы:**
- Модифицировать: `GridManager.cs` — добавить NetworkBehaviour

```csharp
public class GridManager : NetworkBehaviour
{
    // ...

    [ServerRpc(RequireOwnership = false)]
    public void ResizeGridServerRpc(int width, int height)
    {
        gridWidth = width;
        gridHeight = height;
        GenerateGridVisual();
        if (createWalls) GenerateWalls();
        ResizeGridClientRpc(width, height);
    }

    [ClientRpc]
    private void ResizeGridClientRpc(int width, int height)
    {
        gridWidth = width;
        gridHeight = height;
        if (!IsHost)
        {
            GenerateGridVisual();
            if (createWalls) GenerateWalls();
        }
    }
}
```

### Task 7.2: Передача текстуры карты (обязательно, чанками)

**Файлы:**
- Создать: `Assets/Scripts/Networking/MapTextureSync.cs`

**Проблема:** PNG/JPG текстуры карт могут быть 2–20 МБ. RPC с `byte[]` такого размера обрубит соединение. Решение: разбиваем на чанки по ~4 КБ и передаём через кастомные именованные сообщения (`CustomMessagingManager`).

```csharp
// MapTextureSync.cs
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MapTextureSync : NetworkBehaviour
{
    private const int CHUNK_SIZE = 4096; // 4 KB per chunk
    private const string MSG_MAP_CHUNK = "MapChunk";
    private const string MSG_MAP_COMPLETE = "MapComplete";

    private List<byte> _receiveBuffer;
    private int _expectedChunks;
    private int _receivedChunks;
    private GridManager _gridManager;

    private void Awake()
    {
        _gridManager = GetComponent<GridManager>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Сервер слушает запросы
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                "RequestMap", OnRequestMap);
        }
        else
        {
            // Клиент слушает чанки
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_MAP_CHUNK, OnMapChunkReceived);
            NetworkManager.Singleton.CustomMessagingManager.RegisterNamedMessageHandler(
                MSG_MAP_COMPLETE, OnMapComplete);
        }
    }

    /// <summary>DM (host) загружает карту и отправляет всем клиентам</summary>
    public void SendMapToAll(Texture2D texture)
    {
        if (!IsServer) return;

        // Применяем локально
        _gridManager.SetBoardTexture(texture);

        // Сериализуем в PNG
        byte[] pngData = texture.EncodeToPNG();
        int totalChunks = Mathf.CeilToInt((float)pngData.Length / CHUNK_SIZE);

        Debug.Log($"[MapSync] Sending map: {pngData.Length} bytes in {totalChunks} chunks");

        // Сначала шлём метаданные (количество чанков, размер текстуры)
        using var metaStream = new FastBufferWriter(
            sizeof(int) * 3, Unity.Collections.Allocator.Temp);
        metaStream.WriteValue(totalChunks);
        metaStream.WriteValue(texture.width);
        metaStream.WriteValue(texture.height);

        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            "MapMeta", NetworkManager.Singleton.ConnectedClientsIds, metaStream);

        // Шлём чанки
        for (int i = 0; i < totalChunks; i++)
        {
            int offset = i * CHUNK_SIZE;
            int size = Mathf.Min(CHUNK_SIZE, pngData.Length - offset);

            using var chunkStream = new FastBufferWriter(
                sizeof(int) + size, Unity.Collections.Allocator.Temp);
            chunkStream.WriteValue(i); // chunk index
            chunkStream.WriteBytes(pngData, offset, size);

            NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
                MSG_MAP_CHUNK, NetworkManager.Singleton.ConnectedClientsIds, chunkStream);
        }
    }

    /// <summary>Новый клиент запрашивает текущую карту</summary>
    private void OnRequestMap(ulong clientId, FastBufferReader reader)
    {
        if (!IsServer) return;
        // TODO: сохранить текущую текстуру и отправить конкретному клиенту
        Debug.Log($"[MapSync] Client {clientId} requested map");
    }

    private void OnMapChunkReceived(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValue(out int chunkIndex);

        int dataSize = (int)(reader.Length - sizeof(int));
        byte[] chunkData = new byte[dataSize];
        reader.ReadBytes(chunkData, dataSize);

        if (_receiveBuffer == null)
            _receiveBuffer = new List<byte>();

        // Расширяем буфер если нужно
        int requiredSize = (chunkIndex + 1) * CHUNK_SIZE;
        while (_receiveBuffer.Count < requiredSize)
            _receiveBuffer.Add(0);

        // Копируем чанк в буфер
        int destOffset = chunkIndex * CHUNK_SIZE;
        for (int i = 0; i < chunkData.Length; i++)
        {
            if (destOffset + i < _receiveBuffer.Count)
                _receiveBuffer[destOffset + i] = chunkData[i];
        }

        _receivedChunks++;
        Debug.Log($"[MapSync] Chunk {chunkIndex + 1}/{_expectedChunks}");
    }

    private void OnMapComplete(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValue(out int width);
        reader.ReadValue(out int height);

        Debug.Log($"[MapSync] Map received: {width}x{height}, {_receiveBuffer.Count} bytes");

        Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        tex.LoadImage(_receiveBuffer.ToArray());
        tex.Apply();

        _gridManager.SetBoardTexture(tex);
        _receiveBuffer = null;
    }

    public override void OnDestroy()
    {
        if (NetworkManager.Singleton?.CustomMessagingManager != null)
        {
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler("RequestMap");
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_MAP_CHUNK);
            NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_MAP_COMPLETE);
        }
        base.OnDestroy();
    }
}
```

**Упрощение (MVP):** для первой версии можно передавать PNG целиком через `ClientRpc` с `byte[]`, если текстуры ≤ 1 МБ. Чанковая передача — для продакшена. Но лучше сразу сделать чанки — меньше проблем с большими картами.

**Действия в Editor:**
1. Повесить `MapTextureSync` на GameObject «GameBoard» (рядом с GridManager)
2. В `DiceUI` (или новом `HostUI`) добавить кнопку «Загрузить карту» → вызывает `MapTextureSync.SendMapToAll()`

**Проверка:** Host загружает карту → через 2-3 секунды все клиенты видят ту же карту на геймборде.

---

## Phase 8: Тестирование

### Task 8.1: Multiplayer Play Mode — локальный тест

**Настройка:**
1. Window → Multiplayer → Multiplayer Play Mode
2. Enable «Virtual Players» — 1 Editor + 1 Virtual Player
3. Или 2 Editor instances

**Тест-кейсы:**

| # | Тест | Ожидание |
|---|---|---|
| 1 | Host запускает игру | Editor + Virtual Player подключаются |
| 2 | Client бросает кубик | Все видят полёт и результат |
| 3 | Host загружает карту | Все клиенты видят карту |

**Важно:** Multiplayer Play Mode позволяет запускать несколько инстансов **из одного Editor** без клонирования проекта. Быстро, удобно.

---

### Task 8.2: Билд + Editor тест (через Relay)

**Действия:**
1. Build Settings → Build → Standalone .exe
2. Editor: Play → Host (получить join code)
3. Билд: ввести join code → Join
4. Проверить синхронизацию через интернет

**Тест-кейсы добавляются:**
| # | Тест | Ожидание |
|---|---|---|
| 4 | Host (Editor) + Client (Build) через Relay | Успешное соединение |
| 5 | Client отключается | Корректная обработка |
| 6 | Host закрывается | Клиент получает уведомление |

---

### Task 8.3: Автоматизированное тестирование (Integration Tests)

**Файлы:**
- Создать: `Assets/Tests/Networking/MultiplayerIntegrationTest.cs`

```csharp
// MultiplayerIntegrationTest.cs
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.TestHelpers.Runtime;
using UnityEngine;

public class MultiplayerIntegrationTest : NetcodeIntegrationTest
{
    protected override int NumberOfClients => 2;

    private GameObject _networkPrefab;

    [UnitySetUp]
    public IEnumerator Setup()
    {
        yield return new WaitForSeconds(0.5f);
    }

    [UnityTest]
    public IEnumerator TestHostClientConnection()
    {
        // Проверяем что все подключены
        Assert.IsTrue(m_ServerNetworkManager.IsServer);
        Assert.AreEqual(2, m_ServerNetworkManager.ConnectedClients.Count);
        yield return null;
    }
}
```

**Действия:**
1. Создать Assembly Definition для тестов (`Assets/Tests/Networking/Tests.asmdef`) с ссылками на:
   - `Unity.Netcode.Runtime`
   - `Unity.Netcode.TestHelpers.Runtime`
   - `nunit.framework`
   - `UnityEngine.TestRunner`
2. Запустить через Window → General → Test Runner

**Проверка:** тесты проходят зелёным.

---

## Phase 9: Production Polish (после базового функционала)

### Task 9.1: Reconnection handling
- Клиент отключился → может переподключиться с тем же clientId
- Токены и состояние восстанавливаются

### Task 9.2: Latency compensation
- Client-side prediction для движения (Interpolation)
- NGO имеет встроенный `NetworkTransform` с интерполяцией

### Task 9.3: Persistence
- Сохранение состояния игры на хосте (сериализация в JSON)
- Загрузка сохранённой игры при перезапуске сессии

### Task 9.4: Permission system
- DM (host) — полный доступ
- Player — базовый доступ
- Observer — только просмотр

---

## Сводка фаз и приоритетов

| Фаза | Описание | Приоритет | Время (оценка) |
|---|---|---|---|
| 1 | Установка NGO + Relay пакетов | 🔴 Critical | 30 мин |
| 2 | Core Networking (NetworkManager, подключения) | 🔴 Critical | 1 час |
| 3 | Lobby System (Host/Join UI) | 🔴 Critical | 2 часа |
| 5 | Networked Dice (физика на клиенте, ClientNetworkTransform) | 🟡 High | 4 часа |
| 7 | Grid + Map Texture (сетка + чанковая передача карт) | 🟡 High | 3 часа |
| 8 | Testing Infrastructure | 🟡 High | 2 часа |
| 9 | Polish (reconnect, persistence) | 🔵 Low | TBD |

**Итого MVP: ~13 часов чистой разработки.**

---

## Что ещё нужно (инструменты)

| Инструмент | Назначение | Статус |
|---|---|---|
| **Unity Relay** | NAT punch-through, интернет-игра | ✅ Твой выбор |
| **Unity Lobby** | Комнаты, коды подключения | ✅ Рекомендую |
| **NGO** (Netcode for GameObjects) | Сетевая синхронизация | ✅ Обязательно |
| **UTP** (Unity Transport) | Транспортный слой | ✅ Ставится с NGO |
| **Multiplayer Play Mode** | Тестирование (2+ Editor) | ✅ Встроен в Unity 6 |
| **Unity Cloud Dashboard** | Управление Relay лимитами | ✅ Бесплатно до 50 CCU |

**НЕ нужно:**
- ❌ Mirror — не интегрируется с Relay
- ❌ Steamworks — $100 + overkill
- ❌ Photon Pun/Fusion — платно после 20 CCU
- ❌ Самостоятельный сервер — Relay всё делает

---

## Риски и открытые вопросы

1. **Физика кубиков через сеть** — решено: NetworkTransform синхронизирует позицию/вращение всем клиентам. Физика только на хосте. Задержка ~50-100 мс приемлема для D&D.
2. **Передача карт** — решено: чанковая передача через CustomMessagingManager. 20 МБ карта = ~5000 чанков по 4 КБ ≈ 5-10 секунд на передачу. Для ускорения можно сжать в JPG quality 70%.
3. **Лимит Relay** — 50 CCU. Для D&D (DM + 8 игроков = 9) — хватает с запасом. Бесплатный тир покрывает полностью.
4. **Отладка** — Unity Transport имеет встроенный Network Simulator для тестирования лагов/потерь пакетов.

---

## Порядок реализации (рекомендованный)

1. **Начинаем с Phase 1-3** — минимальный рабочий лобби (Host/Join через Relay)
2. **Phase 5** — дайсы (физика на клиенте-владельце)
3. **Phase 7** — карта (грид + текстура)
4. **Тестирование между фазами** — каждая фаза тестируется через Multiplayer Play Mode

---

> **План готов. Когда будешь готов начать — скажи, и я начну реализацию по задачам.**
