using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Перетаскивание дайсов мышкой. Работает с IDice (и локальные Dice, и сетевые NetworkDice).
///   ЛКМ по дайсу          — выбрать (сбрасывает предыдущее выделение)
///   Ctrl+ЛКМ по дайсу     — добавить/убрать из выделения
///   ЛКМ по пустоте        — снять выделение
///   Зажать ЛКМ на пустоте  — выделение прямоугольной областью
///   Ctrl + область        — добавить к выделению
///   Тащить ЛКМ             — двигать все выделенные дайсы
///   Отпустить ЛКМ          — бросить все выделенные с инерцией
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

    [Tooltip("Сила случайного вращения при броске дайса")]
    [Range(0f, 20f)]
    public float throwSpin = 5f;

    [Tooltip("Максимальная скорость броска")]
    public float maxThrowSpeed = 30f;

    private readonly List<IDice> _selected = new();
    private bool _isDragging;
    private Vector3 _dragOffset;
    private Vector3 _prevMouseWorldPos;
    private Vector3 _velocity;
    private Camera _cam;

    // Выделение областью
    private bool _areaSelectPending;
    private bool _isAreaSelecting;
    private bool _areaCtrl;
    private Vector2 _areaStart;
    private Vector2 _areaEnd;

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

        // Блокируем драг только тому, кто сам использует инструмент
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive)
            return;

        if (!GameplayInputGate.AllowsWorldPointerInput)
        {
            if (_isDragging) ReleaseAll();
            _areaSelectPending = false;
            _isAreaSelecting = false;
            return;
        }

        bool ctrl = k != null && k.ctrlKey.isPressed;

        if (m.leftButton.wasPressedThisFrame)
            HandlePress(m, ctrl);

        if (m.leftButton.isPressed)
        {
            if (_isDragging)
                DragAll(m);
            else if (_draggedToken != null)
                DragToken(m);
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
            else if (_draggedToken != null)
                ReleaseToken();
            else if (_areaSelectPending)
                ClearSelection();
        }
    }

    // ═══ Нажатие ═══

    private static readonly int InteractionRaycastMask = Physics.DefaultRaycastLayers;

    void HandlePress(Mouse m, bool ctrl)
    {
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());

        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, InteractionRaycastMask))
        {
            // Ищем IDice — сначала Dice, потом NetworkDice
            IDice dice = hit.collider.GetComponentInParent<Dice>();
            if (dice == null)
                dice = hit.collider.GetComponentInParent<NetworkDice>();

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

            // Ищем TokenController
            TokenController token = hit.collider.GetComponentInParent<TokenController>();
            if (token != null)
            {
                StartDraggingToken(token, ray);
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
            return;

        var diceToSelect = new List<IDice>();

        // Локальные дайсы
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

        // Сетевые дайсы
        foreach (var nd in FindObjectsByType<NetworkDice>())
        {
            Vector3 sp = _cam.WorldToScreenPoint(nd.transform.position);
            if (sp.z > 0 && screenRect.Contains(new Vector2(sp.x, sp.y)))
                diceToSelect.Add(nd);
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
        if (!GameplayInputGate.AllowsImGuiOverlays) return;
        if (!_isAreaSelecting) return;

        if (_whiteTex == null) _whiteTex = Texture2D.whiteTexture;
        if (_boxStyle == null) _boxStyle = new GUIStyle(GUI.skin.box);

        Rect screenRect = GetAreaScreenRect();
        float guiY = Screen.height - screenRect.yMax;
        Rect guiRect = new Rect(screenRect.x, guiY, screenRect.width, screenRect.height);

        if (guiRect.width < 1f || guiRect.height < 1f) return;

        GUI.color = new Color(0.2f, 0.6f, 1f, 0.15f);
        GUI.DrawTexture(guiRect, _whiteTex);

        GUI.color = new Color(0.2f, 0.6f, 1f, 0.7f);
        GUI.Box(guiRect, "", _boxStyle);

        GUI.color = Color.white;
    }

    // ═══ Выделение ═══

    void SelectSingle(IDice dice, Ray ray)
    {
        ClearSelection();
        AddToSelection(dice);
        StartDragging(ray);
    }

    void ToggleSelection(IDice dice, Ray ray)
    {
        if (_selected.Contains(dice))
            RemoveFromSelection(dice);
        else
            AddToSelection(dice);
    }

    void AddToSelection(IDice dice)
    {
        if (dice == null || _selected.Contains(dice)) return;

        // Если это сетевой кубик и мы не владелец — запрашиваем владение
        var netDice = dice as NetworkDice;
        if (netDice != null && !netDice.IsOwner)
            netDice.RequestOwnership();

        _selected.Add(dice);
        dice.IsRolling = false;
        if (dice.Rigidbody != null)
        {
            if (!dice.Rigidbody.isKinematic)
            {
                dice.Rigidbody.linearVelocity = Vector3.zero;
                dice.Rigidbody.angularVelocity = Vector3.zero;
            }
            dice.Rigidbody.isKinematic = true;
        }
        var hl = dice.gameObject.GetComponent<DiceHighlight>();
        if (hl != null) hl.SetHighlighted(true);
    }

    void RemoveFromSelection(IDice dice)
    {
        if (dice == null) return;
        _selected.Remove(dice);
        if (dice.Rigidbody != null) dice.Rigidbody.isKinematic = false;
        var hl = dice.gameObject.GetComponent<DiceHighlight>();
        if (hl != null) hl.SetHighlighted(false);
    }

    void ClearSelection()
    {
        _areaSelectPending = false;
        foreach (var d in _selected)
        {
            if (d != null && d.Rigidbody != null)
            {
                d.Rigidbody.isKinematic = false;
                var hl = d.gameObject.GetComponent<DiceHighlight>();
                if (hl != null) hl.SetHighlighted(false);
            }
        }
        _selected.Clear();
        _isDragging = false;
    }

    // ═══ Перетаскивание ═══

    void StartDragging(Ray ray)
    {
        _velocity = Vector3.zero;
        _isDragging = true;

        foreach (var d in _selected)
        {
            if (d != null)
            {
                Vector3 pos = d.transform.position;
                pos.y = dragHeight;
                d.transform.position = pos;
            }
        }

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

        foreach (var d in _selected)
        {
            if (d != null)
            {
                Vector3 pos = d.transform.position + delta;
                pos.y = dragHeight;
                d.transform.position = pos;
            }
        }

        Vector2 mouseDelta = m.delta.ReadValue();
        float shakeSpeed = mouseDelta.magnitude;
        if (_selected.Count > 1 && shakeSpeed > shakeThreshold)
        {
            float t = Mathf.Clamp01((shakeSpeed - shakeThreshold) / shakeThreshold) * clusterStrength;
            foreach (var d in _selected)
            {
                if (d != null)
                {
                    Vector3 pos = Vector3.Lerp(d.transform.position, currentMouseWorld, t);
                    pos.y = dragHeight;
                    d.transform.position = pos;
                }
            }
            SeparateDice();
        }

        if (Time.deltaTime > 0.0001f)
            _velocity = (currentMouseWorld - _prevMouseWorldPos) / Time.deltaTime;

        _prevMouseWorldPos = currentMouseWorld;
    }

    void ReleaseAll()
    {
        if (_selected.Count == 0) return;

        Vector3 throwVelocity = _velocity * throwMultiplier;
        throwVelocity.y += 2f;

        if (throwVelocity.magnitude > maxThrowSpeed)
            throwVelocity = throwVelocity.normalized * maxThrowSpeed;

        int manualCount = 0;

        foreach (var d in _selected)
        {
            if (d == null) continue;
            if (d.Rigidbody != null)
            {
                d.StartRoll();
                d.Rigidbody.isKinematic = false;
                d.Rigidbody.linearVelocity = throwVelocity;
                d.Rigidbody.angularVelocity = Random.insideUnitSphere * throwSpin;
                manualCount++;
            }
        }

        _isDragging = false;

        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null) diceUI.StartManualRoll(manualCount);
    }

    Vector3 GetSelectionCenter()
    {
        if (_selected.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var d in _selected)
        {
            if (d != null)
            {
                sum += d.transform.position;
                count++;
            }
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    void SeparateDice()
    {
        const float minDist = 1.05f;
        for (int i = 0; i < _selected.Count; i++)
        {
            if (_selected[i] == null) continue;
            for (int j = i + 1; j < _selected.Count; j++)
            {
                if (_selected[j] == null) continue;
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

    // ═══ Перетаскивание токенов ═══

    private TokenController _draggedToken;
    private Vector3 _tokenDragOffset;
    private Vector3 _tokenStartPos;

    void StartDraggingToken(TokenController token, Ray ray)
    {
        ClearSelection();
        token.RequestOwnership();
        _draggedToken = token;
        _tokenStartPos = token.transform.position;

        Plane p = new Plane(Vector3.up, new Vector3(0, token.transform.position.y, 0));
        if (p.Raycast(ray, out float dist))
        {
            Vector3 hitPoint = ray.GetPoint(dist);
            _tokenDragOffset = token.transform.position - hitPoint;
        }
    }

    void DragToken(Mouse m)
    {
        if (_draggedToken == null) return;
        Plane plane = new Plane(Vector3.up, new Vector3(0, _draggedToken.transform.position.y, 0));
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
        if (plane.Raycast(ray, out float dist))
        {
            Vector3 pos = ray.GetPoint(dist) + _tokenDragOffset;
            pos.y = _tokenStartPos.y;
            _draggedToken.transform.position = pos;
            _draggedToken.SnapToGrid();
        }
    }

    void ReleaseToken()
    {
        if (_draggedToken != null)
        {
            _draggedToken.SnapToGrid();
            _draggedToken = null;
        }
    }
}
