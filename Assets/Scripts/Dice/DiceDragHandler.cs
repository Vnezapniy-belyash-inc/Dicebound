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
    private readonly Dictionary<IDice, Vector3> _dragStartPositions = new();
    private readonly Dictionary<IDice, Vector3> _dragPreviewPositions = new();
    private readonly Dictionary<NetworkDice, int> _diceDragGestures = new();
    private static int _nextDragGesture;
    private float _nextDiceMoveSend;
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
        if (m == null || _cam == null)
        {
            CancelActiveDrag();
            return;
        }

        RemoveRejectedDrags();

        // Блокируем драг только тому, кто сам использует инструмент
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive)
        {
            CancelActiveDrag();
            return;
        }
        if (EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive)
        {
            CancelActiveDrag();
            _areaSelectPending = false;
            _isAreaSelecting = false;
            return;
        }

        if (!GameplayInputGate.AllowsWorldPointerInput || SceneEditor.IsEditing || FogManager.IsManualEditing)
        {
            CancelActiveDrag();
            _areaSelectPending = false;
            _isAreaSelecting = false;
            return;
        }

        bool ctrl = k != null && k.ctrlKey.isPressed;
        if (GameplayInputGate.AllowsKeyboardHotkeys && k?.escapeKey.wasPressedThisFrame == true)
        {
            CancelActiveDrag();
            return;
        }

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
            {
                // Include pointer movement delivered in the same input frame as button release.
                DragAll(m, m.delta.ReadValue().sqrMagnitude > 0f);
                ReleaseAll();
            }
            else if (_draggedToken != null)
            {
                DragToken(m);
                ReleaseToken();
            }
            else if (_areaSelectPending)
                ClearSelection();
        }
    }

    // ═══ Нажатие ═══

    private static readonly int InteractionRaycastMask = Physics.DefaultRaycastLayers;

    void HandlePress(Mouse m, bool ctrl)
    {
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());

        if (TryGetPointerHit(ray, out RaycastHit hit))
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
                if (!IsAlive(d)) continue;
                Vector3 sp = _cam.WorldToScreenPoint(d.transform.position);
                if (sp.z > 0 && screenRect.Contains(new Vector2(sp.x, sp.y)))
                    diceToSelect.Add(d);
            }
        }

        // Сетевые дайсы
        foreach (var nd in FindObjectsByType<NetworkDice>())
        {
            Vector3 sp = _cam.WorldToScreenPoint(nd.DisplayPosition);
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

        var netDice = dice as NetworkDice;
        _selected.Add(dice);
        if (netDice == null) dice.IsRolling = false;
        if (dice.Rigidbody != null && netDice == null)
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
        if (dice is UnityEngine.Object obj && obj == null) return;
        if (dice.Rigidbody != null && dice is not NetworkDice)
            dice.Rigidbody.isKinematic = false;
        var hl = dice.gameObject.GetComponent<DiceHighlight>();
        if (hl != null) hl.SetHighlighted(false);
    }

    void ClearSelection()
    {
        _areaSelectPending = false;
        foreach (var d in _selected)
        {
            if (IsAlive(d) && d.Rigidbody != null)
            {
                if (d is not NetworkDice) d.Rigidbody.isKinematic = false;
                var hl = d.gameObject.GetComponent<DiceHighlight>();
                if (hl != null) hl.SetHighlighted(false);
            }
        }
        _selected.Clear();
        _isDragging = false;
        _diceDragGestures.Clear();
        _dragPreviewPositions.Clear();
    }

    // ═══ Перетаскивание ═══

    void StartDragging(Ray ray)
    {
        _velocity = Vector3.zero;
        _isDragging = true;
        _dragStartPositions.Clear();
        _dragPreviewPositions.Clear();
        _diceDragGestures.Clear();
        _nextDiceMoveSend = 0f;

        foreach (var d in _selected)
        {
            if (IsAlive(d))
            {
                if (d is NetworkDice netDice)
                {
                    int gesture = ++_nextDragGesture;
                    if (!netDice.IsReady || !netDice.BeginDrag(gesture)) continue;
                    _diceDragGestures[netDice] = gesture;
                }
                _dragStartPositions[d] = GetDisplayPosition(d);
                Vector3 pos = GetDisplayPosition(d);
                pos.y = dragHeight;
                _dragPreviewPositions[d] = pos;
                SetDisplayPosition(d, pos);
            }
        }

        // Busy/denied dice are excluded; the accepted part of a group continues dragging.
        for (int i = _selected.Count - 1; i >= 0; i--)
            if (!_dragPreviewPositions.ContainsKey(_selected[i])) RemoveFromSelection(_selected[i]);
        if (_selected.Count == 0)
        {
            _isDragging = false;
            return;
        }

        Plane p = new Plane(Vector3.up, new Vector3(0, dragHeight, 0));
        if (p.Raycast(ray, out float dist))
        {
            _prevMouseWorldPos = ray.GetPoint(dist);
            _dragOffset = GetDragSelectionCenter() - _prevMouseWorldPos;
        }
        else
        {
            _prevMouseWorldPos = Vector3.zero;
            _dragOffset = Vector3.zero;
        }
    }

    void DragAll(Mouse m, bool updateVelocity = true)
    {
        if (_selected.Count == 0) return;

        Plane plane = new Plane(Vector3.up, new Vector3(0, dragHeight, 0));
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
        if (!plane.Raycast(ray, out float dist)) return;

        Vector3 currentMouseWorld = ray.GetPoint(dist);
        Vector3 target = currentMouseWorld + _dragOffset;
        Vector3 center = GetDragSelectionCenter();
        Vector3 delta = target - center;

        foreach (var d in _selected)
        {
            if (IsAlive(d))
            {
                if (d is not NetworkDice && d.Rigidbody != null)
                    d.Rigidbody.isKinematic = true;
                Vector3 pos = _dragPreviewPositions.TryGetValue(d, out Vector3 preview)
                    ? preview + delta : GetDisplayPosition(d) + delta;
                pos.y = dragHeight;
                _dragPreviewPositions[d] = pos;
                SetDisplayPosition(d, pos);
            }
        }

        Vector2 mouseDelta = m.delta.ReadValue();
        float shakeSpeed = mouseDelta.magnitude;
        if (_selected.Count > 1 && shakeSpeed > shakeThreshold)
        {
            float t = Mathf.Clamp01((shakeSpeed - shakeThreshold) / shakeThreshold) * clusterStrength;
            foreach (var d in _selected)
            {
                if (IsAlive(d))
                {
                    Vector3 pos = Vector3.Lerp(_dragPreviewPositions[d], currentMouseWorld, t);
                    pos.y = dragHeight;
                    _dragPreviewPositions[d] = pos;
                    SetDisplayPosition(d, pos);
                }
            }
            SeparateDice();
            foreach (var d in _selected)
                if (IsAlive(d)) _dragPreviewPositions[d] = GetDisplayPosition(d);
        }

        if (Time.unscaledTime >= _nextDiceMoveSend)
        {
            foreach (var pair in _diceDragGestures)
                if (pair.Key != null && _dragPreviewPositions.TryGetValue(pair.Key, out Vector3 pos))
                    pair.Key.MoveDrag(pair.Value, pos);
            _nextDiceMoveSend = Time.unscaledTime + 0.05f;
        }

        if (updateVelocity && Time.deltaTime > 0.0001f)
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

        int localCount = 0;

        foreach (var d in _selected)
        {
            if (!IsAlive(d)) continue;
            if (d is NetworkDice netDice)
            {
                if (_diceDragGestures.TryGetValue(netDice, out int gesture))
                    netDice.ThrowDrag(gesture, _dragPreviewPositions[netDice], throwVelocity,
                        Random.insideUnitSphere * throwSpin);
                continue;
            }
            if (d.Rigidbody != null)
            {
                d.StartRoll();
                d.Rigidbody.isKinematic = false;
                d.Rigidbody.linearVelocity = throwVelocity;
                d.Rigidbody.angularVelocity = Random.insideUnitSphere * throwSpin;
                localCount++;
            }
        }

        _isDragging = false;
        _dragStartPositions.Clear();
        _dragPreviewPositions.Clear();
        _diceDragGestures.Clear();

        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null && localCount > 0) diceUI.StartManualRoll(localCount);
    }

    Vector3 GetSelectionCenter()
    {
        if (_selected.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var d in _selected)
        {
            if (IsAlive(d))
            {
                sum += GetDisplayPosition(d);
                count++;
            }
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    Vector3 GetDragSelectionCenter()
    {
        if (_dragPreviewPositions.Count == 0) return GetSelectionCenter();
        Vector3 sum = Vector3.zero;
        foreach (var pos in _dragPreviewPositions.Values) sum += pos;
        return sum / _dragPreviewPositions.Count;
    }

    void SeparateDice()
    {
        const float minDist = 1.05f;
        for (int i = 0; i < _selected.Count; i++)
        {
            if (!IsAlive(_selected[i]))
                continue;
            for (int j = i + 1; j < _selected.Count; j++)
            {
                if (!IsAlive(_selected[j]))
                    continue;
                Vector3 a = GetDisplayPosition(_selected[i]);
                Vector3 b = GetDisplayPosition(_selected[j]);
                Vector3 dir = b - a;
                float dist = dir.magnitude;
                if (dist < minDist && dist > 0.001f)
                {
                    Vector3 push = dir.normalized * (minDist - dist) * 0.5f;
                    SetDisplayPosition(_selected[i], a - push);
                    SetDisplayPosition(_selected[j], b + push);
                }
            }
        }
    }

    // ═══ Перетаскивание токенов ═══

    private TokenController _draggedToken;
    private Vector3 _tokenDragOffset;
    private Vector3 _tokenStartPos;
    private Vector3 _tokenPendingPos;
    private int _tokenDragSequence;
    private float _nextTokenMoveSend;

    void StartDraggingToken(TokenController token, Ray ray)
    {
        ClearSelection();
        _tokenDragSequence = ++_nextDragGesture;
        if (!token.BeginDrag(_tokenDragSequence)) return;
        _draggedToken = token;
        _tokenStartPos = token.DisplayPosition;
        _tokenPendingPos = _tokenStartPos;
        _tokenDragOffset = Vector3.zero;
        _nextTokenMoveSend = 0f;

        Plane p = new Plane(Vector3.up, new Vector3(0, _tokenStartPos.y, 0));
        if (p.Raycast(ray, out float dist))
        {
            Vector3 hitPoint = ray.GetPoint(dist);
            _tokenDragOffset = _tokenStartPos - hitPoint;
        }
    }

    void DragToken(Mouse m)
    {
        if (_draggedToken == null) return;
        Plane plane = new Plane(Vector3.up, new Vector3(0, _tokenStartPos.y, 0));
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
        if (plane.Raycast(ray, out float dist))
        {
            Vector3 pos = ray.GetPoint(dist) + _tokenDragOffset;
            pos.y = _tokenStartPos.y;
            _tokenPendingPos = pos;
            _draggedToken.SetDragPreview(_tokenDragSequence, pos);
            if (Time.unscaledTime >= _nextTokenMoveSend)
            {
                _draggedToken.MoveDrag(_tokenDragSequence, pos);
                _nextTokenMoveSend = Time.unscaledTime + 0.05f;
            }
        }
    }

    void ReleaseToken()
    {
        if (_draggedToken != null)
        {
            _draggedToken.EndDrag(_tokenDragSequence, _tokenPendingPos);
            _draggedToken = null;
        }
    }

    void CancelActiveDrag()
    {
        if (_draggedToken != null)
        {
            _draggedToken.SetDragPreview(_tokenDragSequence, _tokenStartPos);
            _draggedToken.CancelDrag(_tokenDragSequence, _tokenStartPos);
            _tokenDragSequence++;
            _draggedToken = null;
        }
        if (_isDragging)
        {
            foreach (var pair in _dragStartPositions)
            {
                if (!IsAlive(pair.Key)) continue;
                if (pair.Key is NetworkDice dice &&
                    _diceDragGestures.TryGetValue(dice, out int gesture))
                {
                    dice.SetDragPreview(gesture, pair.Value);
                    dice.CancelDrag(gesture, pair.Value);
                }
                else if (pair.Key is not NetworkDice)
                    pair.Key.transform.position = pair.Value;
            }
        }
        if (_selected.Count > 0) ClearSelection();
        _dragStartPositions.Clear();
        _areaSelectPending = false;
        _isAreaSelecting = false;
    }

    private Vector3 GetDisplayPosition(IDice dice) =>
        dice is NetworkDice networkDice ? networkDice.DisplayPosition : dice.transform.position;

    private bool TryGetPointerHit(Ray ray, out RaycastHit hit)
    {
        Physics.SyncTransforms();
        hit = default;
        float nearest = float.PositiveInfinity;
        bool found = false;
        foreach (var candidate in Physics.RaycastAll(ray, Mathf.Infinity, InteractionRaycastMask))
        {
            var preview = candidate.collider.GetComponentInParent<NetworkDragPreview>();
            if (preview != null && preview.IsActive) continue; // Its hidden server root is elsewhere.
            if (candidate.distance >= nearest) continue;
            hit = candidate;
            nearest = candidate.distance;
            found = true;
        }
        foreach (var preview in FindObjectsByType<NetworkDragPreview>(FindObjectsInactive.Exclude))
        {
            if (!preview.Raycast(ray, InteractionRaycastMask, out RaycastHit candidate) || candidate.distance >= nearest) continue;
            hit = candidate;
            nearest = candidate.distance;
            found = true;
        }
        return found;
    }

    private void SetDisplayPosition(IDice dice, Vector3 position)
    {
        if (dice is NetworkDice networkDice)
        {
            if (_diceDragGestures.TryGetValue(networkDice, out int gesture))
                networkDice.SetDragPreview(gesture, position);
        }
        else dice.transform.position = position;
    }

    private void RemoveRejectedDrags()
    {
        if (_draggedToken != null && !_draggedToken.IsLocalDragActive(_tokenDragSequence))
        {
            _draggedToken = null;
            DiceUI.Instance?.ShowToolNotice("Токен занят другим участником или захват завершён.");
        }
        Vector3 previousCenter = GetDragSelectionCenter();
        bool removed = false;
        for (int i = _selected.Count - 1; i >= 0; i--)
        {
            var dice = _selected[i];
            bool destroyed = dice is UnityEngine.Object obj && obj == null;
            if (!destroyed && (!_isDragging || dice is not NetworkDice nd ||
                (_diceDragGestures.TryGetValue(nd, out int gesture) && nd.IsLocalDragActive(gesture)))) continue;
            _dragStartPositions.Remove(dice);
            _dragPreviewPositions.Remove(dice);
            if (dice is NetworkDice networkDice) _diceDragGestures.Remove(networkDice);
            if (destroyed) _selected.RemoveAt(i);
            else RemoveFromSelection(dice);
            removed = true;
        }
        if (!removed) return;
        if (_selected.Count == 0) _isDragging = false;
        else _dragOffset += GetDragSelectionCenter() - previousCenter;
        DiceUI.Instance?.ShowToolNotice("Часть кубиков занята другим участником или захват завершён.");
    }

    private void OnDisable() => CancelActiveDrag();
    private static bool IsAlive(IDice dice) => dice != null &&
        (dice is not UnityEngine.Object obj || obj != null);
    private void OnApplicationFocus(bool focused)
    {
        if (!focused) CancelActiveDrag();
    }
}
