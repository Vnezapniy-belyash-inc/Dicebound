using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rendering-only local drag. No NetworkObjects, scripts, colliders or rigidbodies are cloned.
/// The actual network transform remains authoritative throughout the gesture.
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class NetworkDragPreview : MonoBehaviour
{
    private sealed class RendererPair
    {
        public MeshRenderer Source;
        public MeshRenderer Copy;
        public bool WasHidden;
    }

    private readonly List<RendererPair> _renderers = new();
    private MaterialPropertyBlock _properties;
    private Transform _visual;
    private bool _awaitingCommit;
    private bool _hasCommit;
    private int _commitTick;
    private Vector3 _commitPosition;
    private Quaternion _rotation;
    private float _deadline;
    private NetworkDragTransform _networkTransform;
    public int Gesture { get; private set; }
    public bool IsActive => _visual != null;
    public bool IsDragging => IsActive && !_awaitingCommit;
    public Vector3 Position => IsActive ? _visual.position : transform.position;
    public Transform DisplayTransform => IsActive ? _visual : transform;

    private void Awake()
    {
        // Native Unity objects cannot be created while MonoBehaviour fields are initialized.
        _properties = new MaterialPropertyBlock();
    }

    public bool Begin(int gesture)
    {
        if (IsDragging) return false;
        Vector3 position = Position;
        Quaternion rotation = DisplayTransform.rotation;
        // A new gesture supersedes a pending completion; reliable server requests stay ordered.
        if (IsActive) Stop();
        Gesture = gesture;
        _awaitingCommit = false;
        _hasCommit = false;
        _deadline = Time.unscaledTime + 5f;
        _networkTransform = GetComponent<NetworkDragTransform>();
        _rotation = rotation;
        var visual = new GameObject(name + " (local drag)");
        _visual = visual.transform;
        _visual.SetPositionAndRotation(position, _rotation);
        _visual.localScale = transform.lossyScale;
        CloneBranch(transform, _visual);
        return true;
    }

    public bool Raycast(Ray ray, int layerMask, out RaycastHit hit)
    {
        hit = default;
        if (!IsActive) return false;
        // Query the existing colliders in their real location using a transformed ray.
        // This makes the visual selectable without adding any local physics objects.
        Matrix4x4 toSource = transform.localToWorldMatrix * _visual.worldToLocalMatrix;
        Matrix4x4 toVisual = _visual.localToWorldMatrix * transform.worldToLocalMatrix;
        Ray sourceRay = new Ray(toSource.MultiplyPoint3x4(ray.origin), toSource.MultiplyVector(ray.direction));
        float nearest = float.PositiveInfinity;
        bool found = false;
        foreach (var collider in GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || !collider.gameObject.activeInHierarchy ||
                (layerMask & (1 << collider.gameObject.layer)) == 0) continue;
            if (!collider.Raycast(sourceRay, out RaycastHit candidate, Mathf.Infinity)) continue;
            Vector3 point = toVisual.MultiplyPoint3x4(candidate.point);
            float distance = Vector3.Dot(point - ray.origin, ray.direction);
            if (distance < 0f || distance >= nearest) continue;
            hit = candidate;
            hit.point = point;
            hit.distance = distance;
            nearest = distance;
            found = true;
        }
        return found;
    }

    private void CloneBranch(Transform source, Transform copy)
    {
        copy.gameObject.layer = source.gameObject.layer;
        var text = source.GetComponent<TextMesh>();
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (text != null)
        {
            var clone = copy.gameObject.AddComponent<TextMesh>();
            clone.text = text.text;
            clone.font = text.font;
            clone.fontSize = text.fontSize;
            clone.fontStyle = text.fontStyle;
            clone.characterSize = text.characterSize;
            clone.anchor = text.anchor;
            clone.alignment = text.alignment;
            clone.lineSpacing = text.lineSpacing;
            clone.richText = text.richText;
            clone.color = text.color;
        }
        else if (filter != null)
            copy.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

        if (renderer != null)
        {
            var clone = copy.GetComponent<MeshRenderer>();
            // Native Unity components use an overloaded null check in the Editor.
            if (clone == null) clone = copy.gameObject.AddComponent<MeshRenderer>();
            clone.sharedMaterials = renderer.sharedMaterials;
            clone.shadowCastingMode = renderer.shadowCastingMode;
            clone.receiveShadows = renderer.receiveShadows;
            clone.lightProbeUsage = renderer.lightProbeUsage;
            clone.reflectionProbeUsage = renderer.reflectionProbeUsage;
            clone.sortingLayerID = renderer.sortingLayerID;
            clone.sortingOrder = renderer.sortingOrder;
            clone.renderingLayerMask = renderer.renderingLayerMask;
            clone.enabled = renderer.enabled && source.gameObject.activeInHierarchy;
            _renderers.Add(new RendererPair { Source = renderer, Copy = clone, WasHidden = renderer.forceRenderingOff });
            clone.forceRenderingOff = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
        }
        // Copy only a visual hierarchy, including labels and portraits, never network behaviours.
        for (int i = 0; i < source.childCount; i++)
        {
            Transform child = source.GetChild(i);
            if (child.GetComponent<Unity.Netcode.NetworkObject>() != null) continue;
            var clone = new GameObject(child.name).transform;
            clone.SetParent(copy, false);
            clone.localPosition = child.localPosition;
            clone.localRotation = child.localRotation;
            clone.localScale = child.localScale;
            CloneBranch(child, clone);
        }
    }

    public void Confirm(int gesture)
    {
        if (IsDragging && Gesture == gesture) _deadline = float.PositiveInfinity;
    }

    public void Reject(int gesture)
    {
        // A terminal authoritative reply wins over a later duplicate rejection.
        if (IsActive && Gesture == gesture && !_hasCommit) Stop();
    }

    public void Move(int gesture, Vector3 position)
    {
        if (!IsDragging || Gesture != gesture) return;
        _visual.SetPositionAndRotation(position, _rotation);
    }

    public void WaitForFinish(int gesture)
    {
        if (!IsActive || Gesture != gesture) return;
        _awaitingCommit = true;
        _deadline = Time.unscaledTime + 5f;
    }

    public void Complete(int gesture, int tick, Vector3 position, Quaternion rotation, bool isServer)
    {
        if (!IsActive || Gesture != gesture) return;
        _awaitingCommit = true;
        _hasCommit = true;
        _commitTick = tick;
        _commitPosition = position;
        _rotation = rotation;
        _visual.SetPositionAndRotation(position, rotation);
        _deadline = Time.unscaledTime + 5f;
        if (isServer) Stop();
    }

    private void LateUpdate()
    {
        if (!IsActive) return;
        if (_hasCommit && _networkTransform != null && _networkTransform.HasCommit(_commitTick, _commitPosition))
        {
            Stop();
            return;
        }
        // NetworkTransform may have changed the real root since Update; the local visual is independent.
        _visual.localScale = transform.lossyScale;
        foreach (var pair in _renderers)
        {
            if (pair.Source == null || pair.Copy == null) continue;
            pair.Copy.enabled = pair.Source.enabled && pair.Source.gameObject.activeInHierarchy;
            pair.Copy.sharedMaterial = pair.Source.sharedMaterial;
            pair.Source.GetPropertyBlock(_properties);
            pair.Copy.SetPropertyBlock(_properties);
        }
    }

    public bool HasTimedOut => IsActive && Time.unscaledTime > _deadline;

    public void Stop()
    {
        foreach (var pair in _renderers)
            if (pair.Source != null) pair.Source.forceRenderingOff = pair.WasHidden;
        _renderers.Clear();
        if (_visual != null)
        {
            _visual.gameObject.SetActive(false);
            Destroy(_visual.gameObject);
        }
        _visual = null;
        _awaitingCommit = false;
        _hasCommit = false;
    }

    private void OnDisable() => Stop();
    private void OnDestroy() => Stop();
}
