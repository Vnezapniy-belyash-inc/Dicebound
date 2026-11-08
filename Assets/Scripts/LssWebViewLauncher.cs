#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

internal sealed class LssWebViewLauncher : MonoBehaviour, IPointerDownHandler, IPointerUpHandler,
    IPointerMoveHandler, IScrollHandler, IDragHandler
{
    private const int Width = 1200;
    private const int MaxViewportHeight = 900;
    private const float PageScale = .7f;
    private const float ViewportInset = 10f;
    private const float ViewportTop = 52f;
    private static readonly Vector2 ViewportInsets = new(2 * ViewportInset, ViewportTop + ViewportInset);
    private static readonly JsonSerializerSettings WireJson = new()
    {
        StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
    };
    private RawImage image;
    private Text status;
    private Texture2D texture;
    private BrowserSession session;
    private Keyboard keyboard;
    private int mouseButtons;
    private float nextPointerMove;
    private RectTransform window;
    private RectTransform header;
    private RectTransform pinButton;
    private UiModalScope modalScope;
    private UnityEngine.UI.Image backdrop;
    private Canvas windowCanvas;
    private int modalSortOrder;
    private LssWindowGrip[] grips;
    private Vector2Int desiredViewport;
    private Vector2Int requestedViewport;
    private float resizeAfter;
    internal bool IsPinned => modalScope != null && !modalScope.enabled;
    internal bool HasPinnedKeyboardFocus => IsPinned && isActiveAndEnabled &&
        EventSystem.current?.currentSelectedGameObject == gameObject;

    internal static GameObject Create(Transform parent)
    {
        var dialog = AtlasUi.Panel(parent, "Long Story Short", AtlasUi.Paper);
        dialog.anchorMin = dialog.anchorMax = dialog.pivot = new Vector2(.5f, .5f);
        dialog.sizeDelta = (new Vector2(1120, 780) - ViewportInsets) * PageScale + ViewportInsets;
        var header = AtlasUi.Panel(dialog, "Header", AtlasUi.Forest);
        header.anchorMin = new Vector2(0, 1);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(.5f, 1);
        header.sizeDelta = new Vector2(0, 46);
        LssWindowGrip.Attach(header, dialog, Vector2Int.zero);

        var viewport = AtlasUi.Node(dialog, "LSS viewport");
        AtlasUi.Fill(viewport, ViewportInset, ViewportTop, ViewportInset, ViewportInset);
        var surface = AtlasUi.Node(viewport, "LSS web surface");
        AtlasUi.Fill(surface);
        var browser = surface.gameObject.AddComponent<LssWebViewLauncher>();
        browser.window = dialog;
        browser.header = header;
        browser.image = surface.gameObject.AddComponent<RawImage>();
        var selection = surface.gameObject.AddComponent<Selectable>();
        selection.navigation = new Navigation { mode = Navigation.Mode.None };
        browser.status = AtlasUi.Text(surface, "", 18);
        AtlasUi.Fill(browser.status.rectTransform, 14, 0, 14, 0);
        browser.status.alignment = TextAnchor.MiddleCenter;

        var modal = UiModalScope.Wrap(dialog.gameObject, selection);
        browser.modalScope = modal.GetComponent<UiModalScope>();
        browser.backdrop = modal.GetComponent<UnityEngine.UI.Image>();
        browser.windowCanvas = modal.GetComponent<Canvas>();
        browser.modalSortOrder = browser.windowCanvas.sortingOrder;
        var back = IconButton("Назад", "arrow-left", false, 10, () => Navigate("GoBack"));
        var forward = IconButton("Вперёд", "arrow-right", false, 50, () => Navigate("GoForward"));
        var close = AtlasUi.CloseButton(header, () => modal.SetActive(false), dark: true);
        close.gameObject.name = "Закрыть";
        PositionButton((RectTransform)close.transform, true, -10);
        var pin = IconButton("Закрепить", "pin", true, -50, null);
        pin.onClick.AddListener(TogglePin);
        browser.pinButton = (RectTransform)pin.transform;
        back.navigation = forward.navigation = pin.navigation = close.navigation = new Navigation { mode = Navigation.Mode.None };
        LssWindowGrip.AddResizeEdges(dialog);
        browser.grips = dialog.GetComponentsInChildren<LssWindowGrip>(true);
        return modal;

        void TogglePin()
        {
            bool pinned = !browser.IsPinned;
            browser.SetPinned(pinned);
            pin.gameObject.name = pinned ? "Открепить" : "Закрепить";
            pin.GetComponentInChildren<AtlasGlyph>().color = pinned ? new Color(1f, .79f, .34f) : AtlasUi.Light;
        }

        void Navigate(string command)
        {
            browser.Send(command, new { });
            EventSystem.current?.SetSelectedGameObject(surface.gameObject);
        }

        UnityEngine.UI.Button IconButton(string label, string icon, bool right, float x, UnityEngine.Events.UnityAction action)
        {
            var button = AtlasUi.Button(header, "", action, dark: true);
            button.gameObject.name = label;
            PositionButton((RectTransform)button.transform, right, x);
            var iconRect = AtlasUi.Node(button.transform, "Icon");
            AtlasUi.Fill(iconRect, 7, 7, 7, 7);
            var glyph = iconRect.gameObject.AddComponent<AtlasGlyph>();
            glyph.key = icon;
            glyph.color = AtlasUi.Light;
            glyph.raycastTarget = false;
            return button;
        }

        static void PositionButton(RectTransform rect, bool right, float x)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(right ? 1 : 0, 1);
            rect.anchoredPosition = new Vector2(x, -6);
            rect.sizeDelta = new Vector2(34, 34);
        }
    }

    private void SetPinned(bool pinned)
    {
        modalScope.enabled = !pinned;
        backdrop.enabled = !pinned;
        windowCanvas.sortingOrder = pinned ? 60 : modalSortOrder;
        foreach (var grip in grips)
            grip.enabled = !pinned;
    }

    internal bool RecenterIfControlsAreHidden()
    {
        var container = (RectTransform)window.parent;
        if (container.rect.width <= 0 || container.rect.height <= 0)
            return false;

        var control = IsPinned ? pinButton : header;
        var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(container, control);
        Rect visible = container.rect;
        float width = Mathf.Min(visible.xMax, bounds.max.x) - Mathf.Max(visible.xMin, bounds.min.x);
        float height = Mathf.Min(visible.yMax, bounds.max.y) - Mathf.Max(visible.yMin, bounds.min.y);
        if (width >= (IsPinned ? 18 : 110) && height >= 18)
            return false;

        Vector2 remaining = (visible.size - window.sizeDelta) * .5f;
        window.anchoredPosition = Vector2.Min(Vector2.zero, remaining);
        return true;
    }

    private void OnEnable()
    {
        if (image == null)
            return;

        image.color = AtlasUi.Paper;
        status.text = "Открываем Long Story Short…";
        requestedViewport = GetBrowserViewport();
        desiredViewport = requestedViewport;
        try
        {
            string executable = Path.Combine(Application.streamingAssetsPath, "LssWebView", "LssWebView.exe");
            string arguments = FormattableString.Invariant($"--texture {requestedViewport.x} {requestedViewport.y}");
            var info = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };
            session = new BrowserSession(info);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            status.text = "Не удалось запустить компонент Long Story Short.";
            return;
        }

        keyboard = Keyboard.current;
        if (keyboard != null)
            keyboard.onTextInput += Type;
    }

    private void OnDisable()
    {
        if (keyboard != null)
            keyboard.onTextInput -= Type;
        keyboard = null;
        session?.Commands.CompleteAdding();
        session = null;
        mouseButtons = 0;
        nextPointerMove = 0;
        if (image != null)
            image.texture = null;
        if (texture != null)
            Destroy(texture);
        texture = null;
    }

    private void Update()
    {
        if (session == null)
            return;

        UpdateViewport();
        string error = Interlocked.Exchange(ref session.Error, null);
        if (error != null)
            status.text = error;
        var frame = Interlocked.Exchange(ref session.Frame, null);
        if (frame != null)
        {
            if (texture == null)
                texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (texture.LoadImage(frame))
            {
                image.texture = texture;
                image.color = Color.white;
                if (error == null)
                    status.text = "";
            }
        }

        if (keyboard == null || EventSystem.current?.currentSelectedGameObject != gameObject)
            return;

        Key(keyboard.backspaceKey, "Backspace", 8);
        Key(keyboard.tabKey, "Tab", 9);
        Key(keyboard.enterKey, "Enter", 13);
        Key(keyboard.deleteKey, "Delete", 46);
        Key(keyboard.leftArrowKey, "ArrowLeft", 37);
        Key(keyboard.upArrowKey, "ArrowUp", 38);
        Key(keyboard.rightArrowKey, "ArrowRight", 39);
        Key(keyboard.downArrowKey, "ArrowDown", 40);
        Key(keyboard.homeKey, "Home", 36);
        Key(keyboard.endKey, "End", 35);
        if (IsPinned)
            Key(keyboard.escapeKey, "Escape", 27);
        if (keyboard.ctrlKey.isPressed)
        {
            Key(keyboard.aKey, "a", 65);
            if (keyboard.vKey.wasPressedThisFrame)
                InsertText(GUIUtility.systemCopyBuffer);
        }
    }

    private void UpdateViewport()
    {
        var target = GetBrowserViewport();
        if (target != desiredViewport)
        {
            desiredViewport = target;
            resizeAfter = Time.unscaledTime + .12f;
        }

        if (target != requestedViewport && Time.unscaledTime >= resizeAfter &&
            Send("ResizeViewport", new { width = target.x, height = target.y }))
            requestedViewport = target;
    }

    private Vector2Int GetBrowserViewport()
    {
        var size = Vector2.Max(Vector2.one, (window.sizeDelta - ViewportInsets) / PageScale);
        float scale = Mathf.Min(1f, Mathf.Min(Width / size.x, MaxViewportHeight / size.y));
        return new Vector2Int(Mathf.Max(1, Mathf.RoundToInt(size.x * scale)),
            Mathf.Max(1, Mathf.RoundToInt(size.y * scale)));
    }

    private int Modifiers => keyboard == null ? 0 :
        (keyboard.altKey.isPressed ? 1 : 0) | (keyboard.ctrlKey.isPressed ? 2 : 0) |
        (keyboard.shiftKey.isPressed ? 8 : 0);

    private void Key(KeyControl control, string key, int code)
    {
        if (!control.wasPressedThisFrame)
            return;
        Send("Input.dispatchKeyEvent", new { type = "rawKeyDown", key, windowsVirtualKeyCode = code, modifiers = Modifiers });
        Send("Input.dispatchKeyEvent", new { type = "keyUp", key, windowsVirtualKeyCode = code, modifiers = Modifiers });
    }

    private void Type(char character)
    {
        if (!char.IsControl(character) && EventSystem.current?.currentSelectedGameObject == gameObject)
            InsertText(character.ToString());
    }

    private void InsertText(string value)
    {
        if (!string.IsNullOrEmpty(value))
            Send("Input.insertText", new { text = value });
    }

    public void OnPointerDown(PointerEventData e)
    {
        EventSystem.current?.SetSelectedGameObject(gameObject);
        mouseButtons |= ButtonMask(e);
        Mouse("mousePressed", e, ButtonName(e), Mathf.Max(1, e.clickCount));
    }

    public void OnPointerUp(PointerEventData e)
    {
        mouseButtons &= ~ButtonMask(e);
        Mouse("mouseReleased", e, ButtonName(e), Mathf.Max(1, e.clickCount));
    }

    public void OnPointerMove(PointerEventData e) => MovePointer(e);
    public void OnDrag(PointerEventData e) => MovePointer(e);
    public void OnScroll(PointerEventData e) => Mouse("mouseWheel", e);
    private static int ButtonMask(PointerEventData e) => e.button == PointerEventData.InputButton.Right ? 2 : 1;
    private static string ButtonName(PointerEventData e) => e.button == PointerEventData.InputButton.Right ? "right" : "left";

    private void MovePointer(PointerEventData e)
    {
        if (session == null || session.Commands.Count != 0 || Time.unscaledTime < nextPointerMove)
            return;

        nextPointerMove = Time.unscaledTime + 1f / 30f;
        Mouse("mouseMoved", e);
    }

    private void Mouse(string type, PointerEventData e, string button = "none", int clickCount = 0)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(image.rectTransform, e.position, e.pressEventCamera, out var point))
            return;
        var rect = image.rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0)
            return;
        if (texture == null)
            return;
        float x = Mathf.Clamp01((point.x - rect.xMin) / rect.width) * (texture.width - 1);
        float y = Mathf.Clamp01((rect.yMax - point.y) / rect.height) * (texture.height - 1);
        Send("Input.dispatchMouseEvent", new
        {
            type, x, y, button, clickCount, buttons = mouseButtons, modifiers = Modifiers,
            deltaX = e.scrollDelta.x * 50, deltaY = -e.scrollDelta.y * 50
        });
    }

    private bool Send(string method, object payload)
    {
        return session != null && session.Commands.TryAdd(JsonConvert.SerializeObject(new { method, payload }, WireJson));
    }

    private sealed class BrowserSession
    {
        internal readonly BlockingCollection<string> Commands = new(96);
        internal byte[] Frame;
        internal string Error;

        internal BrowserSession(ProcessStartInfo info)
        {
            var process = Process.Start(info);
            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Interlocked.Exchange(ref Frame, Convert.FromBase64String(e.Data));
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Interlocked.Exchange(ref Error, e.Data);
            };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = Task.Run(() =>
            {
                using (process)
                {
                    try
                    {
                        using var input = process.StandardInput;
                        input.AutoFlush = true;
                        foreach (string command in Commands.GetConsumingEnumerable())
                            input.WriteLine(command);
                    }
                    catch (IOException)
                    {
                        Interlocked.Exchange(ref Error, "Long Story Short отключился. Откройте лист заново.");
                    }
                    if (!process.WaitForExit(3000))
                        process.Kill();
                }
            });
        }
    }
}
#endif
