using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Генерирует Mesh + данные граней для каждого типа дайса.
/// d6 берётся из примитива Cube, остальные — процедурно.
/// d100 использует ту же геометрию что d10, но другие значения граней.
/// </summary>
public static class DieMeshGenerator
{
    private const float Size = 0.5f; // circumradius (половина размера)

    // ──────────────────────────────────────────────
    //  Публичный API
    // ──────────────────────────────────────────────

    /// <summary>
    /// Возвращает (mesh, faceData[]) для указанного типа.
    /// d100 возвращает геометрию d10, но значения граней 00..90.
    /// </summary>
    public static (Mesh mesh, DieFaceData[] faces) Generate(DieType type)
    {
        switch (type)
        {
            case DieType.d4:  return BuildMesh(BuildTetrahedron(),    new[] { 1, 2, 3, 4 });
            // d6: -Z,+Z,-Y,+Y,-X,+X → противоположные грани: 1↔6, 2↔5, 3↔4
            case DieType.d6:  return BuildMesh(BuildCube(),           new[] { 1, 6, 2, 5, 3, 4 });
            // d8: верхние 0-3, нижние 4-7 → противоположные: 1↔8, 2↔7, 3↔6, 4↔5
            case DieType.d8:  return BuildMesh(BuildOctahedron(),     new[] { 1, 2, 3, 4, 6, 5, 8, 7 });
            // d10: top 0-4, bottom 0-4 → top_i ↔ bottom_(i+2) → 0↔9, 1↔8, 2↔7, 3↔6, 4↔5
            case DieType.d10:  return BuildD10(new[] { 0, 1, 2, 3, 4, 6, 5, 9, 8, 7 });
            // d100: 00↔90, 10↔80, 20↔70, 30↔60, 40↔50
            case DieType.d100: return BuildD10(new[] { 0, 10, 20, 30, 40, 60, 50, 90, 80, 70 });
            // d12: вокруг вершин икосаэдра 0..11 → vi↔противоположная: 1↔12, 2↔11, 5↔8, 6↔7, 9↔4, 10↔3
            case DieType.d12: return BuildMesh(BuildDodecahedronVertices(),
                              new[] { 1, 2, 11, 12, 5, 6, 7, 8, 9, 10, 3, 4 });
            // d20: 1..20 (противоположные = 21)
            case DieType.d20: return BuildMesh(BuildIcosahedron(),    EnumerateFaceValues(20));
            default:          return (null, null);
        }
    }

    // ══════════════════════════════════════════════
    //  d10 / d100 — изолированный самодостаточный генератор
    // ══════════════════════════════════════════════

    static (Mesh, DieFaceData[]) BuildD10(int[] faceValues)
    {
        const int n = 5;
        float H = Size;
        // Высота кольца из условия компланарности китов
        // h = Size * (sin36 + cos36·sin72 - sin36·cos72 - sin72) / (sin36 + cos36·sin72 - sin36·cos72 + sin72)
        float h = Size * 0.105573f;
        float R = Mathf.Sqrt(Size * Size - h * h);

        var verts = new Vector3[12];
        verts[0] = new Vector3(0, H, 0);
        verts[1] = new Vector3(0, -H, 0);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            verts[2 + i] = new Vector3(R * Mathf.Cos(a), h, R * Mathf.Sin(a));
            float a2 = (i + 0.5f) * Mathf.PI * 2f / n;
            verts[7 + i] = new Vector3(R * Mathf.Cos(a2), -h, R * Mathf.Sin(a2));
        }

        var tris = new int[60];
        var faces = new DieFaceData[10];
        var vertNorms = new Vector3[12]; // аккумулируем нормали вершин
        int ti = 0;

        for (int i = 0; i < n; i++)
        {
            int top = 0, bot = 1;
            int up0 = 2 + i;
            int up1 = 2 + (i + 1) % n;
            int lo0 = 7 + i;
            int lo1 = 7 + (i + 1) % n;

            // ── Верхний kite ──
            // Нормаль всей грани (из квида) — гарантированно наружу
            Vector3 fnTop = OutwardQuadNormal(verts, top, up0, lo0, up1);
            // Треугольники с правильным winding'ом
            EmitTriWithNormal(tris, ref ti, vertNorms, verts, top, up0, lo0, fnTop);
            EmitTriWithNormal(tris, ref ti, vertNorms, verts, top, lo0, up1, fnTop);
            // Face data
            Vector3 cTop = (verts[top] + verts[up0] + verts[lo0] + verts[up1]) / 4f;
            faces[i] = new DieFaceData { center = cTop, normal = fnTop, value = faceValues[i] };

            // ── Нижний kite ──
            Vector3 fnBot = OutwardQuadNormal(verts, bot, lo0, up1, lo1);
            EmitTriWithNormal(tris, ref ti, vertNorms, verts, bot, lo0, up1, fnBot);
            EmitTriWithNormal(tris, ref ti, vertNorms, verts, bot, up1, lo1, fnBot);
            Vector3 cBot = (verts[bot] + verts[lo0] + verts[up1] + verts[lo1]) / 4f;
            faces[i + n] = new DieFaceData { center = cBot, normal = fnBot, value = faceValues[i + n] };
        }

        // Нормализуем вершинные нормали
        for (int i = 0; i < 12; i++)
            if (vertNorms[i].sqrMagnitude > 0.0001f)
                vertNorms[i].Normalize();

        Mesh mesh = new Mesh { name = "Die_d10" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.normals = vertNorms; // <-- ручные нормали!
        mesh.RecalculateBounds();
        return (mesh, faces);
    }

    /// <summary>Нормаль квада, гарантированно наружу (от origin).</summary>
    static Vector3 OutwardQuadNormal(Vector3[] v, int i0, int i1, int i2, int i3)
    {
        Vector3 n = Vector3.Cross(v[i1] - v[i0], v[i3] - v[i0]);
        Vector3 c = (v[i0] + v[i1] + v[i2] + v[i3]) / 4f;
        if (Vector3.Dot(n, c) < 0f) n = -n;
        return n.normalized;
    }

    /// <summary>Выдаёт треугольник так, чтобы его нормаль совпадала с faceNormal.</summary>
    static void EmitTriWithNormal(int[] tris, ref int ti, Vector3[] vertNorms,
                                   Vector3[] verts, int i0, int i1, int i2, Vector3 faceNormal)
    {
        Vector3 tn = Vector3.Cross(verts[i1] - verts[i0], verts[i2] - verts[i0]);
        if (Vector3.Dot(tn, faceNormal) < 0f)
        {
            // Флип
            tris[ti] = i0; tris[ti+1] = i2; tris[ti+2] = i1; ti += 3;
        }
        else
        {
            tris[ti] = i0; tris[ti+1] = i1; tris[ti+2] = i2; ti += 3;
        }
        // Аккумулируем нормаль в вершины
        tn.Normalize();
        vertNorms[i0] += tn;
        vertNorms[i1] += tn;
        vertNorms[i2] += tn;
    }

    // ══════════════════════════════════════════════
    //  Тетраэдр (d4) — 4 треугольные грани
    // ══════════════════════════════════════════════

    static Vector3[] BuildTetrahedron()
    {
        float s = Size;
        // Правильные координаты регулярного тетраэдра с circumradius = s
        float a = s * 2f * Mathf.Sqrt(2f) / 3f; // 2R√2/3
        float b = s / 3f;
        float c = s * Mathf.Sqrt(2f) / 3f;       // R√2/3
        float d = s * Mathf.Sqrt(6f) / 3f;       // R√6/3

        return new[]
        {
            new Vector3( 0,  s,  0),  // 0 — верх
            new Vector3( 0, -b,  a),  // 1
            new Vector3(-d, -b, -c),  // 2
            new Vector3( d, -b, -c),  // 3
        };
    }

    static int[] TetraFaces => new[]
    {
        0,2,1,  0,3,2,  0,1,3,  1,2,3,
    };

    // ══════════════════════════════════════════════
    //  Куб (d6) — 6 квадратных граней
    // ══════════════════════════════════════════════

    static Vector3[] BuildCube()
    {
        // circumradius = s * √3 → s = R / √3
        float s = Size / Mathf.Sqrt(3f);
        return new[]
        {
            new Vector3(-s, -s, -s), new Vector3( s, -s, -s),
            new Vector3( s,  s, -s), new Vector3(-s,  s, -s),
            new Vector3(-s, -s,  s), new Vector3( s, -s,  s),
            new Vector3( s,  s,  s), new Vector3(-s,  s,  s),
        };
    }

    // вершины каждой грани (квады, потом триангулируем)
    static int[][] CubeQuads => new[]
    {
        new[]{0,3,2,1}, // -Z
        new[]{4,5,6,7}, // +Z
        new[]{0,1,5,4}, // -Y
        new[]{2,3,7,6}, // +Y
        new[]{0,4,7,3}, // -X
        new[]{1,2,6,5}, // +X
    };

    // ══════════════════════════════════════════════
    //  Октаэдр (d8) — 8 треугольных граней
    // ══════════════════════════════════════════════

    static Vector3[] BuildOctahedron()
    {
        float s = Size;
        return new[]
        {
            new Vector3( 0,  s,  0),  // 0 top
            new Vector3( 0, -s,  0),  // 1 bottom
            new Vector3( s,  0,  0),  // 2 +X
            new Vector3(-s,  0,  0),  // 3 -X
            new Vector3( 0,  0,  s),  // 4 +Z
            new Vector3( 0,  0, -s),  // 5 -Z
        };
    }

    static int[] OctaFaces => new[]
    {
        0,2,4,  0,4,3,  0,3,5,  0,5,2,
        1,4,2,  1,3,4,  1,5,3,  1,2,5,
    };

    // ══════════════════════════════════════════════
    //  Пентагональный трапецоэдр (d10 / d100)
    // ══════════════════════════════════════════════

    static Vector3[] BuildTrapezohedron()
    {
        const int n = 5;
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;

        // Все вершины лежат на сфере радиуса Size
        // Правильные пропорции регулярного трапецоэдра:
        //   h_ring = Size * φ/(2+φ)  — чтобы все рёбра китов были равны
        float H = Size;
        float h = Size * phi / (2f + phi);       // ≈ Size * 0.4472
        float R = Mathf.Sqrt(Size * Size - h * h); // ≈ Size * 0.8944

        var verts = new List<Vector3> { new(0, H, 0), new(0, -H, 0) }; // 0=top, 1=bottom

        // верхнее кольцо (5 вершин)
        for (int i = 0; i < n; i++)
        {
            float angle = i * Mathf.PI * 2f / n;
            verts.Add(new Vector3(R * Mathf.Cos(angle), h, R * Mathf.Sin(angle)));
        }
        // нижнее кольцо (смещено на полшага)
        for (int i = 0; i < n; i++)
        {
            float angle = (i + 0.5f) * Mathf.PI * 2f / n;
            verts.Add(new Vector3(R * Mathf.Cos(angle), -h, R * Mathf.Sin(angle)));
        }

        return verts.ToArray();
    }

    // вершины каждого кита (квад): top, upper, lower, upper_next
    /// <summary>
    /// Генерирует треугольники для d10/d100.
    /// Порядок: все верхние киты [0..4], затем все нижние [5..9] —
    /// совпадает с порядком faceValues.
    /// </summary>
    static TrapezohedronTriangles TrapFaces()
    {
        const int n = 5;
        var tri = new List<int>();

        // Верхние киты (5 штук): top → up_i → lo_i → up_{i+1}
        for (int i = 0; i < n; i++)
        {
            int top = 0;
            int up0 = 2 + i;
            int up1 = 2 + (i + 1) % n;
            int lo0 = 2 + n + i;
            AddQuad(tri, top, up0, lo0, up1);
        }

        // Нижние киты (5 штук): bot → lo_i → up_{i+1} → lo_{i+1}
        for (int i = 0; i < n; i++)
        {
            int bot = 1;
            int up1 = 2 + (i + 1) % n;
            int lo0 = 2 + n + i;
            int lo1 = 2 + n + (i + 1) % n;
            AddQuad(tri, bot, lo0, up1, lo1);
        }

        return new TrapezohedronTriangles { triangles = tri.ToArray(), faceCount = n * 2 };
    }

    struct TrapezohedronTriangles
    {
        public int[] triangles;
        public int faceCount; // = 10
    }

    // ══════════════════════════════════════════════
    //  Икосаэдр (d20) — алгоритмически из golden rectangles
    // ══════════════════════════════════════════════

    static Vector3[] BuildIcosahedron()
    {
        float phi = (1f + Mathf.Sqrt(5f)) / 2f;
        var raw = new Vector3[]
        {
            new( 0,  1,  phi), new( 0,  1, -phi), new( 0, -1,  phi), new( 0, -1, -phi),
            new( 1,  phi,  0), new( 1, -phi,  0), new(-1,  phi,  0), new(-1, -phi,  0),
            new( phi,  0,  1), new( phi,  0, -1), new(-phi,  0,  1), new(-phi,  0, -1),
        };
        for (int i = 0; i < raw.Length; i++)
            raw[i] = raw[i].normalized * Size;
        return raw;
    }

    static (int[] triangles, int faceCount) BuildIcosahedronFaces()
    {
        Vector3[] verts = BuildIcosahedron();
        int n = verts.Length; // 12

        // Соседи: dot ≈ 1/√5
        float neighborDot = 1f / Mathf.Sqrt(5f);
        const float eps = 0.01f;

        // Для каждой вершины — список соседей
        var neighbors = new List<int>[n];
        for (int i = 0; i < n; i++)
        {
            neighbors[i] = new List<int>();
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                float d = Vector3.Dot(verts[i].normalized, verts[j].normalized);
                if (Mathf.Abs(d - neighborDot) < eps)
                    neighbors[i].Add(j);
            }
        }

        // Собираем грани: для каждой вершины i, для каждой пары её соседей (a, b),
        // если a и b тоже соседи → грань (i, a, b)
        var faceSet = new HashSet<(int, int, int)>(new FaceComparer());
        for (int i = 0; i < n; i++)
        {
            var neigh = neighbors[i];
            for (int ai = 0; ai < neigh.Count; ai++)
            {
                for (int bi = ai + 1; bi < neigh.Count; bi++)
                {
                    int a = neigh[ai], b = neigh[bi];
                    if (!neighbors[a].Contains(b)) continue;

                    // сортируем чтобы дедупликация работала
                    var key = SortTriple(i, a, b);
                    faceSet.Add(key);
                }
            }
        }

        // В треугольники
        var tris = new List<int>();
        foreach (var f in faceSet)
        {
            tris.Add(f.Item1);
            tris.Add(f.Item2);
            tris.Add(f.Item3);
        }

        return (tris.ToArray(), faceSet.Count);
    }

    // ══════════════════════════════════════════════
    //  Додекаэдр (d12) — дуал икосаэдра
    // ══════════════════════════════════════════════

    static Vector3[] BuildDodecahedronVertices()
    {
        // Вершины додекаэдра = центры граней икосаэдра
        Vector3[] icoVerts = BuildIcosahedron();
        var (icoTris, faceCount) = BuildIcosahedronFaces();

        var centers = new Vector3[faceCount];
        int fi = 0;
        for (int t = 0; t < icoTris.Length; t += 3)
        {
            Vector3 c = (icoVerts[icoTris[t]] + icoVerts[icoTris[t+1]] + icoVerts[icoTris[t+2]]) / 3f;
            centers[fi++] = c.normalized * Size;
        }
        return centers;
    }

    static (int[] triangles, int faceCount) BuildDodecahedronFaces()
    {
        // Грани додекаэдра = вокруг каждой вершины икосаэдра
        // собираем центры граней икосаэдра, прилегающих к каждой вершине
        Vector3[] dodeVerts = BuildDodecahedronVertices();
        int nDode = dodeVerts.Length; // = 20

        Vector3[] icoVerts = BuildIcosahedron();
        var (icoTris, icoFaceCount) = BuildIcosahedronFaces();

        // Строим: для каждого икосаэдрного face index → его центр (индекс в dodeVerts)
        // и для каждой икосаэдрной вершины → список индексов граней, прилегающих к ней

        var vertToFaces = new List<int>[icoVerts.Length];
        for (int i = 0; i < icoVerts.Length; i++)
            vertToFaces[i] = new List<int>();

        int faceIdx = 0;
        for (int t = 0; t < icoTris.Length; t += 3)
        {
            vertToFaces[icoTris[t]].Add(faceIdx);
            vertToFaces[icoTris[t+1]].Add(faceIdx);
            vertToFaces[icoTris[t+2]].Add(faceIdx);
            faceIdx++;
        }

        // Каждая вершина икосаэдра даёт pentagon в додекаэдре
        var tris = new List<int>();
        for (int vi = 0; vi < icoVerts.Length; vi++)
        {
            var faceIndices = vertToFaces[vi]; // 5 индексов
            if (faceIndices.Count != 5) continue;

            // Упорядочиваем по углу вокруг оси verts[vi]
            Vector3 axis = icoVerts[vi].normalized;

            // Надёжный reference direction — не параллельный axis
            Vector3 refDir = Mathf.Abs(axis.y) < 0.99f ? Vector3.up : Vector3.right;

            faceIndices.Sort((a, b) =>
            {
                Vector3 ca = (dodeVerts[a] - axis * Vector3.Dot(dodeVerts[a], axis)).normalized;
                Vector3 cb = (dodeVerts[b] - axis * Vector3.Dot(dodeVerts[b], axis)).normalized;
                Vector3 perp = Vector3.Cross(axis, refDir).normalized;
                float angA = Mathf.Atan2(Vector3.Dot(ca, perp),
                                         Vector3.Dot(ca, refDir));
                float angB = Mathf.Atan2(Vector3.Dot(cb, perp),
                                         Vector3.Dot(cb, refDir));
                return angA.CompareTo(angB);
            });

            // Триангулируем pentagon (fan)
            for (int j = 1; j < faceIndices.Count - 1; j++)
            {
                tris.Add(faceIndices[0]);
                tris.Add(faceIndices[j]);
                tris.Add(faceIndices[j + 1]);
            }
        }

        return (tris.ToArray(), icoVerts.Length); // 12 pentagon faces
    }

    // ══════════════════════════════════════════════
    //  Утилиты
    // ══════════════════════════════════════════════

    static int[] EnumerateFaceValues(int count)
    {
        var v = new int[count];
        for (int i = 0; i < count; i++) v[i] = i + 1;
        return v;
    }

    static (int, int, int) SortTriple(int a, int b, int c)
    {
        int[] arr = { a, b, c };
        System.Array.Sort(arr);
        return (arr[0], arr[1], arr[2]);
    }

    class FaceComparer : IEqualityComparer<(int, int, int)>
    {
        public bool Equals((int, int, int) x, (int, int, int) y) =>
            x.Item1 == y.Item1 && x.Item2 == y.Item2 && x.Item3 == y.Item3;
        public int GetHashCode((int, int, int) o) =>
            o.Item1 * 10000 + o.Item2 * 100 + o.Item3;
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a); tris.Add(b); tris.Add(c);
        tris.Add(a); tris.Add(c); tris.Add(d);
    }

    /// <summary>
    /// Собирает Mesh из вершин и списка граней. FaceData вычисляется
    /// из финальных треугольников — гарантирует совпадение с рендером.
    /// </summary>
    static (Mesh, DieFaceData[]) BuildMesh(Vector3[] verts, int[] faceValues)
    {
        int faceCount = faceValues.Length;
        var tris = new List<int>();

        // Генерируем треугольники (как раньше)
        if (faceCount == 4) // d4
        {
            int[] ft = TetraFaces;
            for (int f = 0; f < 12; f++) tris.Add(ft[f]);
        }
        else if (faceCount == 6) // d6
        {
            for (int f = 0; f < 6; f++) AddQuad(tris, CubeQuads[f][0], CubeQuads[f][1], CubeQuads[f][2], CubeQuads[f][3]);
        }
        else if (faceCount == 8) // d8
        {
            int[] ft = OctaFaces;
            for (int f = 0; f < 24; f++) tris.Add(ft[f]);
        }
        else if (faceCount == 10) // d10
        {
            var trap = TrapFaces();
            for (int t = 0; t < trap.triangles.Length; t++) tris.Add(trap.triangles[t]);
        }
        else if (faceCount == 12) // d12
        {
            var (dodeTris, _) = BuildDodecahedronFaces();
            for (int t = 0; t < dodeTris.Length; t++) tris.Add(dodeTris[t]);
        }
        else if (faceCount == 20) // d20
        {
            var (icoTrisArr, _) = BuildIcosahedronFaces();
            for (int t = 0; t < icoTrisArr.Length; t++) tris.Add(icoTrisArr[t]);
        }

        // Исправляем winding
        var fixedTris = FixTriangleWinding(verts, tris);
        Mesh mesh = new Mesh { name = "Die" };
        mesh.vertices = verts;
        mesh.triangles = fixedTris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        // FaceData — из финальных треугольников
        int triPerFace = fixedTris.Length / 3 / faceCount;
        var faceData = ComputeFaceDataFromTris(verts, fixedTris, faceCount, triPerFace, faceValues);

        return (mesh, faceData);
    }

    /// <summary>Вычисляет DieFaceData[] из готового списка треугольников.</summary>
    static DieFaceData[] ComputeFaceDataFromTris(Vector3[] verts, int[] tris,
                                                  int faceCount, int triPerFace, int[] faceValues)
    {
        var result = new DieFaceData[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            Vector3 sumCenter = Vector3.zero;

            for (int t = 0; t < triPerFace; t++)
            {
                int idx = (f * triPerFace + t) * 3;
                int i0 = tris[idx], i1 = tris[idx + 1], i2 = tris[idx + 2];
                sumCenter += (verts[i0] + verts[i1] + verts[i2]) / 3f;
            }

            sumCenter /= triPerFace;
            // Нормаль = направление центра грани — гарантированно наружу
            result[f] = new DieFaceData
            {
                center = sumCenter,
                normal = sumCenter.normalized,
                value = faceValues[f]
            };
        }
        return result;
    }

    /// <summary>Переворачивает треугольники с inward-нормалями.</summary>
    static int[] FixTriangleWinding(Vector3[] verts, List<int> tris)
    {
        var result = new int[tris.Count];
        for (int t = 0; t < tris.Count; t += 3)
        {
            int i0 = tris[t], i1 = tris[t + 1], i2 = tris[t + 2];
            Vector3 v0 = verts[i0], v1 = verts[i1], v2 = verts[i2];
            Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
            Vector3 center = (v0 + v1 + v2) / 3f;

            // Нормаль должна смотреть от центра фигуры (origin)
            if (Vector3.Dot(normal, center) < 0f)
            {
                // Флип: меняем i1 и i2 местами
                result[t] = i0;
                result[t + 1] = i2;
                result[t + 2] = i1;
            }
            else
            {
                result[t] = i0;
                result[t + 1] = i1;
                result[t + 2] = i2;
            }
        }
        return result;
    }
}