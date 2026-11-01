using UnityEngine;

// Keep the visible palm outside cat petting volumes; controller tracking is untouched.
[DefaultExecutionOrder(400)]
[RequireComponent(typeof(ControllerHandPoser))]
public class PettingHandSurfaceContact : MonoBehaviour
{
    public PettingHand palm;
    public float maxVisualOffset = 0.2f;
    public float furCompression = 0.008f;
    public float VisualOffset { get; private set; }
    Vector3 restLocalPosition;
    Vector3 contactNormal;
    CatPetZone lastZone;
    SphereCollider palmCollider;
    void Awake()
    {
        restLocalPosition = transform.localPosition;
        if (!palm) palm = GetComponentInChildren<PettingHand>();
        if (palm) palmCollider = palm.GetComponent<SphereCollider>();
    }
    void Update()
    {
        // Restore the unmodified tracked pose before palm overlap detection.
        transform.localPosition = restLocalPosition;
        VisualOffset = 0f;
    }
    void LateUpdate()
    {
        if (!palm || !palmCollider || !palm.isActiveAndEnabled || !palm.ContactZone)
        {
            lastZone = null;
            return;
        }
        var zone = palm.ContactZone;
        var surface = zone.GetComponent<SphereCollider>();
        if (!surface || !surface.enabled) return;
        Vector3 direction;
        float depth;
        if (!Physics.ComputePenetration(palmCollider, palm.transform.position, palm.transform.rotation,
            surface, surface.transform.position, surface.transform.rotation, out direction, out depth)) return;
        if (lastZone != zone) contactNormal = direction;
        else if (Vector3.Dot(contactNormal, direction) > 0.3f)
            contactNormal = Vector3.Slerp(contactNormal, direction, 0.2f).normalized;
        lastZone = zone;
        Vector3 center = surface.transform.TransformPoint(surface.center);
        Vector3 palmCenter = palm.transform.TransformPoint(palmCollider.center);
        float radius = surface.radius * LargestScale(surface.transform.lossyScale) +
                       palmCollider.radius * LargestScale(palm.transform.lossyScale) - furCompression;
        float outwardOffset = Mathf.Clamp(radius - Vector3.Dot(palmCenter - center, contactNormal), 0f, maxVisualOffset);
        transform.position += contactNormal * outwardOffset;
        VisualOffset = outwardOffset;
    }
    void OnDisable() { transform.localPosition = restLocalPosition; VisualOffset = 0f; }
    static float LargestScale(Vector3 scale)
    {
        return Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }
}