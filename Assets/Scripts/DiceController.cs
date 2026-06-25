using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls a D20 dice: rolling via mouse click, detecting when it stops,
// and reporting the top face value via a UI Text component.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DiceController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("UI Text element to display the rolled value")]
    public Text resultText;

    [Header("Physics")]
    [Tooltip("Base force applied when rolling")]
    public float throwForce = 5f;
    [Tooltip("Base torque applied when rolling")]
    public float throwTorque = 5f;

    // Normals of the 20 faces of an icosahedron (normalized)
    private static readonly Vector3[] faceNormals = new Vector3[]
    {
        new Vector3( 0.000f,  0.894f,  0.447f),
        new Vector3( 0.000f,  0.894f, -0.447f),
        new Vector3( 0.723f,  0.447f,  0.526f),
        new Vector3(-0.723f,  0.447f,  0.526f),
        new Vector3(-0.276f,  0.447f,  0.851f),
        new Vector3( 0.276f,  0.447f,  0.851f),
        new Vector3( 0.894f,  0.000f,  0.447f),
        new Vector3( 0.894f,  0.000f, -0.447f),
        new Vector3( 0.276f, -0.447f,  0.851f),
        new Vector3(-0.276f, -0.447f,  0.851f),
        new Vector3(-0.723f, -0.447f,  0.526f),
        new Vector3( 0.723f, -0.447f,  0.526f),
        new Vector3( 0.000f, -0.894f,  0.447f),
        new Vector3( 0.000f, -0.894f, -0.447f),
        new Vector3(-0.723f, -0.447f, -0.526f),
        new Vector3( 0.723f, -0.447f, -0.526f),
        new Vector3(-0.276f, -0.447f, -0.851f),
        new Vector3( 0.276f, -0.447f, -0.851f),
        new Vector3(-0.894f,  0.000f,  0.447f),
        new Vector3(-0.894f,  0.000f, -0.447f)
    };

    // Mapping from face index to die value (standard D20 ordering)
    private static readonly int[] faceValues = new int[]
    {
        1, 2, 3, 4, 5,
        6, 7, 8, 9, 10,
        11,12,13,14,15,
        16,17,18,19,20
    };

    private Rigidbody rb;
    private bool wasSleeping = false;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Ensure the die starts stationary
        rb.Sleep();
    }

    private void Update()
    {
        // Detect left mouse click
        if (Input.GetMouseButtonDown(0))
        {
            Roll();
        }

        // Detect when die comes to rest
        bool sleeping = rb.IsSleeping();
        if (!wasSleeping && sleeping)
        {
            // Just came to rest
            ShowResult();
        }
        wasSleeping = sleeping;
    }

    /// <summary>
    /// Applies a random impulse and torque to simulate a roll.
    /// </summary>
    public void Roll()
    {
        // Wake up the rigidbody if it was sleeping
        rb.WakeUp();

        // Random direction and force
        Vector3 force = Random.onUnitSphere * throwForce;
        Vector3 torque = Random.onUnitSphere * throwTorque;

        rb.AddForce(force, ForceMode.Impulse);
        rb.AddTorque(torque, ForceMode.Impulse);

        // Clear previous result
        if (resultText != null)
            resultText.text = "";
    }

    /// <summary>
    /// Determines which face is currently on top and displays the value.
    /// </summary>
    private void ShowResult()
    {
        int bestIndex = 0;
        float bestDot = -1f;

        // Transform each local face normal to world space and compare with up
        for (int i = 0; i < faceNormals.Length; i++)
        {
            Vector3 worldNormal = transform.TransformDirection(faceNormals[i]);
            float dot = Vector3.Dot(worldNormal, Vector3.up);
            if (dot > bestDot)
            {
                bestDot = dot;
                bestIndex = i;
            }
        }

        int value = faceValues[bestIndex];
        if (resultText != null)
            resultText.text = $"Выпало: {value}";
        else
            Debug.Log($"Dice result: {value}");
    }
}