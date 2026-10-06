using System;
using NUnit.Framework;
using UnityEngine;

public class DiceboundTokenLabelTests
{
    private GameObject _cameraObject;
    private GameObject _token;
    private Camera _camera;

    [SetUp]
    public void SetUp()
    {
        _cameraObject = new GameObject("Label camera", typeof(Camera));
        _camera = _cameraObject.GetComponent<Camera>();
        _camera.pixelRect = new Rect(0, 0, 800, 600);
        _camera.orthographic = true;
        _camera.orthographicSize = 5;
        _camera.transform.position = new Vector3(0, 10, -10);
        _camera.transform.LookAt(Vector3.zero);
        _token = new GameObject("Display token");
    }

    private Rect Project()
    {
        var arguments = new object[] { _camera, _token.transform,
            new Bounds(Vector3.zero, new Vector3(1, 0.15f, 1)), 600f, null };
        Assert.That((bool)Type.GetType("TokenLabelLayout, Assembly-CSharp", true)
            .GetMethod("Project").Invoke(null, arguments), Is.True);
        return (Rect)arguments[4];
    }

    [Test]
    public void ZoomScalesWholeLabelAndOffsetTogetherWithToken()
    {
        Rect near = Project();
        float centerY = 600 - _camera.WorldToScreenPoint(Vector3.zero).y;
        _camera.orthographicSize *= 2;
        Rect far = Project();
        Assert.That(far.width, Is.EqualTo(near.width / 2).Within(0.001f));
        Assert.That(far.height, Is.EqualTo(near.height / 2).Within(0.001f));
        Assert.That(far.y - centerY, Is.EqualTo((near.y - centerY) / 2).Within(0.001f));
        Assert.That(far.center.x, Is.EqualTo(near.center.x).Within(0.001f));
    }

    [Test]
    public void LabelWidthMatchesTokenAtNearAndDistantZoom()
    {
        foreach (float zoom in new[] { 2f, 5f, 20f, 100f })
        {
            _camera.orthographicSize = zoom;
            Rect label = Project();
            float tokenWidth = _camera.WorldToScreenPoint(Vector3.right * 0.5f).x -
                _camera.WorldToScreenPoint(Vector3.left * 0.5f).x;
            Assert.That(label.width, Is.EqualTo(tokenWidth).Within(0.001f));
        }
    }

    [Test]
    public void PlateStaysInsideOwnCellAtBottomEdge()
    {
        var method = Type.GetType("TokenLabelLayout, Assembly-CSharp", true).GetMethod("PlaceInsideCell");
        foreach (float scale in new[] { 0.25f, 1f, 4f })
        {
            Rect cell = new Rect(100, 200, 100 * scale, 100 * scale);
            Rect original = new Rect(100 + 5 * scale, cell.yMax + 10 * scale, 90 * scale, 12 * scale);
            Rect placed = (Rect)method.Invoke(null, new object[] { original, cell });
            Assert.That(placed.size, Is.EqualTo(original.size));
            Assert.That(placed.yMax, Is.LessThan(cell.yMax));
            Assert.That(cell.yMax - placed.yMax, Is.EqualTo(scale).Within(0.001f));
            Assert.That(placed.yMin, Is.GreaterThanOrEqualTo(cell.yMin));
            Assert.That(placed.xMin, Is.GreaterThanOrEqualTo(cell.xMin));
            Assert.That(placed.xMax, Is.LessThanOrEqualTo(cell.xMax));
        }
    }

    [Test]
    public void FootprintFollowsFractionalMovementAcrossCellBoundary()
    {
        var gridType = Type.GetType("GridManager, Assembly-CSharp", true);
        // Keep the grid inactive so its scene-building callbacks do not run in this geometry test.
        var gridObject = new GameObject("Label grid");
        gridObject.SetActive(false);
        try
        {
            var grid = gridObject.AddComponent(gridType);
            gridType.GetField("cellSize").SetValue(grid, 1f);
            var method = gridType.GetMethod("GetTokenFootprintCorner");
            Vector3 previous = (Vector3)method.Invoke(grid, new object[] { new Vector3(0.49f, 0.25f, 0), 0 });
            foreach (float x in new[] { 0.5f, 0.51f, 0.52f })
            {
                Vector3 current = (Vector3)method.Invoke(grid, new object[] { new Vector3(x, 0.25f, 0), 0 });
                Assert.That(current.x - previous.x, Is.EqualTo(0.01f).Within(0.00001f));
                Assert.That(current.y, Is.EqualTo(0));
                previous = current;
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(gridObject); }
    }

    [Test]
    public void LabelTracksDisplayTokenMovementWithoutChangingSize()
    {
        Rect first = Project();
        _token.transform.position += Vector3.right * 2;
        Rect moved = Project();
        float projectedMovement = _camera.WorldToScreenPoint(Vector3.right * 2).x -
            _camera.WorldToScreenPoint(Vector3.zero).x;
        Assert.That(moved.center.x - first.center.x, Is.EqualTo(projectedMovement).Within(0.001f));
        Assert.That(moved.y, Is.EqualTo(first.y).Within(0.001f));
        Assert.That(moved.size, Is.EqualTo(first.size));
    }

    [TearDown]
    public void TearDown()
    {
        UnityEngine.Object.DestroyImmediate(_token);
        UnityEngine.Object.DestroyImmediate(_cameraObject);
    }
}
