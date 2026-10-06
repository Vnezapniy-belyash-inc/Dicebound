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
    private bool _strokeStartedOnMap;
    private Vector2Int _lastStrokeCell = new(-2, -2);

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
        if (GameplayInputGate.AllowsKeyboardHotkeys &&
            Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            Deactivate();
            return;
        }
        if (!GameplayInputGate.AllowsWorldPointerInput)
        {
            if (Mouse.current?.leftButton.isPressed != true)
            {
                _strokeStartedOnMap = false;
                _lastStrokeCell = new Vector2Int(-2, -2);
            }
            return;
        }

        var mt = MeasurementTool.Instance;
        if (mt != null && mt.IsLocalActive && mt.CurrentMode != MeasurementTool.Mode.Ruler)
            return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            _strokeStartedOnMap = false;
            _lastStrokeCell = new Vector2Int(-2, -2);
        }

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Deactivate();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
            _strokeStartedOnMap = true;
        if (_strokeStartedOnMap && mouse.leftButton.isPressed)
            TryPaintAt(mouse);
    }

    public void Activate(int textureIndex)
    {
        SceneEditor.Instance?.Deactivate();
        FogManager.Instance?.StopManual();
        MeasurementTool.Instance?.Deactivate();
        SelectedTextureIndex = textureIndex;
        IsActive = true;
        _strokeStartedOnMap = false;
        _lastStrokeCell = new Vector2Int(-2, -2);
    }

    public void Deactivate()
    {
        if (IsActive) GameplayInputGate.MarkToolExit();
        IsActive = false;
        _strokeStartedOnMap = false;
    }

    private static bool IsInGame()
    {
        return GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;
    }

    private void TryPaintAt(Mouse mouse)
    {
        Vector2Int cell = RaycastToCell(mouse);
        if (cell == _lastStrokeCell) return;
        _lastStrokeCell = cell;
        if (cell.x < 0)
        {
            DiceUI.Instance?.ShowToolNotice("Выберите клетку внутри карты.");
            return;
        }

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        var gm = FindAnyObjectByType<GridManager>();
        var existing = CellMarker.FindAtCell(cell, gm);
        if (IsEraseMode && existing.Count == 0)
        {
            DiceUI.Instance?.ShowToolNotice("На этой клетке нет отметки для стирания.");
            return;
        }
        if (!nm.IsHost)
        {
            foreach (var marker in existing)
                if (marker.SpawnerClientId != nm.LocalClientId)
                {
                    DiceUI.Instance?.ShowToolNotice("Отметку другого игрока может изменить только GM.");
                    return;
                }
        }

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
        Vector3 point = ray.GetPoint(dist);
        return gm.IsPointOnMap(point) ? gm.GetGridPosition(point) : new Vector2Int(-1, -1);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
