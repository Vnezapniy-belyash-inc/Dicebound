using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Локальный визуал одной сессии измерения (линии + превью клеток).
/// </summary>
sealed class MeasurementSessionVisual
{
    private static Material _sharedPreviewMat;

    private readonly float _yOffset;
    private readonly float _sphereSnapTolerance;
    private readonly float _sphereOriginMarkerSize;
    private readonly int _circleSegments;
    private readonly GameObject _root;
    private readonly LineRenderer _lr;
    private readonly LineRenderer _sphereOriginLr;
    private readonly List<GameObject> _previewCells = new();

    private MeasurementSessionVisual(
        GameObject root, LineRenderer lr, LineRenderer sphereOriginLr,
        float yOffset, int circleSegments, float sphereSnapTolerance, float sphereOriginMarkerSize)
    {
        _root = root;
        _lr = lr;
        _sphereOriginLr = sphereOriginLr;
        _yOffset = yOffset;
        _circleSegments = circleSegments;
        _sphereSnapTolerance = sphereSnapTolerance;
        _sphereOriginMarkerSize = sphereOriginMarkerSize;
    }

    public static MeasurementSessionVisual Create(
        Transform parent, float lineWidth, float yOffset, int circleSegments,
        float sphereSnapTolerance, float sphereOriginMarkerSize)
    {
        var root = new GameObject("MeasurementVisual");
        root.transform.SetParent(parent, false);

        var lr = root.AddComponent<LineRenderer>();
        var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        lr.material = mat;
        lr.startWidth = lineWidth;
        lr.endWidth = lineWidth;
        lr.useWorldSpace = true;
        lr.enabled = false;
        lr.positionCount = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;

        var sphereGo = new GameObject("SphereOriginMarker");
        sphereGo.transform.SetParent(root.transform, false);
        var sphereLr = sphereGo.AddComponent<LineRenderer>();
        sphereLr.material = mat;
        sphereLr.startWidth = lineWidth * 1.2f;
        sphereLr.endWidth = lineWidth * 1.2f;
        sphereLr.useWorldSpace = true;
        sphereLr.enabled = false;
        sphereLr.positionCount = 0;
        sphereLr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        sphereLr.receiveShadows = false;

        return new MeasurementSessionVisual(root, lr, sphereLr, yOffset, circleSegments,
            sphereSnapTolerance, sphereOriginMarkerSize);
    }

    public void Apply(MeasurementSnapshot snap)
    {
        if (!snap.Active || !snap.HasPoints)
        {
            Hide();
            return;
        }

        Color c = new Color(snap.ColorRgb.x, snap.ColorRgb.y, snap.ColorRgb.z, 0.85f);
        _lr.material.color = c;
        _lr.startColor = c;
        _lr.endColor = c;
        _sphereOriginLr.material.color = c;
        _sphereOriginLr.startColor = c;
        _sphereOriginLr.endColor = c;

        _lr.enabled = true;
        var mode = (MeasurementTool.Mode)snap.Mode;
        Vector3 a = snap.PointA;
        Vector3 b = snap.PointB;
        a.y = _yOffset;
        b.y = _yOffset;

        switch (mode)
        {
            case MeasurementTool.Mode.Ruler:
                _lr.positionCount = 2;
                _lr.SetPosition(0, a);
                _lr.SetPosition(1, b);
                _sphereOriginLr.enabled = false;
                ClearPreviews();
                break;

            case MeasurementTool.Mode.Circle:
                DrawCircle(a, Vector3.Distance(a, b));
                DrawSphereOriginMarker(a);
                UpdatePreviews(snap);
                break;

            case MeasurementTool.Mode.Square:
                _sphereOriginLr.enabled = false;
                DrawSquare(a, b);
                UpdatePreviews(snap);
                break;

            case MeasurementTool.Mode.Cone:
                _sphereOriginLr.enabled = false;
                DrawCone(a, b);
                UpdatePreviews(snap);
                break;
        }
    }

    public void Hide()
    {
        ClearPreviews();
        if (_lr != null)
        {
            _lr.enabled = false;
            _lr.positionCount = 0;
        }
        if (_sphereOriginLr != null)
        {
            _sphereOriginLr.enabled = false;
            _sphereOriginLr.positionCount = 0;
        }
    }

    public void Destroy()
    {
        ClearPreviews();
        if (_root != null)
            Object.Destroy(_root);
    }

    private void DrawCircle(Vector3 center, float radius)
    {
        _lr.positionCount = _circleSegments + 1;
        for (int i = 0; i <= _circleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / _circleSegments;
            float x = center.x + Mathf.Cos(angle) * radius;
            float z = center.z + Mathf.Sin(angle) * radius;
            _lr.SetPosition(i, new Vector3(x, _yOffset, z));
        }
    }

    private void DrawSphereOriginMarker(Vector3 origin)
    {
        var gm = Object.FindAnyObjectByType<GridManager>();
        bool isIntersection = GridManager.IsIntersectionPosition(origin, gm);
        float s = _sphereOriginMarkerSize;

        _sphereOriginLr.enabled = true;
        if (isIntersection)
        {
            _sphereOriginLr.positionCount = 5;
            _sphereOriginLr.SetPosition(0, new Vector3(origin.x, _yOffset, origin.z + s));
            _sphereOriginLr.SetPosition(1, new Vector3(origin.x + s, _yOffset, origin.z));
            _sphereOriginLr.SetPosition(2, new Vector3(origin.x, _yOffset, origin.z - s));
            _sphereOriginLr.SetPosition(3, new Vector3(origin.x - s, _yOffset, origin.z));
            _sphereOriginLr.SetPosition(4, new Vector3(origin.x, _yOffset, origin.z + s));
        }
        else
        {
            _sphereOriginLr.positionCount = 5;
            _sphereOriginLr.SetPosition(0, new Vector3(origin.x - s, _yOffset, origin.z - s));
            _sphereOriginLr.SetPosition(1, new Vector3(origin.x + s, _yOffset, origin.z - s));
            _sphereOriginLr.SetPosition(2, new Vector3(origin.x + s, _yOffset, origin.z + s));
            _sphereOriginLr.SetPosition(3, new Vector3(origin.x - s, _yOffset, origin.z + s));
            _sphereOriginLr.SetPosition(4, new Vector3(origin.x - s, _yOffset, origin.z - s));
        }
    }

    private void DrawSquare(Vector3 corner, Vector3 opposite)
    {
        _lr.positionCount = 5;
        float halfCell = 0.5f;
        var gm = Object.FindAnyObjectByType<GridManager>();
        if (gm != null) halfCell = gm.CellSize / 2f;

        float minX = Mathf.Min(corner.x, opposite.x) - halfCell;
        float maxX = Mathf.Max(corner.x, opposite.x) + halfCell;
        float minZ = Mathf.Min(corner.z, opposite.z) - halfCell;
        float maxZ = Mathf.Max(corner.z, opposite.z) + halfCell;

        _lr.SetPosition(0, new Vector3(minX, _yOffset, minZ));
        _lr.SetPosition(1, new Vector3(maxX, _yOffset, minZ));
        _lr.SetPosition(2, new Vector3(maxX, _yOffset, maxZ));
        _lr.SetPosition(3, new Vector3(minX, _yOffset, maxZ));
        _lr.SetPosition(4, new Vector3(minX, _yOffset, minZ));
    }

    private void DrawCone(Vector3 origin, Vector3 target)
    {
        Vector3 dir = target - origin;
        float length = dir.magnitude;
        if (length < 0.01f) { _lr.positionCount = 0; return; }
        dir /= length;

        Vector3 perp = new Vector3(-dir.z, 0, dir.x).normalized;
        float halfWidth = length * 0.5f;

        Vector3 o = new Vector3(origin.x, _yOffset, origin.z);
        Vector3 r = o + dir * length + perp * halfWidth;
        Vector3 l = o + dir * length - perp * halfWidth;

        _lr.positionCount = 4;
        _lr.SetPosition(0, o);
        _lr.SetPosition(1, r);
        _lr.SetPosition(2, l);
        _lr.SetPosition(3, o);
    }

    private void UpdatePreviews(MeasurementSnapshot snap)
    {
        var cells = MeasurementTool.GetCellsForSnapshot(snap);
        var gm = Object.FindAnyObjectByType<GridManager>();
        if (gm == null) return;

        while (_previewCells.Count > cells.Count)
        {
            var go = _previewCells[_previewCells.Count - 1];
            _previewCells.RemoveAt(_previewCells.Count - 1);
            if (go != null) Object.Destroy(go);
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 pos = gm.GetCellCenter(cells[i].x, cells[i].y, _yOffset);
            if (i < _previewCells.Count)
            {
                _previewCells[i].transform.position = pos;
            }
            else
            {
                _previewCells.Add(CreatePreviewQuad(pos));
            }
        }
    }

    private GameObject CreatePreviewQuad(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "PreviewCell";
        go.transform.SetParent(_root.transform);
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        var collider = go.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);

        if (_sharedPreviewMat == null)
        {
            _sharedPreviewMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            _sharedPreviewMat.SetFloat("_Surface", 1f);
            _sharedPreviewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _sharedPreviewMat.SetInt("_SrcBlend", 5);
            _sharedPreviewMat.SetInt("_DstBlend", 10);
            _sharedPreviewMat.SetInt("_ZWrite", 0);
            _sharedPreviewMat.renderQueue = 3000;
        }

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = _sharedPreviewMat;
        mr.material.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.AddComponent<PreviewBlinker>();
        return go;
    }

    private void ClearPreviews()
    {
        foreach (var go in _previewCells)
        {
            if (go != null) Object.Destroy(go);
        }
        _previewCells.Clear();
    }
}
