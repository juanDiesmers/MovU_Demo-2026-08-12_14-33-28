using System.Collections.Generic;
using TMPro;
using UnityEngine;

// ============================================================================
// PoiSignage.cs — Rotulos de los espacios
// ============================================================================
// Un texto sobre la puerta de cada POI, como la placa de un salon. Es
// senalizacion DENTRO del mundo: la tapan los muros, igual que en el edificio
// real, asi que no regala la ruta.
//
// Un solo componente atiende todos los rotulos (patron Manager): cada 0,25 s
// decide cuales estan lo bastante cerca para encenderse, y solo esos giran
// hacia la camara. Con 12 POIs son 12 distancias cada cuarto de segundo, en vez
// de 12 Update por frame.
//
// Se puede apagar entero desde el JSON ("rotulos": false) para las pruebas en
// las que se quiera medir la orientacion sin senalizacion.
// ============================================================================

public class PoiSignage : MonoBehaviour
{
    private const float Intervalo = 0.25f;

    [SerializeField] private float distanciaVisible = 16f;

    private readonly List<PointOfInterest> pois = new List<PointOfInterest>();
    private readonly List<TextMeshPro> textos = new List<TextMeshPro>();
    private Transform camara;
    private float proximaRevision;

    public void Configurar(float distancia)
    {
        distanciaVisible = Mathf.Max(2f, distancia);
    }

    private void Start()
    {
        Construir();
    }

    /// <summary>Crea (o vuelve a crear) un rotulo por cada POI que lo pida.</summary>
    public void Construir()
    {
        for (int i = 0; i < textos.Count; i++)
        {
            if (textos[i] != null) Destroy(textos[i].gameObject);
        }
        textos.Clear();
        pois.Clear();

        var lista = PointOfInterest.Todos;
        for (int i = 0; i < lista.Count; i++)
        {
            PointOfInterest poi = lista[i];
            if (poi == null || !poi.MostrarRotulo) continue;

            var go = new GameObject("Rotulo");
            // Hijo del POI: asi se apaga con su piso sin tener que programarlo.
            go.transform.SetParent(poi.transform, false);
            go.transform.position = poi.PosicionDelRotulo;

            var t = go.AddComponent<TextMeshPro>();
            t.text = poi.Nombre;
            t.fontSize = poi.Importante ? 3.4f : 2.8f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            // Texto oscuro: los muros son claros. Sin contorno a propósito:
            // tocar outlineWidth le crea a cada rótulo su propia copia del
            // material de la fuente y dejan de dibujarse juntos.
            t.color = poi.Importante ? new Color(0.72f, 0.30f, 0.02f) : new Color(0.08f, 0.10f, 0.14f);
            t.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            t.enabled = false;

            pois.Add(poi);
            textos.Add(t);
        }
    }

    private void LateUpdate()
    {
        if (camara == null)
        {
            Camera c = Camera.main;
            if (c == null) return;
            camara = c.transform;
        }

        Vector3 ojo = camara.position;

        if (Time.unscaledTime >= proximaRevision)
        {
            proximaRevision = Time.unscaledTime + Intervalo;
            float limite = distanciaVisible * distanciaVisible;

            for (int i = 0; i < textos.Count; i++)
            {
                TextMeshPro t = textos[i];
                if (t == null) continue;

                bool cerca = t.gameObject.activeInHierarchy &&
                             (t.transform.position - ojo).sqrMagnitude < limite;
                if (t.enabled != cerca) t.enabled = cerca;
            }
        }

        for (int i = 0; i < textos.Count; i++)
        {
            TextMeshPro t = textos[i];
            if (t == null || !t.enabled) continue;

            // Gira solo sobre el eje vertical: se lee de frente sin inclinarse.
            Vector3 haciaElTexto = t.transform.position - ojo;
            haciaElTexto.y = 0f;
            if (haciaElTexto.sqrMagnitude > 0.01f)
            {
                t.transform.rotation = Quaternion.LookRotation(haciaElTexto, Vector3.up);
            }
        }
    }
}
