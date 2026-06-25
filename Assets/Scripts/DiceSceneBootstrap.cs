using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Builds the dice roll scene at runtime: table, d20, UI, and camera framing.
/// Attach to an empty GameObject in the scene (for example "Bootstrap").
/// </summary>
[DefaultExecutionOrder(-100)]
public class DiceSceneBootstrap : MonoBehaviour
{
    const string DiceName = "D20";
    const string TableName = "DiceTable";
    const string UiName = "DiceUI";
    const string ResultTextName = "ResultText";

    [Header("Layout")]
    [SerializeField] Vector3 diceSpawnPosition = new Vector3(0f, 1.5f, 0f);
    [SerializeField] Vector3 cameraPosition = new Vector3(0f, 4f, -6f);
    [SerializeField] Vector3 cameraLookAt = new Vector3(0f, 1.2f, 0f);

    [Header("Options")]
    [SerializeField] bool setupOnAwake = true;
    [SerializeField] float diceRadius = 0.6f;

    static bool sceneReady;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStaticState()
    {
        sceneReady = false;
    }

    void Awake()
    {
        if (setupOnAwake)
            Setup();
    }

    [ContextMenu("Setup Dice Scene")]
    public void Setup()
    {
        if (sceneReady)
            return;

        EnsureEventSystem();
        CreateTable();
        DiceController controller = CreateDice();
        Text resultText = CreateUi();
        SetupCamera();

        if (controller != null)
        {
            controller.resultText = resultText;
            controller.raycastCamera = Camera.main;
        }

        sceneReady = true;
        Debug.Log("Dice scene bootstrap complete.");
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        var eventSystem = new GameObject("EventSystem");
        eventSystem.AddComponent<EventSystem>();
        eventSystem.AddComponent<InputSystemUIInputModule>();
    }

    void CreateTable()
    {
        if (GameObject.Find(TableName) != null)
            return;

        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Plane);
        table.name = TableName;
        table.transform.position = Vector3.zero;
        table.transform.localScale = new Vector3(2f, 1f, 2f);

        ApplyColor(table.GetComponent<MeshRenderer>(), new Color(0.22f, 0.32f, 0.22f));
    }

    DiceController CreateDice()
    {
        GameObject existing = GameObject.Find(DiceName);
        if (existing != null)
            return existing.GetComponent<DiceController>();

        GameObject dice = new GameObject(DiceName);
        dice.transform.position = diceSpawnPosition;
        dice.transform.rotation = Random.rotation;

        DiceMeshGenerator meshGenerator = dice.AddComponent<DiceMeshGenerator>();
        meshGenerator.radius = diceRadius;

        Rigidbody rb = dice.AddComponent<Rigidbody>();
        rb.mass = 1f;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        ApplyColor(dice.GetComponent<MeshRenderer>(), new Color(0.75f, 0.12f, 0.12f));

        dice.AddComponent<DiceFaceLabels>();
        return dice.AddComponent<DiceController>();
    }

    Text CreateUi()
    {
        GameObject existingText = GameObject.Find(ResultTextName);
        if (existingText != null && existingText.TryGetComponent(out Text cachedText))
            return cachedText;

        GameObject canvasObject = GameObject.Find(UiName);
        if (canvasObject == null)
        {
            canvasObject = new GameObject(UiName);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject textObject = new GameObject(ResultTextName);
        textObject.transform.SetParent(canvasObject.transform, false);

        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 36;
        text.alignment = TextAnchor.UpperCenter;
        text.color = Color.white;
        text.text = "Кликни по кубику";

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -40f);
        rect.sizeDelta = new Vector2(640f, 80f);

        return text;
    }

    void SetupCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
            return;

        camera.transform.position = cameraPosition;
        camera.transform.rotation = Quaternion.LookRotation(cameraLookAt - cameraPosition, Vector3.up);
    }

    static void ApplyColor(MeshRenderer renderer, Color color)
    {
        if (renderer == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader == null)
            return;

        var material = new Material(shader);
        material.color = color;
        renderer.sharedMaterial = material;
    }
}
