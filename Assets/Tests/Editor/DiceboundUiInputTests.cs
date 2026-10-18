using System;
using System.Reflection;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.TestTools;

public class DiceboundUiInputTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static bool Gate(string property) => (bool)Runtime("GameplayInputGate").GetProperty(property).GetValue(null);

    [Test]
    public void CharacterImportRoundTripsTextAndRejectsInvalidFilesWithoutChangingSheet()
    {
        var root = new GameObject("Character import regression");
        try
        {
            var data = Runtime("CharacterData");
            var source = root.AddComponent(data);
            var destinationObject = new GameObject("Destination"); destinationObject.transform.SetParent(root.transform);
            var destination = destinationObject.AddComponent(data);
            data.GetField("characterName").SetValue(source, "Алиса: хранительница северных ворот");
            data.GetField("note1").SetValue(source, "Первая строка\nВторая: меч и щит");
            data.GetField("strength").SetValue(source, 17);
            string json = (string)Runtime("LssJsonConverter").GetMethod("ExportToLss").Invoke(null, new object[] { source });
            var import = Runtime("CharacterSheetUI").GetMethod("ImportCharacterJson");
            import.Invoke(null, new object[] { json, destination });
            Assert.That(data.GetField("characterName").GetValue(destination), Is.EqualTo(data.GetField("characterName").GetValue(source)));
            Assert.That(data.GetField("note1").GetValue(destination), Is.EqualTo(data.GetField("note1").GetValue(source)));
            Assert.That(data.GetField("strength").GetValue(destination), Is.EqualTo(17));
            string before = JsonUtility.ToJson(destination);
            foreach (string invalid in new[] { "{", "{}", "{\"data\":\"{}\"}" })
            {
                Assert.Throws<TargetInvocationException>(() => import.Invoke(null, new object[] { invalid, destination }));
                Assert.That(JsonUtility.ToJson(destination), Is.EqualTo(before));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [UnityTearDown]
    public IEnumerator ExitRuntime() { if (Application.isPlaying) yield return new ExitPlayMode(); }

    [UnityTest]
    public IEnumerator HeldMovementKeyDoesNotMoveCameraBehindDialogAndResumesAfterClosing()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        var originalSettings = UnityEngine.InputSystem.InputSystem.settings;
        var originalDevices = UnityEngine.InputSystem.InputSystem.devices
            .Where(d => d is UnityEngine.InputSystem.Keyboard || d is UnityEngine.InputSystem.Pointer)
            .Select(d => (device: d, enabled: d.enabled)).ToArray();
        var testSettings = UnityEngine.Object.Instantiate(originalSettings);
        testSettings.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
        testSettings.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        UnityEngine.InputSystem.InputSystem.settings = testSettings;
        foreach (var item in originalDevices) UnityEngine.InputSystem.InputSystem.DisableDevice(item.device);
        var keyboard = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>();
        var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        var root = new GameObject("Camera input regression", typeof(Camera));
        var dialog = new GameObject("Modal"); dialog.SetActive(false);
        try
        {
            var movement = root.AddComponent(Runtime("CameraMovement"));
            var update = movement.GetType().GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance);
            dialog.AddComponent(Runtime("UiModalScope")); dialog.SetActive(true);
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard,
                new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            Vector3 before = root.transform.position;
            yield return null; yield return null;
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, new Vector2(-100, -100));
            UnityEngine.InputSystem.LowLevel.InputState.Change(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.Update();
            Assert.That(keyboard.wKey.isPressed, Is.True);
            Assert.That(Gate("IsModalOpen"), Is.True, "Only the test keyboard may close the test dialog.");
            update.Invoke(movement, null);
            Assert.That(root.transform.position, Is.EqualTo(before));
            dialog.SetActive(false);
            yield return null; yield return null; yield return null;
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, new Vector2(-100, -100));
            UnityEngine.InputSystem.LowLevel.InputState.Change(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.QueueStateEvent(keyboard, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W));
            UnityEngine.InputSystem.InputSystem.Update();
            Assert.That(Gate("AllowsWorldPointerInput"), Is.True, "Closed modal must release world input after its close frame.");
            update.Invoke(movement, null);
            Assert.That(root.transform.position, Is.Not.EqualTo(before),
                $"Camera must become usable after the dialog closes: dt={Time.deltaTime}, timeScale={Time.timeScale}, keyboard={UnityEngine.InputSystem.Keyboard.current?.deviceId}/{keyboard.deviceId}, W={UnityEngine.InputSystem.Keyboard.current?.wKey.isPressed}, hotkeys={Gate("AllowsKeyboardHotkeys")}, pointer={Gate("AllowsWorldPointerInput")}, playing={Application.isPlaying}.");
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.RemoveDevice(keyboard);
            UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            UnityEngine.InputSystem.InputSystem.settings = originalSettings;
            foreach (var item in originalDevices)
                if (item.enabled) UnityEngine.InputSystem.InputSystem.EnableDevice(item.device);
                else UnityEngine.InputSystem.InputSystem.DisableDevice(item.device);
            UnityEngine.Object.DestroyImmediate(testSettings);
            UnityEngine.Object.DestroyImmediate(dialog); UnityEngine.Object.DestroyImmediate(root);
        }
    }

    [UnityTest]
    public IEnumerator NestedDialogsBlockWorldAndRestoreFocusInOrder()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        var root = new GameObject("UI modal regression");
        var existing = EventSystem.current;
        GameObject events = existing == null ? new GameObject("Test events", typeof(EventSystem)) : null;
        var system = EventSystem.current;
        var previous = system.currentSelectedGameObject;
        try
        {
            var behind = new GameObject("Background action", typeof(RectTransform), typeof(Button));
            behind.transform.SetParent(root.transform);
            system.SetSelectedGameObject(behind);
            GameObject Dialog(string name, out Button cancel)
            {
                var dialog = new GameObject(name, typeof(RectTransform)); dialog.SetActive(false);
                dialog.transform.SetParent(root.transform);
                var button = new GameObject("Cancel", typeof(RectTransform), typeof(Button));
                button.transform.SetParent(dialog.transform); cancel = button.GetComponent<Button>();
                var scope = dialog.AddComponent(Runtime("UiModalScope"));
                scope.GetType().GetField("initialSelection").SetValue(scope, cancel);
                return dialog;
            }
            var first = Dialog("First", out var firstCancel);
            var second = Dialog("Second", out var secondCancel);
            first.SetActive(true);
            yield return null; // Child Selectables finish OnEnable before the dialog assigns focus.
            Assert.That(system.currentSelectedGameObject, Is.SameAs(firstCancel.gameObject));
            Assert.That(Gate("AllowsWorldPointerInput"), Is.False);
            Assert.That(Gate("AllowsKeyboardHotkeys"), Is.False);
            Assert.That(Gate("AllowsImGuiOverlays"), Is.False);
            second.SetActive(true);
            yield return null;
            Assert.That(system.currentSelectedGameObject, Is.SameAs(secondCancel.gameObject));
            second.SetActive(false);
            Assert.That(system.currentSelectedGameObject, Is.SameAs(firstCancel.gameObject));
            Assert.That(Gate("IsModalOpen"), Is.True);
            first.SetActive(false);
            Assert.That(system.currentSelectedGameObject, Is.SameAs(behind));
            Assert.That(Runtime("UiModalScope").GetProperty("IsOpen").GetValue(null), Is.False);
            Assert.That(Gate("AllowsWorldPointerInput"), Is.False, "Closing click must not fall through in the same frame.");
            Assert.That(Gate("AllowsKeyboardHotkeys"), Is.False, "Closing Esc must not reach a world shortcut.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            system.SetSelectedGameObject(previous);
            if (events != null) UnityEngine.Object.DestroyImmediate(events);
        }
    }

    [UnityTest]
    public IEnumerator DialogRefocusesBackgroundSelectionAndIgnoresDisabledInitialButton()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        var root = new GameObject("Focus regression"); root.SetActive(false);
        GameObject events = EventSystem.current == null ? new GameObject("Test events", typeof(EventSystem)) : null;
        var system = EventSystem.current; var previous = system.currentSelectedGameObject;
        try
        {
            var disabled = new GameObject("Disabled", typeof(RectTransform), typeof(Button));
            disabled.transform.SetParent(root.transform); disabled.GetComponent<Button>().interactable = false;
            var enabled = new GameObject("Cancel", typeof(RectTransform), typeof(Button)); enabled.transform.SetParent(root.transform);
            var scope = root.AddComponent(Runtime("UiModalScope"));
            scope.GetType().GetField("initialSelection").SetValue(scope, disabled.GetComponent<Button>());
            root.SetActive(true);
            yield return null;
            Assert.That(system.currentSelectedGameObject, Is.SameAs(enabled));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); system.SetSelectedGameObject(previous); if (events != null) UnityEngine.Object.DestroyImmediate(events); }
    }
}
