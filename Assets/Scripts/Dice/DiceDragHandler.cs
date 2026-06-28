using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Перетаскивание дайсов мышкой:
///   ЛКМ по дайсу          — выбрать (сбрасывает предыдущее выделение)
///   Ctrl+ЛКМ по дайсу     — добавить/убрать из выделения
///   ЛКМ по пустоте        — снять выделение
///   Зажать ЛКМ на пустоте  — выделение прямоугольной областью (как в Windows)
///   Ctrl + область        — добавить к выделению
///   Тащить ЛКМ             — двигать все выделенные дайсы
///   Отпустить ЛКМ          — бросить все выделенные с инерцией
/// Выделенные дайсы подсвечиваются голубой обводкой.
/// </summary>
public class DiceDragHandler : MonoBehaviour
{
    [Tooltip("Высота перетаскивания")]
    public float dragHeight = 1.2f;

    [Tooltip("Множитель инерции при броске")]
    public float throwMultiplier = 1.0f;

    [Tooltip("Порог пикселей для начала выделения областью")]
    public float areaThreshold = 5f;

    [Tooltip("Порог скорости мыши (px/кадр) для стряхивания дайсов в кучу")]
    public float shakeThreshold = 25f;

    [Tooltip("Сила притяжения дайсов к центру при тряске")]
    [Range(0f, 1f)]
    public float clusterStrength = 0.15f;

    private readonly List<Dice> _selected = new();
    private bool _isDragging;
    private Vector3 _dragOffset;
    private Vector3 _prevMouseWorldPos;
    private Vector3 _velocity;
    private Camera _cam;

    // Выделение областью
    private bool _areaSelectPending;    // ЛКМ нажат на пустоте, ждём движения
    private bool _isAreaSelecting;      // активно рисуем прямоугольник
    private bool _areaCtrl;             // был ли Ctrl при старте области
    private Vector2 _areaStart;         // экранные координаты начала
    private Vector2 _areaEnd;           // экранные координаты текущего

    // Кеш для OnGUI
    private static Texture2D _whiteTex;
    private static GUIStyle _boxStyle;

    void Start()
    {
        _cam = Camera.main;
    }

    void Update()
    {
        Mouse m = Mouse.current;
        Keyboard k = Keyboard.current;
        if (m == null || _cam == null) return;

        bool ctrl = k != null && k.ctrlKey.isPressed;

        if (m.leftButton.wasPressedThisFrame)
            HandlePress(m, ctrl);

        if (m.leftButton.isPressed)
        {
            if (_isDragging)
                DragAll(m);
            else if (_areaSelectPending)
                CheckAreaStart(m);
            else if (_isAreaSelecting)
                _areaEnd = m.position.ReadValue();
        }

        if (m.leftButton.wasReleasedThisFrame)
        {
            if (_isAreaSelecting)
                FinishAreaSelect();
            else if (_isDragging)
                ReleaseAll();
            else if (_areaSelectPending)
                ClearSelection(); // просто клик по пустоте
        }
    }

    // ═══ Нажатие ═══

    void HandlePress(Mouse m, bool ctrl)
    {
        // Не обрабатываем если курсор над UI
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            Dice dice = hit.collider.GetComponentInParent<Dice>();
            if (dice != null)
            {
                if (ctrl)
                    ToggleSelection(dice, ray);
                else if (_selected.Contains(dice))
                    StartDragging(ray);
                else
                    SelectSingle(dice, ray);
                return;
            }
        }

        // Нажатие на пустом месте — начинаем отслеживание области
        _areaSelectPending = true;
        _areaStart = m.position.ReadValue();
        _areaEnd = _areaStart;
        _areaCtrl = ctrl;
    }

    // ═══ Выделение областью ═══

    void CheckAreaStart(Mouse m)
    {
        Vector2 current = m.position.ReadValue();
        if (Vector2.Distance(current, _areaStart) > areaThreshold)
        {
            _areaSelectPending = false;
            _isAreaSelecting = true;
            _areaEnd = current;
        }
    }

    void FinishAreaSelect()
    {
        _isAreaSelecting = false;

        Rect screenRect = GetAreaScreenRect();
        if (screenRect.width < 2f && screenRect.height < 2f)
            return; // слишком маленькая область

        var diceToSelect = new List<Dice>();
        if (DiceManager.Instance != null)
        {
            foreach (var d in DiceManager.Instance.ActiveDice)
            {
                if (d == null) continue;
                Vector3 sp = _cam.WorldToScreenPoint(d.transform.position);
                if (sp.z > 0 && screenRect.Contains(new Vector2(sp.x, sp.y)))
                    diceToSelect.Add(d);
            }
        }

        if (!_areaCtrl)
            ClearSelection();

        foreach (var d in diceToSelect)
            AddToSelection(d);
    }

    Rect GetAreaScreenRect()
    {
        float xMin = Mathf.Min(_areaStart.x, _areaEnd.x);
        float xMax = Mathf.Max(_areaStart.x, _areaEnd.x);
        float yMin = Mathf.Min(_areaStart.y, _areaEnd.y);
        float yMax = Mathf.Max(_areaStart.y, _areaEnd.y);
        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    void OnGUI()
    {
        if (!_isAreaSelecting) return;

        if (_whiteTex == null) _whiteTex = Texture2D.whiteTexture;
        if (_boxStyle == null) _boxStyle = new GUIStyle(GUI.skin.box);

        Rect screenRect = GetAreaScreenRect();
        // GUI использует Y-вниз (top-left origin), экранные координаты — Y-вверх
        float guiY = Screen.height - screenRect.yMax;
        Rect guiRect = new Rect(screenRect.x, guiY, screenRect.width, screenRect.height);

        if (guiRect.width < 1f || guiRect.height < 1f) return;

        // Полупрозрачная заливка
        GUI.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        GUI.DrawTexture(guiRect, _whiteTex);

        // Рамка
        GUI.color = new Color(0.2f, 0.6f, 1f, 0.7f);
        GUI.Box(guiRect, "", _boxStyle);

        GUI.color = Color.white;
    }

    // ═══ Выделение ═══

    void SelectSingle(Dice dice, Ray ray)
    {
        ClearSelection();
        AddToSelection(dice);
        StartDragging(ray);
    }

    void ToggleSelection(Dice dice, Ray ray)
    {
        if (_selected.Contains(dice))
        {
            RemoveFromSelection(dice);
            if (_selected.Count == 0) return;
        }
        else
        {
            AddToSelection(dice);
        }
        StartDragging(ray);
    }

    void AddToSelection(Dice dice)
    {
        if (_selected.Contains(dice)) return;
        _selected.Add(dice);
        dice.IsRolling = false;
        dice.GetComponent<Rigidbody>().isKinematic = true;
        dice.GetComponent<DiceHighlight>().SetHighlighted(true);
    }

    void RemoveFromSelection(Dice dice)
    {
        _selected.Remove(dice);
        dice.GetComponent<Rigidbody>().isKinematic = false;
        dice.GetComponent<DiceHighlight>().SetHighlighted(false);
    }

    void ClearSelection()
    {
        _areaSelectPending = false;
        foreach (var d in _selected)
        {
            d.GetComponent<Rigidbody>().isKinematic = false;
            d.GetComponent<DiceHighlight>().SetHighlighted(false);
        }
        _selected.Clear();
        _isDragging = false;
    }

    // ═══ Перетаскивание ═══

    void StartDragging(Ray ray)
    {
        // Единая плоскость на dragHeight — совпадает с DragAll, нет скачка
        Plane p = new Plane(Vector3.up, new Vector3(0, dragHeight, 0));
        if (p.Raycast(ray, out float dist))
        {
            _prevMouseWorldPos = ray.GetPoint(dist);
            _dragOffset = GetSelectionCenter() - _prevMouseWorldPos;
        }
        else
        {
            _prevMouseWorldPos = Vector3.zero;
            _dragOffset = Vector3.zero;
        }

        _velocity = Vector3.zero;
        _isDragging = true;
    }

    void DragAll(Mouse m)
    {
        if (_selected.Count == 0) return;

        Plane plane = new Plane(Vector3.up, new Vector3(0, dragHeight, 0));
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 currentMouseWorld = ray.GetPoint(dist);
        Vector3 target = currentMouseWorld + _dragOffset;
        Vector3 center = GetSelectionCenter();
        Vector3 delta = target - center;

        // Двигаем все выделенные дайсы
        foreach (var d in _selected)
            d.transform.position += delta;

        // Стряхивание в кучу: при быстрых движениях мыши стягиваем дайсы к курсору
        Vector2 mouseDelta = m.delta.ReadValue();
        float shakeSpeed = mouseDelta.magnitude;
        if (_selected.Count > 1 && shakeSpeed > shakeThreshold)
        {
            float t = Mathf.Clamp01((shakeSpeed - shakeThreshold) / shakeThreshold) * clusterStrength;
            foreach (var d in _selected)
                d.transform.position = Vector3.Lerp(d.transform.position, currentMouseWorld, t);

            // Не даём дайсам войти друг в друга
            SeparateDice();
        }

        // Инерция
        if (Time.deltaTime > 0.0001f)
            _velocity = (currentMouseWorld - _prevMouseWorldPos) / Time.deltaTime;

        _prevMouseWorldPos = currentMouseWorld;
    }

    void ReleaseAll()
    {
        if (_selected.Count == 0) return;

        Vector3 throwVelocity = _velocity * throwMultiplier;
        throwVelocity.y += 2f;

        foreach (var d in _selected)
        {
            Rigidbody rb = d.GetComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.linearVelocity = throwVelocity;
        }

        _isDragging = false;
    }

    Vector3 GetSelectionCenter()
    {
        if (_selected.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        foreach (var d in _selected) sum += d.transform.position;
        return sum / _selected.Count;
    }

    void SeparateDice()
    {
        const float minDist = 1.05f; // 2 × circumradius + зазор
        for (int i = 0; i < _selected.Count; i++)
        {
            for (int j = i + 1; j < _selected.Count; j++)
            {
                Vector3 a = _selected[i].transform.position;
                Vector3 b = _selected[j].transform.position;
                Vector3 dir = b - a;
                float dist = dir.magnitude;
                if (dist < minDist && dist > 0.001f)
                {
                    Vector3 push = dir.normalized * (minDist - dist) * 0.5f;
                    _selected[i].transform.position -= push;
                    _selected[j].transform.position += push;
                }
            }
        }
    }
}
