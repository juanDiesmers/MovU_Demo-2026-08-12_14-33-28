using UnityEngine;

/// <summary>Anima la figura del jugador (tercera persona) según lo que camina.</summary>
public class FiguraDelJugador : MonoBehaviour
{
    private NpcFigura figura;
    private CharacterController cc;

    private void Start()
    {
        figura = GetComponentInChildren<NpcFigura>(true);
        cc = GetComponentInParent<CharacterController>();
    }

    private void LateUpdate()
    {
        if (figura == null || !figura.Visible) return;
        Vector3 vel = cc != null ? cc.velocity : Vector3.zero;
        vel.y = 0f;
        figura.Animar(vel.magnitude, Time.time, Time.deltaTime);
    }
}
