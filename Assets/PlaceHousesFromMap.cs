using System.Collections.Generic;
using UnityEngine;

public class PlaceHousesFromMap : MonoBehaviour
{
    [Header("Referencias")]
    public Terrain terrain;
    public Transform quad;              // el Quad con el mapa
    public Texture2D map;               // imagen del mapa (Read/Write activado, sin compresión)

    [Header("Edificios en el mapa")]
    public string buildingHex = "D6D6D6";           // color de los edificios en la imagen
    [Range(0f, 0.3f)] public float tolerance = 0.025f;
    public float minFootprint = 6f;                 // m² mínimos para considerar un edificio
    public float maxFootprint = 400f;               // m² máximos: descarta manchas enormes (campos, fincas)
    [Range(0.1f, 1f)] public float minFill = 0.5f;  // relleno mínimo de la mancha respecto a su caja orientada (descarta formas irregulares)
    public float maxHouseSize = 12f;                // lado máximo de una casa (m)
    [Range(0.1f, 1f)] public float minCoverage = 0.4f;
    public float margin = 0.3f;                     // separación entre casas (m)

    [Header("Casas")]
    public GameObject[] housePrefabs;               // arrastra aquí tus prefabs
    public float minStretch = 0.7f;
    public float maxStretch = 1.4f;
    public Vector2 heightVariation = new Vector2(0.9f, 1.25f);
    public float minHeightScale = 2f;   // escala Y mínima de cada casa
    public float yOffset = 0f;
    public int seed = 1;

    const string RootName = "Casas generadas";

    static Color Hex(string h)
    {
        ColorUtility.TryParseHtmlString("#" + h.Trim().TrimStart('#'), out Color c);
        return c;
    }

    Vector2 PixelToWorld(int px, int py)
    {
        Vector3 w = quad.TransformPoint(new Vector3(
            (px + 0.5f) / map.width - 0.5f,
            (py + 0.5f) / map.height - 0.5f, 0f));
        return new Vector2(w.x, w.z);
    }

    [ContextMenu("Borrar casas generadas")]
    void Clear()
    {
        var t = transform.Find(RootName);
        if (t != null) DestroyImmediate(t.gameObject);
    }

    [ContextMenu("Place")]
    void Place()
    {
        if (terrain == null || quad == null || map == null ||
            housePrefabs == null || housePrefabs.Length == 0)
        {
            Debug.LogError("Faltan referencias: Terrain, Quad, Map o Prefabs de casas.");
            return;
        }

        Clear();
        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);
#if UNITY_EDITOR
        UnityEditor.Undo.RegisterCreatedObjectUndo(root.gameObject, "Place houses");
#endif

        var rng = new System.Random(seed);
        int W = map.width, H = map.height;
        Color32[] px = map.GetPixels32();
        Color tc = Hex(buildingHex);

        // 1) Máscara de píxeles de edificio
        bool[] mask = new bool[W * H];
        int maskCount = 0;
        for (int i = 0; i < px.Length; i++)
        {
            float d = (Mathf.Abs(px[i].r / 255f - tc.r) +
                       Mathf.Abs(px[i].g / 255f - tc.g) +
                       Mathf.Abs(px[i].b / 255f - tc.b)) / 3f;
            if (d < tolerance) { mask[i] = true; maskCount++; }
        }

        float dx = Vector2.Distance(PixelToWorld(0, 0), PixelToWorld(1, 0));
        float dy = Vector2.Distance(PixelToWorld(0, 0), PixelToWorld(0, 1));
        float pixArea = dx * dy;

        Vector3 tp = terrain.transform.position;
        Vector3 ts = terrain.terrainData.size;

        // 2) Agrupar píxeles en edificios (flood fill)
        bool[] visited = new bool[W * H];
        var stack = new Stack<int>();
        var comp = new List<int>();
        var areas = new List<float>();
        int blobs = 0, houses = 0;
        int skipBig = 0, skipShape = 0, skipCov = 0, skipOut = 0, skipSize = 0;

        void Push(int j)
        {
            if (mask[j] && !visited[j]) { visited[j] = true; stack.Push(j); }
        }

        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || visited[start]) continue;

            comp.Clear();
            visited[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                comp.Add(i);
                int x = i % W, y = i / W;
                if (x > 0) Push(i - 1);
                if (x < W - 1) Push(i + 1);
                if (y > 0) Push(i - W);
                if (y < H - 1) Push(i + W);
            }

            float blobArea = comp.Count * pixArea;
            if (blobArea < minFootprint) continue;
            areas.Add(blobArea);

            if (blobArea > maxFootprint) { skipBig++; continue; }

            // 3) Orientación principal de la mancha (PCA en coordenadas del mundo)
            var pts = new Vector2[comp.Count];
            Vector2 mean = Vector2.zero;
            for (int k = 0; k < pts.Length; k++)
            {
                pts[k] = PixelToWorld(comp[k] % W, comp[k] / W);
                mean += pts[k];
            }
            mean /= pts.Length;

            float cxx = 0, czz = 0, cxz = 0;
            foreach (var p in pts)
            {
                Vector2 d = p - mean;
                cxx += d.x * d.x; czz += d.y * d.y; cxz += d.x * d.y;
            }
            float ang = 0.5f * Mathf.Atan2(2f * cxz, cxx - czz);
            Vector2 U = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            Vector2 V = new Vector2(-U.y, U.x);

            float minU = float.MaxValue, maxU = float.MinValue;
            float minV = float.MaxValue, maxV = float.MinValue;
            foreach (var p in pts)
            {
                float pu = Vector2.Dot(p, U), pv = Vector2.Dot(p, V);
                minU = Mathf.Min(minU, pu); maxU = Mathf.Max(maxU, pu);
                minV = Mathf.Min(minV, pv); maxV = Mathf.Max(maxV, pv);
            }
            float lenU = maxU - minU + dx;
            float lenV = maxV - minV + dy;

            // forma: un edificio rellena bien su caja orientada; un campo irregular, no
            if (blobArea / (lenU * lenV) < minFill) { skipShape++; continue; }
            blobs++;

            // 4) Dividir en celdas si la mancha es muy grande
            int nU = Mathf.Max(1, Mathf.CeilToInt(lenU / maxHouseSize));
            int nV = Mathf.Max(1, Mathf.CeilToInt(lenV / maxHouseSize));
            float cellW = lenU / nU, cellD = lenV / nV;

            int[] counts = new int[nU * nV];
            foreach (var p in pts)
            {
                int iu = Mathf.Clamp(Mathf.FloorToInt((Vector2.Dot(p, U) - minU) / lenU * nU), 0, nU - 1);
                int iv = Mathf.Clamp(Mathf.FloorToInt((Vector2.Dot(p, V) - minV) / lenV * nV), 0, nV - 1);
                counts[iv * nU + iu]++;
            }

            for (int iv = 0; iv < nV; iv++)
            for (int iu = 0; iu < nU; iu++)
            {
                if (counts[iv * nU + iu] * pixArea < minCoverage * cellW * cellD)
                {
                    skipCov++;
                    continue;
                }

                float cu = minU + (iu + 0.5f) * cellW;
                float cv = minV + (iv + 0.5f) * cellD;
                Vector2 c = U * cu + V * cv;

                if (c.x < tp.x || c.x > tp.x + ts.x || c.y < tp.z || c.y > tp.z + ts.z)
                {
                    skipOut++;
                    continue;
                }

                if (SpawnHouse(root, c, U, V, cellW - 2 * margin, cellD - 2 * margin, rng))
                    houses++;
                else
                    skipSize++;
            }
        }

        // estadística de áreas para ajustar minFootprint / maxFootprint
        string stats = "sin manchas";
        if (areas.Count > 0)
        {
            areas.Sort();
            stats = $"mín {areas[0]:F1} | mediana {areas[areas.Count / 2]:F1} | " +
                    $"p90 {areas[Mathf.Min(areas.Count - 1, Mathf.FloorToInt(areas.Count * 0.9f))]:F1} | máx {areas[areas.Count - 1]:F1} m²";
        }

        Debug.Log($"Píxeles: {maskCount} | Manchas válidas: {blobs} | Casas: {houses}\n" +
                  $"Descartadas -> demasiado grandes: {skipBig}, forma irregular: {skipShape}, cobertura: {skipCov}, " +
                  $"fuera de terreno: {skipOut}, tamaño/prefab: {skipSize}\n" +
                  $"Áreas de manchas (>= minFootprint): {stats}");
    }

    bool SpawnHouse(Transform root, Vector2 c, Vector2 U, Vector2 V, float w, float d, System.Random rng)
    {
        if (w < 1f || d < 1f) return false;

        var prefab = housePrefabs[rng.Next(housePrefabs.Length)];
        if (prefab == null) return false;

        var go = Instantiate(prefab, root);
        go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Vector3 baseScale = go.transform.localScale;

        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) { DestroyImmediate(go); return false; }
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        Vector3 size = b.size;
        Vector3 off = b.center;
        float bottom = b.min.y;
        if (size.x < 0.01f || size.z < 0.01f) { DestroyImmediate(go); return false; }

        // alinear el lado largo de la casa con el lado largo de la huella
        bool swap = (size.x >= size.z) != (w >= d);
        float fx = swap ? d : w, fz = swap ? w : d;

        float sx = Mathf.Clamp(fx / size.x, minStretch, maxStretch);
        float sz = Mathf.Clamp(fz / size.z, minStretch, maxStretch);
        float sy = Mathf.Clamp((sx + sz) * 0.5f, minStretch, maxStretch)
                * Mathf.Lerp(heightVariation.x, heightVariation.y, (float)rng.NextDouble());

        // altura mínima: la escala Y nunca baja de minHeightScale
        sy = Mathf.Max(sy, minHeightScale);

        Vector2 r = swap ? V : U;
        Quaternion rot = Quaternion.LookRotation(new Vector3(-r.y, 0f, r.x), Vector3.up);
        Vector3 m = new Vector3(sx, sy, sz);
        Vector3 cen = rot * Vector3.Scale(off, m);

        float gy = terrain.SampleHeight(new Vector3(c.x, 0f, c.y)) + terrain.transform.position.y;
        go.transform.SetPositionAndRotation(
            new Vector3(c.x - cen.x, gy + yOffset - bottom * sy, c.y - cen.z), rot);
        go.transform.localScale = Vector3.Scale(baseScale, m);
        return true;
    }

#if UNITY_EDITOR
    // Genera Assets/mascara_debug.png: en rojo los píxeles que se consideran edificio.
    [ContextMenu("Exportar máscara")]
    void ExportMask()
    {
        if (map == null)
        {
            Debug.LogError("Falta la textura Map.");
            return;
        }

        Color32[] px = map.GetPixels32();
        Color tc = Hex(buildingHex);
        var outPx = new Color32[px.Length];
        for (int i = 0; i < px.Length; i++)
        {
            float d = (Mathf.Abs(px[i].r / 255f - tc.r) +
                       Mathf.Abs(px[i].g / 255f - tc.g) +
                       Mathf.Abs(px[i].b / 255f - tc.b)) / 3f;
            outPx[i] = d < tolerance
                ? new Color32(255, 0, 0, 255)
                : new Color32((byte)(px[i].r / 2), (byte)(px[i].g / 2), (byte)(px[i].b / 2), 255);
        }

        var tex = new Texture2D(map.width, map.height, TextureFormat.RGB24, false);
        tex.SetPixels32(outPx);
        tex.Apply();
        System.IO.File.WriteAllBytes("Assets/mascara_debug.png", tex.EncodeToPNG());
        DestroyImmediate(tex);
        UnityEditor.AssetDatabase.Refresh();
        Debug.Log("Máscara exportada a Assets/mascara_debug.png");
    }

    // Lista los colores más frecuentes de la imagen para localizar el relleno de los edificios.
    [ContextMenu("Colores más frecuentes")]
    void TopColors()
    {
        if (map == null)
        {
            Debug.LogError("Falta la textura Map.");
            return;
        }

        Color32[] px = map.GetPixels32();
        var counts = new Dictionary<int, int>();
        foreach (var p in px)
        {
            // agrupar colores parecidos (pasos de 4 niveles)
            int key = ((p.r >> 2) << 16) | ((p.g >> 2) << 8) | (p.b >> 2);
            counts.TryGetValue(key, out int n);
            counts[key] = n + 1;
        }

        var list = new List<KeyValuePair<int, int>>(counts);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));

        var sb = new System.Text.StringBuilder("Colores más frecuentes:\n");
        for (int i = 0; i < Mathf.Min(20, list.Count); i++)
        {
            int k = list[i].Key;
            var c = new Color32((byte)(((k >> 16) & 63) << 2), (byte)(((k >> 8) & 63) << 2), (byte)((k & 63) << 2), 255);
            sb.AppendLine($"#{ColorUtility.ToHtmlStringRGB(c)}  ->  {list[i].Value} px  ({100f * list[i].Value / px.Length:F1}%)");
        }
        Debug.Log(sb.ToString());
    }
#endif
}