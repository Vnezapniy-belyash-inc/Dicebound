using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundStabilityTests
{
    private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    [Test]
    public void AreaAndEffectMaterialsRenderAfterMapWithFogClipping()
    {
        var type = RuntimeType("WorldOverlayMaterial");
        int last = 3010;
        foreach (var field in new[] { "EffectsQueue", "PreviewQueue", "MeasurementQueue" })
        {
            int queue = (int)type.GetField(field).GetValue(null);
            var material = (Material)type.GetMethod("Create").Invoke(null, new object[] { queue });
            try
            {
                Assert.That(queue, Is.GreaterThan(last).And.LessThan(3020));
                Assert.That(material.renderQueue, Is.EqualTo(queue));
                Assert.That(material.shader.name, Is.EqualTo("Dicebound/Markup"));
                Assert.That(material.HasProperty("_FogMask"), Is.True);
                Assert.That(material.GetFloat("_FogEnabled"), Is.EqualTo(1), "Uninitialised fog must conceal overlays.");
            }
            finally { UnityEngine.Object.DestroyImmediate(material); }
            last = queue;
        }
    }

    [Test]
    public void PreviewBlinkUsesPropertyBlockWithoutCloningMaterial()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        var material = new Material(Resources.Load<Shader>("DiceboundMarkup"));
        try
        {
            var renderer = go.GetComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            var type = RuntimeType("PreviewBlinker"); var blink = go.AddComponent(type);
            type.GetMethod("Start", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(blink, null);
            type.GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(blink, null);
            Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
            Assert.That(block.GetColor("_Color").a, Is.InRange(0.15f, 0.5f));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(material); }
    }

    [Test]
    public void CachedGuiStyleDetectsDestroyedSceneTextures()
    {
        var method = RuntimeType("VttUiSkin").GetMethod("HasGuiBackgrounds", BindingFlags.Static | BindingFlags.NonPublic);
        var style = new GUIStyle();
        var normal = new Texture2D(2, 2); var hover = new Texture2D(2, 2); var active = new Texture2D(2, 2);
        try
        {
            style.normal.background = normal; style.hover.background = hover; style.active.background = active;
            Assert.That(method.Invoke(null, new object[] { style }), Is.EqualTo(true));
            UnityEngine.Object.DestroyImmediate(hover);
            Assert.That(method.Invoke(null, new object[] { style }), Is.EqualTo(false), "Managed style must not mask a destroyed Unity resource.");
            Assert.That(method.Invoke(null, new object[] { null }), Is.EqualTo(false));
        }
        finally
        {
            if (normal != null) UnityEngine.Object.DestroyImmediate(normal);
            if (hover != null) UnityEngine.Object.DestroyImmediate(hover);
            if (active != null) UnityEngine.Object.DestroyImmediate(active);
        }
    }

    [Test]
    public void RoundedUiCacheRecreatesDestroyedResourcesAndSurvivesSceneCleanup()
    {
        var method = RuntimeType("VttUiSkin").GetMethod("Rounded", BindingFlags.Static | BindingFlags.NonPublic);
        var first = (Sprite)method.Invoke(null, new object[] { 29 });
        Sprite rebuilt = null;
        try
        {
            Assert.That((first.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0, Is.True);
            Assert.That((first.texture.hideFlags & HideFlags.DontUnloadUnusedAsset) != 0, Is.True);
            UnityEngine.Object.DestroyImmediate(first.texture);
            rebuilt = (Sprite)method.Invoke(null, new object[] { 29 });
            Assert.That(rebuilt, Is.Not.SameAs(first));
            Assert.That(rebuilt.texture, Is.Not.Null);
            Assert.That(method.Invoke(null, new object[] { 29 }), Is.SameAs(rebuilt), "Valid cache must not allocate again.");
        }
        finally
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first);
            if (rebuilt != null) { if (rebuilt.texture != null) UnityEngine.Object.DestroyImmediate(rebuilt.texture); UnityEngine.Object.DestroyImmediate(rebuilt); }
        }
    }

    [Test]
    public void GridCoversMovedLargeMapWithThreeCellMarginAndStableDrawOrder()
    {
        var root = new GameObject("Grid coverage test");
        try
        {
            var grid = root.AddComponent(RuntimeType("GridManager"));
            grid.GetType().GetField("createWalls").SetValue(grid, false);
            grid.GetType().GetMethod("SetBounds", new[] { typeof(Bounds), typeof(Quaternion) }).Invoke(grid,
                new object[] { new Bounds(Vector3.zero, new Vector3(100, 0.1f, 100)), Quaternion.identity });
            Assert.That(grid.GetType().GetField("visibleMarginCells").GetValue(grid), Is.EqualTo(3));
            void Check(Bounds map)
            {
                grid.GetType().GetMethod("SetVisualBounds").Invoke(grid, new object[] { map });
                float minX = Mathf.Floor(map.min.x) - 3, maxX = Mathf.Ceil(map.max.x) + 3;
                float minZ = Mathf.Floor(map.min.z) - 3, maxZ = Mathf.Ceil(map.max.z) + 3;
                int horizontal = 0, vertical = 0;
                foreach (var line in root.GetComponentsInChildren<LineRenderer>())
                {
                    var a = line.transform.TransformPoint(line.GetPosition(0)); var b = line.transform.TransformPoint(line.GetPosition(1));
                    Assert.That(line.sharedMaterial.renderQueue, Is.EqualTo(3005));
                    if (Mathf.Abs(a.z - b.z) < 0.001f)
                    { horizontal++; Assert.That(a.x, Is.EqualTo(minX)); Assert.That(b.x, Is.EqualTo(maxX)); }
                    else
                    { vertical++; Assert.That(a.z, Is.EqualTo(minZ)); Assert.That(b.z, Is.EqualTo(maxZ)); }
                }
                Assert.That(horizontal, Is.EqualTo((int)(maxZ - minZ) + 1));
                Assert.That(vertical, Is.EqualTo((int)(maxX - minX) + 1));
            }
            Check(new Bounds(new Vector3(80, 0, -20), new Vector3(120, 0.1f, 80)));
            Check(new Bounds(new Vector3(-42.4f, 0, 14.6f), new Vector3(13.2f, 0.1f, 9.1f)));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    [Test]
    public void OpaqueMapCanUseJpegWhenPreparedPngExceedsTransferBudget()
    {
        var texture = new Texture2D(512, 512, TextureFormat.RGBA32, false);
        var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            var random = new System.Random(17); var pixels = new Color32[512 * 512];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            texture.SetPixels32(pixels); texture.Apply();
            const int budget = 128 * 1024;
            Assert.That(texture.EncodeToPNG().Length, Is.GreaterThan(budget));
            var encoded = (byte[])RuntimeType("MapTextureUtility").GetMethod("TryJpegWithinBudget").Invoke(null, new object[] { texture, budget });
            Assert.That(encoded, Is.Not.Null); Assert.That(encoded.Length, Is.LessThanOrEqualTo(budget));
            Assert.That(decoded.LoadImage(encoded), Is.True); Assert.That(decoded.width, Is.EqualTo(texture.width));
            pixels[0].a = 0; texture.SetPixels32(pixels); texture.Apply();
            Assert.That(RuntimeType("MapTextureUtility").GetMethod("TryJpegWithinBudget").Invoke(null, new object[] { texture, budget }), Is.Null, "Transparent maps must retain PNG alpha.");
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); UnityEngine.Object.DestroyImmediate(decoded); }
    }
    [Test]
    public void SparseMaximumFogGridUsesSmallPacketsAndRoundTrips()
    {
        var visible = new bool[256 * 256 * 16]; var explored = new bool[visible.Length];
        visible[42] = explored[42] = explored[explored.Length - 1] = true;
        var type = RuntimeType("FogStateCodec");
        var payload = (byte[])type.GetMethod("Encode").Invoke(null, new object[] { visible, explored });
        Assert.That(payload.Length, Is.LessThan(4096));
        var args = new object[] { payload, visible.Length, null, null };
        type.GetMethod("Decode").Invoke(null, args);
        Assert.That(args[2], Is.EqualTo(visible)); Assert.That(args[3], Is.EqualTo(explored));
        Assert.Throws<TargetInvocationException>(() => type.GetMethod("Decode").Invoke(null, new object[] { payload, visible.Length / 2, null, null }));
    }
    [Test]
    public void FasterHostFramesCannotFloodTheTransferQueue()
    {
        int Run(int fps)
        {
            var type = RuntimeType("NetworkTransferBudget"); type.GetMethod("Reset").Invoke(null, null);
            var consume = type.GetMethod("TryConsumeAt"); int total = 0;
            for (int frame = 0; frame < fps; frame++)
                while ((bool)consume.Invoke(null, new object[] { 1UL, 1000, (double)frame / fps, frame })) total += 1000;
            return total;
        }
        int slow = Run(30), fast = Run(1000);
        Assert.That(slow, Is.GreaterThan(100 * 1024));
        Assert.That(fast, Is.LessThanOrEqualTo(134 * 1024));
        Assert.That(Math.Abs(fast - slow), Is.LessThan(12 * 1024));
    }
    [Test]
    public void LargeMapKeepsAspectAndPixelsWithBoundedWorkingTexture()
    {
        var source = new Texture2D(8192, 8, TextureFormat.RGB24, false);
        Texture2D resized = null;
        try
        {
            var colors = new Color32[8192 * 8];
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color32(30, 80, 150, 255);
            source.SetPixels32(colors); source.Apply();
            resized = (Texture2D)RuntimeType("MapTextureUtility").GetMethod("Resize").Invoke(null, new object[] { source });
            Assert.That(resized.width, Is.EqualTo(4096)); Assert.That(resized.height, Is.EqualTo(4));
            Assert.That(resized.mipmapCount, Is.EqualTo(1)); Assert.That(source.width, Is.EqualTo(8192));
            Assert.That((Color32)resized.GetPixel(200, 2), Is.EqualTo(new Color32(30, 80, 150, 255)));
        }
        finally { UnityEngine.Object.DestroyImmediate(source); if (resized != null) UnityEngine.Object.DestroyImmediate(resized); }
    }
}
