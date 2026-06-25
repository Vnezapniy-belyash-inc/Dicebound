using UnityEngine;

/// <summary>
/// Generates an icosahedron (d20) mesh and assigns it to the GameObject's MeshFilter.
/// Attach this to a GameObject along with MeshFilter and MeshRenderer to create a d20 at runtime.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class DiceMeshGenerator : MonoBehaviour
{
    [Tooltip("Radius of the icosahedron")]
    public float radius = 1f;

    private void Awake()
    {
        GenerateIcosahedron();
    }

    /// <summary>
    /// Creates an icosahedron mesh with the given radius.
    /// </summary>
    void GenerateIcosahedron()
    {
        // Vertices of an icosahedron centered at origin
        float t = (1f + Mathf.Sqrt(5f)) / 2f; // golden ratio

        Vector3[] points = new Vector3[12];
        points[0] = new Vector3(-1,  t,  0).normalized * radius;
        points[1] = new Vector3( 1,  t,  0).normalized * radius;
        points[2] = new Vector3(-1, -t,  0).normalized * radius;
        points[3] = new Vector3( 1, -t,  0).normalized * radius;
        points[4] = new Vector3( 0, -1,  t).normalized * radius;
        points[5] = new Vector3( 0,  1,  t).normalized * radius;
        points[6] = new Vector3( 0, -1, -t).normalized * radius;
        points[7] = new Vector3( 0,  1, -t).normalized * radius;
        points[8] = new Vector3( t,  0, -1).normalized * radius;
        points[9] = new Vector3( t,  0,  1).normalized * radius;
        points[10] = new Vector3(-t,  0, -1).normalized * radius;
        points[11] = new Vector3(-t,  0,  1).normalized * radius;

        // Indices for the 20 faces (each triangle is three vertices)
        int[] triIndices = new int[60]
        {
            // 5 faces around point 0
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            // 5 adjacent faces
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            // 5 faces around point 3
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            // 5 adjacent faces
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };

        Mesh mesh = new Mesh();
        mesh.vertices = points;
        mesh.triangles = triIndices;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().sharedMesh = mesh;
    }
}