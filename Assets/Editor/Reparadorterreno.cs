using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// IMPORTANTE: este archivo debe estar dentro de una carpeta llamada "Editor" (Assets/Editor).
//
// Herramienta de reparacion del terreno: Tools > Reparar terreno
//
//  1) REPARAR PAREDES Y PLATAFORMAS
//     Detecta las celdas del heightmap donde el desnivel con la vecina es demasiado fuerte
//     (muros casi verticales), ensancha esa zona unos metros y la rellena por relajacion
//     (cada celda pasa a ser la media de sus vecinas, repetido). El resultado es una rampa
//     suave entre la parte alta y la baja. El resto del terreno no se toca.
//
//  2) SUAVIZAR ZONA
//     Difumina toda la region (util para quitar la planitud artificial de una plataforma).
//
//  3) APOYAR CASAS
//     Reasienta las casas sobre el terreno actual, sin modificar el terreno.
//
// La region de trabajo es la de los objetos seleccionados mas un margen. Si no hay nada
// seleccionado se trabaja sobre TODO el terreno (puede tardar con heightmaps grandes).
// Si se selecciona un contenedor (p. ej. "Casas generadas") se usan sus hijos como casas.
public class ReparadorTerreno : EditorWindow
{
    enum Apoyo { Minimo, Media }

    Terrain terreno;

    // Region
    bool soloSeleccion = true;
    float margenZona = 30f;         // metros alrededor de la seleccion

    // Reparar paredes
    float pendienteMax = 1.2f;      // desnivel/distancia entre celdas vecinas (1 = 45 grados)
    float radio = 6f;               // metros de rampa a cada lado de la pared
    int iteraciones = 400;          // mas = rampa mas suave y mas lento
    bool apoyarDespues = true;      // reasentar las casas seleccionadas tras reparar

    // Suavizar zona
    int pasadas = 5;

    // Casas
    Apoyo apoyo = Apoyo.Minimo;
    float hundir = 0.1f;            // cuanto se hunde la base de la casa (metros)
    float maxCasa = 40f;            // un objeto mayor que esto (en X o Z) se trata como contenedor

    bool copiaSeguridad = true;

    const string RootName = "Casas generadas";

    [MenuItem("Tools/Reparar terreno")]
    static void Abrir()
    {
        GetWindow<ReparadorTerreno>("Reparar terreno");
    }

    void OnEnable()
    {
        if (terreno == null) terreno = Terrain.activeTerrain;
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        terreno = (Terrain)EditorGUILayout.ObjectField("Terreno", terreno, typeof(Terrain), true);
        copiaSeguridad = EditorGUILayout.Toggle("Copia de seguridad", copiaSeguridad);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Region de trabajo", EditorStyles.boldLabel);
        soloSeleccion = EditorGUILayout.Toggle("Limitar a la seleccion", soloSeleccion);
        margenZona = EditorGUILayout.Slider("Margen alrededor (m)", margenZona, 5f, 100f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("1) Reparar paredes y plataformas", EditorStyles.boldLabel);
        pendienteMax = EditorGUILayout.Slider("Pendiente maxima", pendienteMax, 0.3f, 5f);
        radio = EditorGUILayout.Slider("Rampa a cada lado (m)", radio, 1f, 20f);
        iteraciones = EditorGUILayout.IntSlider("Suavizado (iteraciones)", iteraciones, 10, 1500);
        apoyarDespues = EditorGUILayout.Toggle("Apoyar casas despues", apoyarDespues);
        if (GUILayout.Button("REPARAR PAREDES", GUILayout.Height(30)))
            Reparar();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2) Suavizar zona", EditorStyles.boldLabel);
        pasadas = EditorGUILayout.IntSlider("Pasadas", pasadas, 1, 50);
        if (GUILayout.Button("SUAVIZAR ZONA", GUILayout.Height(24)))
            Suavizar();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3) Apoyar casas", EditorStyles.boldLabel);
        apoyo = (Apoyo)EditorGUILayout.EnumPopup("Altura de apoyo", apoyo);
        hundir = EditorGUILayout.Slider("Hundir la casa (m)", hundir, 0f, 1f);
        maxCasa = EditorGUILayout.Slider("Tamano max. de una casa (m)", maxCasa, 10f, 100f);
        if (GUILayout.Button("APOYAR CASAS SELECCIONADAS", GUILayout.Height(24)))
            ApoyarCasas();

        EditorGUILayout.HelpBox(
            "Orden recomendado: selecciona las casas (o 'Casas generadas'), pulsa REPARAR PAREDES, " +
            "revisa el resultado y, si aun se nota una plataforma plana, usa SUAVIZAR ZONA y luego " +
            "APOYAR CASAS.\n" +
            "Si se llevan rocas naturales por delante, sube 'Pendiente maxima' (1.5-2.5) para que solo " +
            "afecte a muros casi verticales.\n" +
            "Desactiva Auto Save de Unity mientras pruebas. Ctrl+Z lo deshace (si la pila de deshacer sigue viva).",
            MessageType.Info);
    }

    // ------------------------------------------------------------------ utilidades

    void HacerCopia(TerrainData td)
    {
        if (!copiaSeguridad) return;

        string ruta = AssetDatabase.GetAssetPath(td);
        if (string.IsNullOrEmpty(ruta)) return;

        string destino = AssetDatabase.GenerateUniqueAssetPath(
            "Assets/" + System.IO.Path.GetFileNameWithoutExtension(ruta) + "_backup.asset");
        if (AssetDatabase.CopyAsset(ruta, destino))
            Debug.Log("Copia de seguridad del terreno (estado guardado en disco): " + destino);
    }

    // Devuelve la region del heightmap sobre la que trabajar
    bool ObtenerRegion(TerrainData td, out int x0, out int z0, out int w, out int h)
    {
        Vector3 tpos = terreno.transform.position;
        Vector3 size = td.size;
        int res = td.heightmapResolution;

        x0 = 0; z0 = 0; w = res; h = res;
        if (!soloSeleccion) return true;

        bool alguno = false;
        Bounds total = new Bounds();
        foreach (GameObject go in Selection.gameObjects)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!alguno) { total = r.bounds; alguno = true; }
                else total.Encapsulate(r.bounds);
            }
        }

        if (!alguno) return true;   // sin seleccion: todo el terreno

        int ax = Mathf.Clamp(Mathf.FloorToInt((total.min.x - margenZona - tpos.x) / size.x * (res - 1)), 0, res - 1);
        int bx = Mathf.Clamp(Mathf.CeilToInt((total.max.x + margenZona - tpos.x) / size.x * (res - 1)), 0, res - 1);
        int az = Mathf.Clamp(Mathf.FloorToInt((total.min.z - margenZona - tpos.z) / size.z * (res - 1)), 0, res - 1);
        int bz = Mathf.Clamp(Mathf.CeilToInt((total.max.z + margenZona - tpos.z) / size.z * (res - 1)), 0, res - 1);

        x0 = ax; z0 = az; w = bx - ax + 1; h = bz - az + 1;
        return w >= 3 && h >= 3;
    }

    static float PendienteMaxima(float[,] a, int w, int h, float sy, float cx, float cz)
    {
        float max = 0f;
        for (int iz = 0; iz < h; iz++)
        {
            for (int ix = 0; ix < w; ix++)
            {
                if (ix < w - 1) max = Mathf.Max(max, Mathf.Abs(a[iz, ix + 1] - a[iz, ix]) * sy / cx);
                if (iz < h - 1) max = Mathf.Max(max, Mathf.Abs(a[iz + 1, ix] - a[iz, ix]) * sy / cz);
            }
        }
        return max;
    }

    bool ComprobarTerreno()
    {
        if (terreno == null)
        {
            EditorUtility.DisplayDialog("Falta el terreno", "Arrastra el Terrain al campo 'Terreno'.", "OK");
            return false;
        }
        return true;
    }

    // ------------------------------------------------------------------ 1) reparar paredes

    void Reparar()
    {
        if (!ComprobarTerreno()) return;

        TerrainData td = terreno.terrainData;
        Vector3 size = td.size;
        int res = td.heightmapResolution;
        float cx = size.x / (res - 1);
        float cz = size.z / (res - 1);

        if (!ObtenerRegion(td, out int x0, out int z0, out int w, out int h))
        {
            Debug.LogWarning("Region demasiado pequena o fuera del terreno.");
            return;
        }

        HacerCopia(td);
        Undo.RegisterCompleteObjectUndo(td, "Reparar terreno");

        float[,] alt = td.GetHeights(x0, z0, w, h);
        float antes = PendienteMaxima(alt, w, h, size.y, cx, cz);

        // 1) Detectar celdas con desnivel excesivo respecto a su vecina
        var paredes = new List<Vector2Int>();
        for (int iz = 0; iz < h; iz++)
        {
            for (int ix = 0; ix < w; ix++)
            {
                bool pared = false;
                if (ix < w - 1 && Mathf.Abs(alt[iz, ix + 1] - alt[iz, ix]) * size.y / cx > pendienteMax) pared = true;
                if (iz < h - 1 && Mathf.Abs(alt[iz + 1, ix] - alt[iz, ix]) * size.y / cz > pendienteMax) pared = true;
                if (pared) paredes.Add(new Vector2Int(ix, iz));
            }
        }

        if (paredes.Count == 0)
        {
            Debug.Log("No se han encontrado paredes con pendiente mayor que " + pendienteMax +
                      " (pendiente maxima actual: " + antes.ToString("F2") + "). Baja 'Pendiente maxima' si aun ves muros.");
            if (apoyarDespues) ApoyarCasas();
            return;
        }

        // 2) Ensanchar la zona de reparacion 'radio' metros alrededor de cada pared
        bool[,] zona = new bool[h, w];
        int rx = Mathf.CeilToInt(radio / cx);
        int rz = Mathf.CeilToInt(radio / cz);
        float radio2 = radio * radio;
        foreach (Vector2Int c in paredes)
        {
            for (int dz = -rz; dz <= rz; dz++)
            {
                int zz = c.y + dz;
                if (zz < 1 || zz > h - 2) continue;      // el borde de la region queda fijo

                for (int dx = -rx; dx <= rx; dx++)
                {
                    int xx = c.x + dx;
                    if (xx < 1 || xx > w - 2) continue;
                    if (dx * cx * dx * cx + dz * cz * dz * cz > radio2) continue;
                    zona[zz, xx] = true;
                }
            }
        }

        // 3) Relajacion: cada celda de la zona pasa a ser la media de sus 4 vecinas
        for (int it = 0; it < iteraciones; it++)
        {
            for (int iz = 1; iz < h - 1; iz++)
            {
                for (int ix = 1; ix < w - 1; ix++)
                {
                    if (!zona[iz, ix]) continue;
                    alt[iz, ix] = 0.25f * (alt[iz, ix - 1] + alt[iz, ix + 1] + alt[iz - 1, ix] + alt[iz + 1, ix]);
                }
            }
        }

        td.SetHeights(x0, z0, alt);

        float despues = PendienteMaxima(alt, w, h, size.y, cx, cz);
        Debug.Log("Reparacion: " + paredes.Count + " celdas de pared. Pendiente maxima en la region: " +
                  antes.ToString("F2") + " -> " + despues.ToString("F2") + " (1.00 = 45 grados).");

        if (apoyarDespues) ApoyarCasas();
    }

    // ------------------------------------------------------------------ 2) suavizar zona

    void Suavizar()
    {
        if (!ComprobarTerreno()) return;

        TerrainData td = terreno.terrainData;
        if (!ObtenerRegion(td, out int x0, out int z0, out int w, out int h))
        {
            Debug.LogWarning("Region demasiado pequena o fuera del terreno.");
            return;
        }

        HacerCopia(td);
        Undo.RegisterCompleteObjectUndo(td, "Suavizar terreno");

        float[,] alt = td.GetHeights(x0, z0, w, h);
        float[,] tmp = new float[h, w];

        for (int p = 0; p < pasadas; p++)
        {
            for (int iz = 0; iz < h; iz++)
            {
                for (int ix = 0; ix < w; ix++)
                {
                    // el borde de la region queda fijo
                    if (ix == 0 || iz == 0 || ix == w - 1 || iz == h - 1)
                    {
                        tmp[iz, ix] = alt[iz, ix];
                        continue;
                    }

                    float suma = 0f;
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                            suma += alt[iz + dz, ix + dx];
                    tmp[iz, ix] = suma / 9f;
                }
            }

            var t = alt; alt = tmp; tmp = t;
        }

        td.SetHeights(x0, z0, alt);
        Debug.Log("Zona suavizada (" + pasadas + " pasadas). Ahora usa APOYAR CASAS.");
    }

    // ------------------------------------------------------------------ 3) apoyar casas

    void Reunir(Transform t, List<GameObject> lista, HashSet<GameObject> vistos)
    {
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        bool grande = Mathf.Max(b.size.x, b.size.z) > maxCasa;
        if (t.name == RootName || (grande && t.childCount > 0))
        {
            foreach (Transform hijo in t) Reunir(hijo, lista, vistos);
        }
        else if (vistos.Add(t.gameObject))
        {
            lista.Add(t.gameObject);
        }
    }

    void ApoyarCasas()
    {
        if (!ComprobarTerreno()) return;

        var casas = new List<GameObject>();
        var vistos = new HashSet<GameObject>();
        foreach (GameObject go in Selection.gameObjects)
            Reunir(go.transform, casas, vistos);

        if (casas.Count == 0)
        {
            Debug.Log("No hay casas seleccionadas que apoyar.");
            return;
        }

        float baseTerreno = terreno.transform.position.y;
        int apoyadas = 0;

        foreach (GameObject go in casas)
        {
            Renderer[] rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) continue;

            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

            // muestrear el terreno bajo la huella (encogida un 10 % para no coger la ladera de al lado)
            float ex = b.size.x * 0.1f, ez = b.size.z * 0.1f;
            float minX = b.min.x + ex, maxX = b.max.x - ex;
            float minZ = b.min.z + ez, maxZ = b.max.z - ez;
            int nx = Mathf.Max(3, Mathf.CeilToInt((maxX - minX) / 0.5f) + 1);
            int nz = Mathf.Max(3, Mathf.CeilToInt((maxZ - minZ) / 0.5f) + 1);

            float suelo = apoyo == Apoyo.Minimo ? float.MaxValue : 0f;
            for (int iz = 0; iz < nz; iz++)
            {
                for (int ix = 0; ix < nx; ix++)
                {
                    float x = Mathf.Lerp(minX, maxX, ix / (float)(nx - 1));
                    float z = Mathf.Lerp(minZ, maxZ, iz / (float)(nz - 1));
                    float y = terreno.SampleHeight(new Vector3(x, 0f, z)) + baseTerreno;

                    if (apoyo == Apoyo.Minimo) suelo = Mathf.Min(suelo, y);
                    else suelo += y;
                }
            }
            if (apoyo == Apoyo.Media) suelo /= nx * nz;

            float dy = (suelo - hundir) - b.min.y;
            Undo.RecordObject(go.transform, "Apoyar casa");
            go.transform.position += new Vector3(0f, dy, 0f);
            apoyadas++;
        }

        Debug.Log("Casas apoyadas sobre el terreno: " + apoyadas);
    }
}