using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

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

    private Camera _cam;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        // Запоминаем текущий поворот для free-режима
        Vector3 euler = transform.rotation.eulerAngles;
        _freeYaw = euler.y;
        _freePitch = euler.x;
    }

    private void Update()
    {
        // Don't move camera when interacting with UI
        if (IsPointerOverUI() || IsEditingText())
            return;

        Keyboard k = Keyboard.current;
        Mouse m = Mouse.current;

        // ── Toggle режима (F1) ──
        if (k != null && k.f1Key.wasPressedThisFrame)
        {
            _freeMode = !_freeMode;
            // При входе в free — синхронизируем углы с текущим поворотом
            if (_freeMode)
            {
                Vector3 e = transform.rotation.eulerAngles;
                _freeYaw = e.y;
                _freePitch = e.x;
            }
        }

        if (_freeMode)
            HandleFreeMode(k, m);
        else
            HandleNormalMode(k, m);
    }

    // ══════════════════════════════════════════════
    //  Normal — top-down
    // ══════════════════════════════════════════════

    void HandleNormalMode(Keyboard k, Mouse m)
    {
        HandleNormalHorizontal(k);
        HandleNormalScroll(m);
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
        if (rmb)
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

    // ═══════════════════ UI-aware helpers ═══════════════════

    static bool IsPointerOverUI()
    {
        return UnityEngine.EventSystems.EventSystem.current != null &&
               UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
    }

    static bool IsEditingText()
    {
        var es = UnityEngine.EventSystems.EventSystem.current;
        if (es == null) return false;
        var go = es.currentSelectedGameObject;
        return go != null && go.GetComponent<InputField>() != null;
    }
}
