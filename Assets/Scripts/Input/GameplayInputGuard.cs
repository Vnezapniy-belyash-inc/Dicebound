using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Consumes modal close frames before world Update handlers and separates IMGUI/uGUI focus.</summary>
[DefaultExecutionOrder(-10000)]
public sealed class GameplayInputGuard : MonoBehaviour
{
    private bool _wasModal, _wasImGui;
    private GameObject _previousSelection;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<GameplayInputGuard>() != null) return;
        var root = new GameObject("GameplayInputGuard"); DontDestroyOnLoad(root);
        root.AddComponent<GameplayInputGuard>();
    }

    private void Update()
    {
        bool modal = GameplayInputGate.IsModalOpen;
        if (modal || _wasModal) GameplayInputGate.MarkToolExit();
        _wasModal = modal;
        bool imgui = DisplaySettingsUI.IsOpen || MovementReviewUI.IsOpen || PublicReferenceLibrary.IsOpen;
        var events = EventSystem.current;
        if (events != null)
        {
            if (imgui && !_wasImGui) _previousSelection = events.currentSelectedGameObject;
            // IMGUI text fields do not use the EventSystem; an old uGUI selection could still submit behind them.
            if (imgui) events.SetSelectedGameObject(null);
            else if (_wasImGui && !modal)
            {
                var selectable = _previousSelection != null ? _previousSelection.GetComponent<Selectable>() : null;
                if (selectable != null && selectable.IsActive() && selectable.IsInteractable())
                    events.SetSelectedGameObject(_previousSelection);
                _previousSelection = null;
            }
        }
        _wasImGui = imgui;
    }
}
