using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class DiceboundTokenAppearanceTests
{
    private GameObject _root;
    private Material _material;
    private Texture2D _portrait;
    private object _appearance;
    private Type _type;

    [UnityTest]
    public IEnumerator HiddenTokenDisablesAllPlayerRenderersAndPickingAndRestoresThem()
    {
        yield return new EnterPlayMode();
        CreateToken();
        var portrait = GameObject.CreatePrimitive(PrimitiveType.Quad);
        portrait.transform.SetParent(_root.transform, false);
        // Also preserve a collider deliberately disabled in the prefab.
        portrait.GetComponent<Collider>().enabled = false;
        _appearance = Activator.CreateInstance(_type, new object[] { _root.transform });
        Apply(true, false);
        foreach (var renderer in _root.GetComponentsInChildren<Renderer>())
            Assert.That(renderer.enabled, Is.False);
        foreach (var collider in _root.GetComponentsInChildren<Collider>())
            Assert.That(collider.enabled, Is.False);
        Assert.That(_root.activeSelf, Is.True, "Networking must remain active while hidden.");
        Physics.SyncTransforms();
        foreach (var hit in Physics.RaycastAll(new Ray(Vector3.up * 10, Vector3.down), 20))
            Assert.That(hit.collider.transform.IsChildOf(_root.transform), Is.False);
        Apply(false, false);
        Assert.That(_root.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(_root.GetComponent<Collider>().enabled, Is.True);
        Assert.That(portrait.GetComponent<Collider>().enabled, Is.False);
        yield return null;
    }

    [UnityTest]
    public IEnumerator GameMasterTransparencyPreservesPortraitAndRestoresOriginalMaterials()
    {
        yield return new EnterPlayMode();
        CreateToken();
        _appearance = Activator.CreateInstance(_type, new object[] { _root.transform });
        var renderer = _root.GetComponent<Renderer>();
        string colorProperty = _material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
        _material.SetColor(colorProperty, Color.white);
        Apply(true, true);
        var faded = renderer.sharedMaterial;
        Assert.That(renderer.enabled, Is.True);
        Assert.That(_root.GetComponent<Collider>().enabled, Is.True);
        Assert.That(faded.GetColor(colorProperty).a, Is.EqualTo(0.35f).Within(0.001f));
        Assert.That(faded.renderQueue, Is.EqualTo(3000));
        Apply(true, true);
        Assert.That(renderer.sharedMaterial, Is.SameAs(faded), "Reuse per-token material variants.");
        Assert.That(faded.GetColor(colorProperty).a, Is.EqualTo(0.35f).Within(0.001f));
        _portrait = new Texture2D(2, 2);
        _material.mainTexture = _portrait;
        Apply(true, true);
        Assert.That(renderer.sharedMaterial.mainTexture, Is.SameAs(_portrait), "Portrait changes preserve hiding.");
        Apply(false, true);
        Assert.That(renderer.sharedMaterial, Is.SameAs(_material));
        Assert.That(_material.GetColor(colorProperty).a, Is.EqualTo(1));
        yield return null;
    }

    private void CreateToken()
    {
        _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Unlit/Transparent");
        Assert.That(shader, Is.Not.Null);
        _material = new Material(shader);
        _root.GetComponent<Renderer>().sharedMaterial = _material;
        _type = Type.GetType("TokenAppearance, Assembly-CSharp", true);
    }

    private void Apply(bool hidden, bool gm) =>
        _type.GetMethod("Apply").Invoke(_appearance, new object[] { hidden, gm });

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (_appearance != null) _type.GetMethod("Dispose").Invoke(_appearance, null);
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_material != null) UnityEngine.Object.Destroy(_material);
        if (_portrait != null) UnityEngine.Object.Destroy(_portrait);
        _appearance = null;
        yield return null;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
