using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Unity.Netcode;

/// <summary>
/// Управление камерой — два режима, переключение по F1:
///
///   [Normal — top-down]
///     WASD/стрелки  → движение по XZ
///     Колёсико      → движение по Y (вниз ближе к полу)
///
///   [Free — как в редакторе]
///     WASD          → движение в направлении взгляда
///     Q/E           → вверх/вниз
///     ПКМ + мышь    → вращение камеры (pitch/yaw)
///     Колёсико      → скорость движения ±
///     Shift         → ускорение ×3
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraMovement : MonoBehaviour
{
    public bool IsFreeMode => _freeMode;
    [Header("Normal режим")]
    public float moveSpeed = 10f;
    public float scrollSpeed = 2f;

    [Header("Free режим")]
    public float freeMoveSpeed = 12f;
    public float freeLookSensitivity = 3f;
    public float freeMinPitch = -89f;
    public float freeMaxPitch = 89f;

    // ─── состояние ───
    private bool _freeMode;
    private float _freeYaw;
    private float _freePitch;
    private bool _wasRmbDown;
    private bool _panning;
    private bool _suppressRightLook;
    private Vector3 _panAnchor;
    private Quaternion _normalRotation;

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        _cam.clearFlags = CameraClearFlags.SolidColor;
        _cam.backgroundColor = new Color(0.035f, 0.05f, 0.07f, 1f);
        _normalRotation = transform.rotation;
        // Запоминаем текущий поворот для free-режима
        Vector3 euler = transform.rotation.eulerAngles;
        _freeYaw = euler.y;
        _freePitch = euler.x;
    }

    private void Update()
    {
        if (!GameplayInputGate.AllowsWorldPointerInput)
        {
            _panning = false;
            return;
        }

        Keyboard k = Keyboard.current;
        Mouse m = Mouse.current;

        // ── Toggle режима (F1) ──
        if (k != null && k.f1Key.wasPressedThisFrame)
            ToggleMode();

        if (_freeMode)
            HandleFreeMode(k, m);
        else
            HandleNormalMode(k, m);
    }

    public void ToggleMode()
    {
        _freeMode = !_freeMode;
        if (_freeMode)
        {
            _normalRotation = transform.rotation;
            Vector3 e = transform.rotation.eulerAngles;
            _freeYaw = e.y;
            _freePitch = e.x;
        }
        else
            transform.rotation = _normalRotation;
    }

    // ══════════════════════════════════════════════
    //  Normal — top-down
    // ══════════════════════════════════════════════

    void HandleNormalMode(Keyboard k, Mouse m)
    {
        HandleMousePan(m);
        HandleNormalHorizontal(k);
        HandleNormalScroll(m);
    }

    void HandleMousePan(Mouse m)
    {
        // GM moves the map with the middle button; players pan their own view.
        if (m == null || NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.IsConnectedClient || NetworkManager.Singleton.IsHost)
            return;
        if (m.middleButton.wasPressedThisFrame)
        {
            _panning = TryGetMapPlanePoint(m, out _panAnchor);
        }
        if (m.middleButton.wasReleasedThisFrame)
            _panning = false;
        if (_panning && m.middleButton.isPressed && TryGetMapPlanePoint(m, out Vector3 current))
            transform.position += _panAnchor - current;
    }

    bool TryGetMapPlanePoint(Mouse m, out Vector3 point)
    {
        var ray = _cam.ScreenPointToRay(m.position.ReadValue());
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (plane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }
        point = Vector3.zero;
        return false;
    }

    void HandleNormalHorizontal(Keyboard k)
    {
        if (k == null) return;

        Vector2 input = Vector2.zero;
        if (k.wKey.isPressed || k.upArrowKey.isPressed)    input.y += 1f;
        if (k.sKey.isPressed || k.downArrowKey.isPressed)  input.y -= 1f;
        if (k.dKey.isPressed || k.rightArrowKey.isPressed) input.x += 1f;
        if (k.aKey.isPressed || k.leftArrowKey.isPressed)  input.x -= 1f;

        if (input.sqrMagnitude < 0.0001f) return;
        input.Normalize();

        Vector3 right = transform.right;
        Vector3 forward = Vector3.Cross(transform.right, Vector3.up);
        transform.position += (right * input.x + forward * input.y) * (moveSpeed * Time.deltaTime);
    }

    void HandleNormalScroll(Mouse m)
    {
        if (m == null) return;

        float scroll = m.scroll.ReadValue().y;
        if (Mathf.Approximately(scroll, 0f)) return;

        transform.position += Vector3.down * (scroll * scrollSpeed);
    }

    // ══════════════════════════════════════════════
    //  Free — как Scene View
    // ══════════════════════════════════════════════

    void HandleFreeMode(Keyboard k, Mouse m)
    {
        HandleFreeLook(m);
        HandleFreeMovement(k, m);
    }

    void HandleFreeLook(Mouse m)
    {
        if (m == null) return;

        bool rmb = m.rightButton.isPressed;
        if (GameplayInputGate.LastToolExitFrame == Time.frameCount)
            _suppressRightLook = true;
        if (!rmb)
            _suppressRightLook = false;
        if (rmb && !_suppressRightLook)
        {
            Vector2 delta = m.delta.ReadValue();
            _freeYaw   += delta.x * freeLookSensitivity * 0.1f;
            _freePitch -= delta.y * freeLookSensitivity * 0.1f;
            _freePitch  = Mathf.Clamp(_freePitch, freeMinPitch, freeMaxPitch);
        }
        _wasRmbDown = rmb;

        // Плавно применяем, даже когда ПКМ отпущен
        transform.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);
    }

    void HandleFreeMovement(Keyboard k, Mouse m)
    {
        if (k == null) return;

        Vector3 move = Vector3.zero;

        if (k.wKey.isPressed) move += transform.forward;
        if (k.sKey.isPressed) move -= transform.forward;
        if (k.dKey.isPressed) move += transform.right;
        if (k.aKey.isPressed) move -= transform.right;
        if (k.qKey.isPressed) move += Vector3.up;
        if (k.eKey.isPressed) move -= Vector3.up;

        if (move.sqrMagnitude < 0.0001f) return;

        move.Normalize();
        float speed = freeMoveSpeed;

        // Shift — ускорение
        if (k.shiftKey.isPressed)
            speed *= 3f;

        // Колёсико — регулировка скорости
        if (m != null)
        {
            float scroll = m.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                freeMoveSpeed = Mathf.Max(1f, freeMoveSpeed + scroll * 2f);
            }
        }

        transform.position += move * (speed * Time.deltaTime);
    }

}
