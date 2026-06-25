using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates an icosahedron (d20) mesh and assigns it to the GameObject's MeshFilter.
/// Also builds a convex MeshCollider and per-face data used for labels and roll results.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class DiceMeshGenerator : MonoBehaviour
{
    [Tooltip("Radius of the icosahedron")]
    public float radius = 1f;

    /// <summary>Outward normals for each of the 20 faces, in triangle order.</summary>
    public Vector3[] FaceNormals { get; private set; }

    /// <summary>Local-space centers for placing face labels.</summary>
    public Vector3[] FaceCenters { get; private set; }

    /// <summary>Local-space "up" direction for orienting digits on each face.</summary>
    public Vector3[] FaceLabelUps { get; private set; }

    /// <summary>Inscribed circle radius of each triangular face.</summary>
    public float[] FaceInradii { get; private set; }

    /// <summary>Standard d20 values mapped to faces in the same order as <see cref="FaceNormals"/>.</summary>
    public int[] FaceValues { get; private set; }

    void Awake()
    {
        GenerateIcosahedron();
    }

    void GenerateIcosahedron()
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;

        Vector3[] points = new Vector3[12];
        points[0] = new Vector3(-1, t, 0).normalized * radius;
        points[1] = new Vector3(1, t, 0).normalized * radius;
        points[2] = new Vector3(-1, -t, 0).normalized * radius;
        points[3] = new Vector3(1, -t, 0).normalized * radius;
        points[4] = new Vector3(0, -1, t).normalized * radius;
        points[5] = new Vector3(0, 1, t).normalized * radius;
        points[6] = new Vector3(0, -1, -t).normalized * radius;
        points[7] = new Vector3(0, 1, -t).normalized * radius;
        points[8] = new Vector3(t, 0, -1).normalized * radius;
        points[9] = new Vector3(t, 0, 1).normalized * radius;
        points[10] = new Vector3(-t, 0, -1).normalized * radius;
        points[11] = new Vector3(-t, 0, 1).normalized * radius;

        int[] triIndices =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11,
            1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9,
            4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        BuildFlatMesh(points, triIndices, out Vector3[] flatVertices, out int[] flatTriangles, out Vector3[] flatNormals);
        ComputeFaceData(points, triIndices);

        Mesh mesh = new Mesh { name = "D20" };
        mesh.vertices = flatVertices;
        mesh.triangles = flatTriangles;
        mesh.normals = flatNormals;

        GetComponent<MeshFilter>().mesh = mesh;

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        meshCollider.sharedMesh = mesh;
        meshCollider.convex = true;
    }

    static void BuildFlatMesh(
        Vector3[] points,
        int[] triIndices,
        out Vector3[] flatVertices,
        out int[] flatTriangles,
        out Vector3[] flatNormals)
    {
        int faceCount = triIndices.Length / 3;
        flatVertices = new Vector3[faceCount * 3];
        flatTriangles = new int[faceCount * 3];
        flatNormals = new Vector3[faceCount * 3];

        for (int face = 0; face < faceCount; face++)
        {
            Vector3 v0 = points[triIndices[face * 3]];
            Vector3 v1 = points[triIndices[face * 3 + 1]];
            Vector3 v2 = points[triIndices[face * 3 + 2]];
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

            int baseIndex = face * 3;
            flatVertices[baseIndex] = v0;
            flatVertices[baseIndex + 1] = v1;
            flatVertices[baseIndex + 2] = v2;
            flatTriangles[baseIndex] = baseIndex;
            flatTriangles[baseIndex + 1] = baseIndex + 1;
            flatTriangles[baseIndex + 2] = baseIndex + 2;
            flatNormals[baseIndex] = normal;
            flatNormals[baseIndex + 1] = normal;
            flatNormals[baseIndex + 2] = normal;
        }
    }

    void ComputeFaceData(Vector3[] vertices, int[] triangles)
    {
        int faceCount = triangles.Length / 3;
        FaceNormals = new Vector3[faceCount];
        FaceCenters = new Vector3[faceCount];
        FaceLabelUps = new Vector3[faceCount];
        FaceInradii = new float[faceCount];

        float labelLift = radius * 0.018f;

        for (int i = 0; i < faceCount; i++)
        {
            Vector3 v0 = vertices[triangles[i * 3]];
            Vector3 v1 = vertices[triangles[i * 3 + 1]];
            Vector3 v2 = vertices[triangles[i * 3 + 2]];

            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
            Vector3 centroid = (v0 + v1 + v2) / 3f;
            float inradius = ComputeFaceInradius(v0, v1, v2);

            FaceNormals[i] = normal;
            FaceCenters[i] = centroid + normal * labelLift;
            FaceLabelUps[i] = ComputeLabelUp(v0, v1, v2, normal, centroid);
            FaceInradii[i] = inradius;
        }

        FaceValues = ComputeStandardD20Values(FaceNormals);
    }

    static float ComputeFaceInradius(Vector3 v0, Vector3 v1, Vector3 v2)
    {
        float edgeA = Vector3.Distance(v0, v1);
        float edgeB = Vector3.Distance(v1, v2);
        float edgeC = Vector3.Distance(v2, v0);
        float semiperimeter = (edgeA + edgeB + edgeC) * 0.5f;
        float area = Mathf.Sqrt(Mathf.Max(0f, semiperimeter * (semiperimeter - edgeA) * (semiperimeter - edgeB) * (semiperimeter - edgeC)));
        return area / semiperimeter;
    }

    static Vector3 ComputeLabelUp(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 normal, Vector3 centroid)
    {
        Vector3 towardVertex = v0;
        if (v1.y > towardVertex.y)
            towardVertex = v1;
        if (v2.y > towardVertex.y)
            towardVertex = v2;

        Vector3 up = Vector3.ProjectOnPlane(towardVertex - centroid, normal);
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.ProjectOnPlane(v1 - v0, normal);

        return up.normalized;
    }

    /// <summary>
    /// Assigns values so opposite faces sum to 21, like a physical d20.
    /// </summary>
    static int[] ComputeStandardD20Values(Vector3[] normals)
    {
        int faceCount = normals.Length;
        var values = new int[faceCount];
        var paired = new bool[faceCount];
        var pairs = new List<(int first, int second)>();

        for (int i = 0; i < faceCount; i++)
        {
            if (paired[i])
                continue;

            int opposite = -1;
            float bestDot = float.MaxValue;

            for (int j = i + 1; j < faceCount; j++)
            {
                if (paired[j])
                    continue;

                float dot = Vector3.Dot(normals[i], normals[j]);
                if (dot < bestDot)
                {
                    bestDot = dot;
                    opposite = j;
                }
            }

            pairs.Add((i, opposite));
            paired[i] = true;
            paired[opposite] = true;
        }

        pairs.Sort((a, b) =>
        {
            float aScore = Mathf.Max(normals[a.first].y, normals[a.second].y);
            float bScore = Mathf.Max(normals[b.first].y, normals[b.second].y);
            return bScore.CompareTo(aScore);
        });

        for (int pairIndex = 0; pairIndex < pairs.Count; pairIndex++)
        {
            int lowValue = pairIndex + 1;
            int highValue = 21 - lowValue;
            (int first, int second) = pairs[pairIndex];

            if (normals[first].y >= normals[second].y)
            {
                values[first] = lowValue;
                values[second] = highValue;
            }
            else
            {
                values[first] = highValue;
                values[second] = lowValue;
            }
        }

        return values;
    }
}
