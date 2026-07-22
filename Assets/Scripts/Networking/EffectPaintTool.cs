using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Кисть эффектов: выбрать цвет в меню, ЛКМ — закрасить клетку, ПКМ — выключить.
/// </summary>
public class EffectPaintTool : MonoBehaviour
{
    public static EffectPaintTool Instance { get; private set; }

    public bool IsActive { get; private set; }
    public int SelectedTextureIndex { get; private set; }

    public bool IsEraseMode => SelectedTextureIndex == CellMarker.EraseToolIndex;

    private Camera _cam;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        _cam = Camera.main;
    }

    private void Update()
    {
        if (!IsActive) return;
        if (!IsInGame()) return;
        if (!GameplayInputGate.AllowsWorldPointerInput) return;

        var mt = MeasurementTool.Instance;
        if (mt != null && mt.IsLocalActive && mt.CurrentMode != MeasurementTool.Mode.Ruler)
            return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Deactivate();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
            TryPaintAt(mouse);
    }

    public void Activate(int textureIndex)
    {
        SelectedTextureIndex = textureIndex;
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    private static bool IsInGame()
    {
        return GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;
    }

    private void TryPaintAt(Mouse mouse)
    {
        Vector2Int cell = RaycastToCell(mouse);
        if (cell.x < 0) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsServer)
            CellMarker.ServerApplyCell(cell, SelectedTextureIndex, nm.LocalClientId);
        else
            CellMarker.RequestApplyCell(cell, SelectedTextureIndex);
    }

    private Vector2Int RaycastToCell(Mouse mouse)
    {
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return new Vector2Int(-1, -1);

        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
        if (!plane.Raycast(ray, out float dist)) return new Vector2Int(-1, -1);

        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return new Vector2Int(-1, -1);

        return gm.GetGridPosition(ray.GetPoint(dist));
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
