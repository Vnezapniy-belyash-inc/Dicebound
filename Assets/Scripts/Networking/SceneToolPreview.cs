using UnityEngine;
using UnityEngine.InputSystem;

public sealed partial class SceneEditor
{
    private LineRenderer _toolPreview;
    private Material _toolPreviewMaterial;
    private void UpdateToolPreview()
    {
        bool active = IsMaster && (IsEditing || FogManager.IsManualEditing) && _grid != null
            && Camera.main != null && Mouse.current != null && GameplayInputGate.AllowsWorldPointerInput;
        if (_toolPreview != null) _toolPreview.gameObject.SetActive(active);
        if (!active) return;
        var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (!new Plane(Vector3.up, Vector3.zero).Raycast(ray, out float d)) return;
        Vector3 world = ray.GetPoint(d);
        if (!_grid.IsPointOnMap(world)) { if (_toolPreview != null) _toolPreview.gameObject.SetActive(false); return; }
        if (_toolPreview == null)
        {
            _toolPreview = new GameObject("Active tool preview").AddComponent<LineRenderer>();
            _toolPreviewMaterial = new Material(Resources.Load<Shader>("DiceboundMarkup"));
            _toolPreview.sharedMaterial = _toolPreviewMaterial;
            _toolPreview.useWorldSpace = true;
            _toolPreview.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        bool erase = ActiveTool == Tool.EraseEdge || ActiveTool == Tool.EraseObstacle || FogManager.Instance?.ManualMode == 2;
        _toolPreviewMaterial.color = erase ? new Color(1, 0.35f, 0.25f) : new Color(0.3f, 1, 0.7f);
        _toolPreview.startWidth = _toolPreview.endWidth = _grid.CellSize * 0.075f;
        Vector3 point = _grid.WorldToGridCoordinates(world);
        bool area = FogManager.IsManualEditing || ActiveTool >= Tool.Square;
        _toolPreview.loop = area;
        if (!area)
        {
            bool vertical = Mathf.Abs(point.x - Mathf.Round(point.x)) < Mathf.Abs(point.z - Mathf.Round(point.z));
            int x = Mathf.Clamp(vertical ? Mathf.RoundToInt(point.x) : Mathf.FloorToInt(point.x), 0, vertical ? _grid.Width : _grid.Width - 1);
            int y = Mathf.Clamp(vertical ? Mathf.FloorToInt(point.z) : Mathf.RoundToInt(point.z), 0, vertical ? _grid.Height - 1 : _grid.Height);
            _toolPreview.positionCount = 2;
            _toolPreview.SetPosition(0, _grid.GridCoordinatesToWorld(new Vector3(x, 0.09f, y)));
            _toolPreview.SetPosition(1, _grid.GridCoordinatesToWorld(new Vector3(x + (vertical ? 0 : 1), 0.09f, y + (vertical ? 1 : 0))));
        }
        else
        {
            int x = Mathf.Clamp(Mathf.FloorToInt(point.x), 0, _grid.Width - 1), y = Mathf.Clamp(Mathf.FloorToInt(point.z), 0, _grid.Height - 1);
            bool round = ActiveTool == Tool.Column && !FogManager.IsManualEditing;
            _toolPreview.positionCount = round ? 32 : 4;
            for (int i = 0; i < _toolPreview.positionCount; i++)
            {
                Vector3 p = round ? new Vector3(x + .5f + Mathf.Cos(i * Mathf.PI / 16) * ColumnDiameter / 2, .09f,
                    y + .5f + Mathf.Sin(i * Mathf.PI / 16) * ColumnDiameter / 2)
                    : new Vector3(x + (i == 1 || i == 2 ? 1 : 0), .09f, y + (i >= 2 ? 1 : 0));
                _toolPreview.SetPosition(i, _grid.GridCoordinatesToWorld(p));
            }
        }
    }
}
