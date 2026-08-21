using UnityEngine;

// ============================================================================
// ObjectiveTrigger.cs — Trigger físico del objetivo en el laberinto
// ============================================================================

public class ObjectiveTrigger : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 45f;
    private bool isTriggered = false;

    private void Update()
    {
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isTriggered) return;

        if (other.CompareTag("Player"))
        {
            isTriggered = true;
            Debug.Log("[ObjectiveTrigger] ¡El jugador ha alcanzado el objetivo!");

            if (MissionManager.Instance != null)
            {
                MissionManager.Instance.CompleteObjective();
            }
        }
    }
}
