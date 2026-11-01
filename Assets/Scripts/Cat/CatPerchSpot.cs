using UnityEngine;
public class CatPerchSpot : MonoBehaviour
{
    public Collider support;
    public float radius = 0.24f;
    public bool allowLying = true;
    [System.NonSerialized] public bool playerOccupied;
    public CatPerching Occupant { get; private set; }
    public bool IsClear(CatPerching cat)
    {
        if (playerOccupied || !support || !support.enabled || (Occupant && Occupant != cat)) return false;
        Vector3[] checks = { Vector3.zero, Vector3.right * radius, Vector3.left * radius, Vector3.forward * radius, Vector3.back * radius };
        foreach (var offset in checks)
        {
            RaycastHit hit;
            if (!support.Raycast(new Ray(transform.position + offset + Vector3.up * .08f, Vector3.down), out hit, .12f) ||
                hit.normal.y < .85f || Mathf.Abs(hit.point.y - transform.position.y) > .03f) return false;
        }
        Vector3 center = transform.position + Vector3.up * 0.18f;
        foreach (var other in Physics.OverlapBox(center, new Vector3(radius, 0.15f, radius), transform.rotation, ~0, QueryTriggerInteraction.Ignore))
        {
            if (other == support || other.GetComponentInParent<PettableCat>() == cat.GetComponent<PettableCat>()) continue;
            // Other furniture, movable props and other cats all occupy landing space.
            return false;
        }
        foreach (var other in FindObjectsByType<PettableCat>(FindObjectsSortMode.None))
            if (other != cat.GetComponent<PettableCat>() && Vector3.Distance(other.transform.position, transform.position) < radius + 0.3f) return false;
        return true;
    }
    public bool Reserve(CatPerching cat) { if (!IsClear(cat)) return false; Occupant = cat; return true; }
    public void Release(CatPerching cat) { if (Occupant == cat) Occupant = null; }
}
