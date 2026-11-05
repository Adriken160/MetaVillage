using UnityEngine;

// Reproduce el audio cuando el jugador entra en la zona y lo apaga al salir.
[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(SphereCollider))]
public class AudioZona : MonoBehaviour
{
    public string etiquetaJugador = "Player";
    public float radio = 15f;
    public bool pararAlSalir = true;
    public bool reiniciarAlEntrar = true;
    public float duracionFundido = 1f;

    AudioSource fuente;
    float volumenObjetivo;
    float volumenMaximo;
    bool dentro;

    void Awake()
    {
        SphereCollider zona = GetComponent<SphereCollider>();
        zona.isTrigger = true;
        zona.radius = radio;

        fuente = GetComponent<AudioSource>();
        fuente.playOnAwake = false;
        fuente.spatialBlend = 1f;
        volumenMaximo = fuente.volume;
        fuente.volume = 0f;
    }

    void OnTriggerEnter(Collider otro)
    {
        if (!otro.CompareTag(etiquetaJugador)) return;
        dentro = true;
        volumenObjetivo = volumenMaximo;

        if (reiniciarAlEntrar || !fuente.isPlaying)
        {
            fuente.Stop();
            fuente.Play();
        }
    }

    void OnTriggerExit(Collider otro)
    {
        if (!otro.CompareTag(etiquetaJugador)) return;
        dentro = false;
        volumenObjetivo = 0f;
    }

    void Update()
    {
        float velocidad = volumenMaximo / Mathf.Max(0.01f, duracionFundido);
        fuente.volume = Mathf.MoveTowards(fuente.volume, volumenObjetivo, velocidad * Time.deltaTime);

        if (!dentro && pararAlSalir && fuente.isPlaying && fuente.volume <= 0.001f)
            fuente.Stop();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, radio);
    }
}