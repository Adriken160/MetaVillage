using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

// Dialogo sencillo para un NPC:
//  - Al acercarte, aparece sobre su cabeza el aviso "[E] Hablar con ...".
//  - Cada vez que pulsas la tecla, pasa a la siguiente frase. Al terminar, vuelve al aviso.
//  - Al alejarte, el texto desaparece.
//  - Opcional: una voz (audio) por cada frase, y que el NPC se gire hacia ti.
//
// Requisitos: el jugador debe tener la etiqueta "Player" (PlayerArmature de Starter Assets ya la tiene)
// y TextMeshPro debe tener sus recursos importados (Window > TextMeshPro > Import TMP Essential Resources).
[RequireComponent(typeof(SphereCollider))]
public class NPCDialogo : MonoBehaviour
{
    [Header("Personaje")]
    public string nombre = "Vecino";
    [TextArea(2, 5)]
    public string[] frases = { "Buenas, forastero.", "Bienvenido a Garganta la Olla." };

    [Header("Voz (opcional, un audio por frase en el mismo orden)")]
    public AudioClip[] audios;

    [Header("Interaccion")]
    public string etiquetaJugador = "Player";
    public Key tecla = Key.E;
    public float radio = 3f;
    public bool mirarAlJugador = true;

    [Header("Texto flotante")]
    public float alturaTexto = 2.3f;
    public float tamanoLetra = 1.5f;
    public Vector2 tamanoCaja = new Vector2(5f, 2f);

    TextMeshPro texto;
    AudioSource fuente;
    Transform jugador;
    int indice = -1;

    void Awake()
    {
        // Zona de deteccion
        SphereCollider zona = GetComponent<SphereCollider>();
        zona.isTrigger = true;
        zona.radius = radio;
        zona.center = new Vector3(0f, 1f, 0f);

        // Texto flotante creado por codigo
        GameObject go = new GameObject("TextoNPC");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, alturaTexto, 0f);

        texto = go.AddComponent<TextMeshPro>();
        texto.rectTransform.pivot = new Vector2(0.5f, 0f);   // el borde inferior de la caja queda en el punto
        texto.alignment = TextAlignmentOptions.Bottom;       // el texto se apoya abajo y crece hacia arriba
        texto.fontSize = tamanoLetra;
        texto.rectTransform.sizeDelta = tamanoCaja;
        texto.color = Color.white;
        texto.outlineWidth = 0.25f;
        texto.outlineColor = new Color32(0, 0, 0, 255);
        go.SetActive(false);

        // Voz en 3D, para que se oiga mas fuerte cuanto mas cerca estes
        fuente = gameObject.AddComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 1f;
        fuente.rolloffMode = AudioRolloffMode.Linear;
        fuente.minDistance = 2f;
        fuente.maxDistance = 15f;
    }

    void OnTriggerEnter(Collider otro)
    {
        if (!otro.CompareTag(etiquetaJugador)) return;

        jugador = otro.transform;
        indice = -1;
        MostrarAviso();
        texto.gameObject.SetActive(true);
    }

    void OnTriggerExit(Collider otro)
    {
        if (!otro.CompareTag(etiquetaJugador)) return;

        jugador = null;
        indice = -1;
        fuente.Stop();
        texto.gameObject.SetActive(false);
    }

    void Update()
    {
        if (jugador == null) return;

        if (Keyboard.current != null && Keyboard.current[tecla].wasPressedThisFrame)
        {
            Avanzar();
        }

        // Gira al NPC hacia el jugador mientras habla
        if (mirarAlJugador && indice >= 0)
        {
            Vector3 dir = jugador.position - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), 5f * Time.deltaTime);
            }
        }
    }

    void LateUpdate()
    {
        // El texto siempre mira a la camara
        if (texto != null && texto.gameObject.activeSelf && Camera.main != null)
        {
            Vector3 desdeCamara = texto.transform.position - Camera.main.transform.position;
            if (desdeCamara.sqrMagnitude > 0.0001f)
            {
                texto.transform.rotation = Quaternion.LookRotation(desdeCamara);
            }
        }
    }

    void Avanzar()
    {
        indice++;

        if (frases == null || indice >= frases.Length)
        {
            indice = -1;
            fuente.Stop();
            MostrarAviso();
            return;
        }

        texto.text = "<b>" + nombre + "</b>\n" + frases[indice];

        if (audios != null && indice < audios.Length && audios[indice] != null)
        {
            fuente.Stop();
            fuente.clip = audios[indice];
            fuente.Play();
        }
    }

    void MostrarAviso()
    {
        texto.text = "[" + tecla + "] Hablar con " + nombre;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position + Vector3.up, radio);
    }
}
