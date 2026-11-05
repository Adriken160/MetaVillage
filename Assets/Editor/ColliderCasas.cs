using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// IMPORTANTE: este archivo debe estar dentro de una carpeta llamada "Editor" (Assets/Editor).
//
// Anade colliders a las casas seleccionadas para que sean solidas y el avatar no las atraviese.
// Menu: Tools > Colliders de casas
//
//  - Caja: un unico BoxCollider en la raiz de cada casa, ajustado a su volumen (en espacio local,
//          asi respeta la rotacion y el estiramiento de cada casa). Es lo mas ligero y estable.
//  - Malla precisa: un MeshCollider (no convexo) en cada pieza con malla de la casa. Sigue la forma
//          exacta (puertas, voladizos...), pero es mas pesado.
//
// Si se selecciona un contenedor (p. ej. "Casas generadas") se usan sus hijos como casas.
// Ctrl+Z lo deshace.
public class ColliderCasas : EditorWindow
{
    enum Tipo { Caja, MallaPrecisa }

    Tipo tipo = Tipo.Caja;
    bool reemplazar = false;        // quita los colliders existentes de la casa y pone los nuevos
    float maxCasa = 40f;            // un objeto mayor que esto (en X o Z) se trata como contenedor

    const string RootName = "Casas generadas";

    [MenuItem("Tools/Colliders de casas")]
    static void Abrir()
    {
        GetWindow<ColliderCasas>("Colliders de casas");
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Hacer solidas las casas seleccionadas", EditorStyles.boldLabel);

        tipo = (Tipo)EditorGUILayout.EnumPopup("Tipo de collider", tipo);
        reemplazar = EditorGUILayout.Toggle("Reemplazar existentes", reemplazar);
        maxCasa = EditorGUILayout.Slider("Tamano max. de una casa (m)", maxCasa, 10f, 100f);

        EditorGUILayout.Space();
        if (GUILayout.Button("ANADIR COLLIDERS", GUILayout.Height(34)))
            Aplicar();

        EditorGUILayout.HelpBox(
            "Selecciona las casas (o el objeto 'Casas generadas') y pulsa el boton.\n" +
            "Si una casa ya tiene un collider activo y no marcas 'Reemplazar existentes', se salta.\n" +
            "Si vuelves a ejecutar 'Place' en PlaceHousesFromMap, las casas nuevas no tendran colliders: " +
            "o repites esta herramienta, o anades los colliders directamente a los prefabs.",
            MessageType.Info);
    }

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

    // True si la casa ya tiene algun collider activo que no sea trigger
    static bool YaEsSolida(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>())
            if (c.enabled && !c.isTrigger) return true;
        return false;
    }

    void Aplicar()
    {
        var casas = new List<GameObject>();
        var vistos = new HashSet<GameObject>();
        foreach (GameObject go in Selection.gameObjects)
            Reunir(go.transform, casas, vistos);

        if (casas.Count == 0)
        {
            EditorUtility.DisplayDialog("Nada seleccionado", "Selecciona alguna casa (o 'Casas generadas') en la jerarquia.", "OK");
            return;
        }

        int hechas = 0, saltadas = 0, sinMalla = 0;

        for (int i = 0; i < casas.Count; i++)
        {
            GameObject go = casas[i];
            EditorUtility.DisplayProgressBar("Anadiendo colliders", go.name, i / (float)casas.Count);

            if (!reemplazar && YaEsSolida(go)) { saltadas++; continue; }

            if (reemplazar)
            {
                foreach (Collider c in go.GetComponentsInChildren<Collider>())
                    Undo.DestroyObjectImmediate(c);
            }

            bool ok = tipo == Tipo.Caja ? AnadirCaja(go) : AnadirMallas(go);
            if (ok) hechas++; else sinMalla++;
        }

        EditorUtility.ClearProgressBar();
        Debug.Log("Colliders anadidos a " + hechas + " casa(s). Saltadas (ya solidas): " + saltadas +
                  ". Sin malla utilizable: " + sinMalla + ".");
    }

    // Un BoxCollider en la raiz, calculado en espacio local de la casa
    bool AnadirCaja(GameObject go)
    {
        Matrix4x4 mundoALocal = go.transform.worldToLocalMatrix;
        bool primero = true;
        Bounds local = new Bounds();

        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;

            Bounds mb = mf.sharedMesh.bounds;
            Matrix4x4 m = mundoALocal * mf.transform.localToWorldMatrix;

            for (int k = 0; k < 8; k++)
            {
                Vector3 esquina = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (k & 1) == 0 ? -1f : 1f,
                    (k & 2) == 0 ? -1f : 1f,
                    (k & 4) == 0 ? -1f : 1f));
                Vector3 p = m.MultiplyPoint3x4(esquina);

                if (primero) { local = new Bounds(p, Vector3.zero); primero = false; }
                else local.Encapsulate(p);
            }
        }

        if (primero) return false;

        BoxCollider bc = Undo.AddComponent<BoxCollider>(go);
        bc.center = local.center;
        bc.size = local.size;
        return true;
    }

    // Un MeshCollider no convexo en cada pieza con malla
    bool AnadirMallas(GameObject go)
    {
        bool alguna = false;

        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;

            MeshCollider mc = Undo.AddComponent<MeshCollider>(mf.gameObject);
            mc.sharedMesh = mf.sharedMesh;
            alguna = true;
        }

        return alguna;
    }
}