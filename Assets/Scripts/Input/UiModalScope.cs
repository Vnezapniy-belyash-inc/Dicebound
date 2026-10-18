using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Owns keyboard focus and gameplay input for a uGUI dialog, including nested dialogs.</summary>
[DefaultExecutionOrder(-9000)]
public sealed class UiModalScope : MonoBehaviour
{
    private static readonly List<UiModalScope> Open = new();
    public static bool IsOpen => Open.Count > 0;
    public Selectable initialSelection;
    public System.Action dismiss;
    private GameObject _previousSelection;

    /// <summary>Wrap an existing centered dialog without replacing its controls or callbacks.</summary>
    public static GameObject Wrap(GameObject dialog, Selectable initial)
    {
        var root = new GameObject(dialog.name + "Modal", typeof(RectTransform), typeof(Canvas),
            typeof(GraphicRaycaster), typeof(Image));
        root.SetActive(false);
        root.transform.SetParent(dialog.transform.parent, false);
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var canvas = root.GetComponent<Canvas>();
        canvas.overrideSorting = true; canvas.sortingOrder = 31900;
        root.GetComponent<Image>().color = new Color(0.01f, 0.02f, 0.04f, 0.65f);
        dialog.transform.SetParent(root.transform, false);
        dialog.SetActive(true);
        root.AddComponent<UiModalScope>().initialSelection = initial;
        return root;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() => Open.Clear();

    private void OnEnable()
    {
        Open.Add(this);
        var events = EventSystem.current;
        _previousSelection = events != null ? events.currentSelectedGameObject : null;
        Focus();
    }

    private void Focus()
    {
        if (EventSystem.current == null) return;
        var target = initialSelection != null && initialSelection.IsActive() && initialSelection.IsInteractable()
            ? initialSelection : System.Array.Find(GetComponentsInChildren<Selectable>(), s => s.IsActive() && s.IsInteractable());
        EventSystem.current.SetSelectedGameObject(target != null ? target.gameObject : null);
    }

    private void Update()
    {
        if (Open.Count == 0 || Open[Open.Count - 1] != this) return;
        // File picker and IMGUI dialogs own their own focus while layered over this dialog.
        if (SimpleFileBrowser.FileBrowser.IsOpen || DisplaySettingsUI.IsOpen || MovementReviewUI.IsOpen
            || PublicReferenceLibrary.IsOpen) return;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null || !selected.transform.IsChildOf(transform)) Focus();
        if (!GameplayInputGate.IsTextInputFocused && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        { if (dismiss != null) dismiss(); else gameObject.SetActive(false); }
    }

    private void OnDisable()
    {
        bool wasTop = Open.Count > 0 && Open[Open.Count - 1] == this;
        Open.Remove(this);
        GameplayInputGate.MarkToolExit();
        if (!wasTop || EventSystem.current == null) return;
        var selectable = _previousSelection != null ? _previousSelection.GetComponent<Selectable>() : null;
        if (selectable != null && selectable.IsActive() && selectable.IsInteractable()
            && (Open.Count == 0 || selectable.transform.IsChildOf(Open[Open.Count - 1].transform)))
            EventSystem.current.SetSelectedGameObject(_previousSelection);
        else if (Open.Count > 0) Open[Open.Count - 1].Focus();
        else EventSystem.current.SetSelectedGameObject(null);
    }
}
