using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Пинги: Alt+Click по карте → подсветка клетки цветом игрока с мерцанием.
/// Видно всем, исчезает через 2.5 секунды.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class PingManager : NetworkBehaviour
{
    [Header("Visual")]
    public float yOffset = 0.01f;
    public float duration = 2.5f;
    public float blinkInterval = 0.2f;

    private Camera _cam;

    private void Start()
    {
        _cam = Camera.main;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    private void OnServerStarted()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
            netObj.Spawn();
    }

    private void Update()
    {
        var k = Keyboard.current;
        var m = Mouse.current;
        if (k == null || m == null || _cam == null) return;

        // Alt+ЛКМ
        if (k.altKey.isPressed && m.leftButton.wasPressedThisFrame)
        {
            if (!GameplayInputGate.AllowsWorldPointerInput) return;

            Vector3? hit = RaycastGrid(m);
            if (hit.HasValue)
            {
                Vector3 pos = SnapToCell(hit.Value);
                if (IsServer)
                    SpawnPing(pos, NetworkManager.Singleton.LocalClientId);
                else
                    RequestPingServerRpc(pos);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestPingServerRpc(Vector3 pos, RpcParams rpcParams = default)
    {
        SpawnPing(pos, rpcParams.Receive.SenderClientId);
    }

    private void SpawnPing(Vector3 pos, ulong clientId)
    {
        // Создаём временный Quad
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"Ping_{clientId}";
        go.transform.position = new Vector3(pos.x, yOffset, pos.z);
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mr.material.SetFloat("_Surface", 1f);
        mr.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mr.material.SetInt("_SrcBlend", 5);
        mr.material.SetInt("_DstBlend", 10);
        mr.material.SetInt("_ZWrite", 0);
        mr.material.renderQueue = 3030; // Placement pings remain above the unexplored-map cover.
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Color c = PlayerRegistry.GetServerPlayerColor(clientId);
        mr.material.color = c;

        // Запускаем мерцание + автоуничтожение
        var blinker = go.AddComponent<PingBlinker>();
        blinker.Init(c, duration, blinkInterval);

        // Синхронизируем: шлём RPC всем (сервер уже создал локально)
        if (IsServer)
            ShowPingClientRpc(pos, new Vector3(c.r, c.g, c.b));
    }

    [Rpc(SendTo.NotServer)]
    private void ShowPingClientRpc(Vector3 pos, Vector3 rgb)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Ping";
        go.transform.position = new Vector3(pos.x, yOffset, pos.z);
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mr.material.SetFloat("_Surface", 1f);
        mr.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mr.material.SetInt("_SrcBlend", 5);
        mr.material.SetInt("_DstBlend", 10);
        mr.material.SetInt("_ZWrite", 0);
        mr.material.renderQueue = 3030;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Color c = new Color(rgb.x, rgb.y, rgb.z, 1f);
        mr.material.color = c;

        var blinker = go.AddComponent<PingBlinker>();
        blinker.Init(c, duration, blinkInterval);
    }

    private Vector3? RaycastGrid(Mouse mouse)
    {
        if (_cam == null) return null;
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return null;
    }

    private Vector3 SnapToCell(Vector3 pos)
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return pos;
        Vector2Int cell = gm.GetGridPosition(pos);
        if (cell.x < 0) return pos;
        return gm.GetCellCenter(cell.x, cell.y, yOffset);
    }

    private new void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }
}

/// <summary>
/// Компонент мерцания: циклично меняет альфу и самоуничтожается через N секунд.
/// </summary>
public class PingBlinker : MonoBehaviour
{
    private Color _baseColor;
    private float _duration;
    private float _interval;
    private float _elapsed;
    private Material _mat;

    public void Init(Color baseColor, float duration, float interval)
    {
        _baseColor = baseColor;
        _duration = duration;
        _interval = interval;
        _mat = GetComponent<MeshRenderer>().material;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= _duration)
        {
            Destroy(gameObject);
            return;
        }

        // Мерцание: sin даёт плавное изменение
        float alpha = 0.3f + 0.6f * Mathf.Abs(Mathf.Sin(_elapsed * Mathf.PI / _interval));
        _mat.color = new Color(_baseColor.r, _baseColor.g, _baseColor.b, alpha);
    }
}
