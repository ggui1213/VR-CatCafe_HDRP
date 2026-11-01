using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>Free floor teleportation with standing room checks for the cafe.</summary>
public class CatCafeTeleportationArea : TeleportationArea
{
    [SerializeField] private Transform playerRoot;
    [SerializeField] private float floorHeight;
    [SerializeField, Min(0.1f)] private float clearanceRadius = 0.25f;
    [SerializeField, Min(1f)] private float clearanceHeight = 1.8f;
    private readonly Collider[] obstacles = new Collider[64];

    public bool IsDestinationClear(Vector3 point)
    {
        if (Mathf.Abs(point.y - floorHeight) > 0.15f)
            return false;

        var bottom = point + Vector3.up * (clearanceRadius + 0.04f);
        var top = point + Vector3.up * (clearanceHeight - clearanceRadius);
        var count = Physics.OverlapCapsuleNonAlloc(bottom, top, clearanceRadius,
            obstacles, ~0, QueryTriggerInteraction.Ignore);
        if (count == obstacles.Length)
            return false;
        for (var i = 0; i < count; i++)
        {
            if (playerRoot != null && obstacles[i].transform.IsChildOf(playerRoot))
                continue;
            return false;
        }
        return true;
    }

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        return base.IsSelectableBy(interactor) &&
            interactor is XRRayInteractor ray &&
            ray.TryGetCurrent3DRaycastHit(out var hit) &&
            IsDestinationClear(hit.point);
    }

    protected override bool GenerateTeleportRequest(IXRInteractor interactor,
        RaycastHit hit, ref TeleportRequest request)
    {
        if (!IsDestinationClear(hit.point) ||
            !base.GenerateTeleportRequest(interactor, hit, ref request)) return false;
        var seating = playerRoot ? playerRoot.GetComponent<CafeSeatingController>() : null;
        if (seating) seating.LeaveSeat();
        return true;
    }
}
