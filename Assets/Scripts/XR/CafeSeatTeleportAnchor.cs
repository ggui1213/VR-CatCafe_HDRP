using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

public class CafeSeatTeleportAnchor : TeleportationAnchor
{
    public CafeSeatingController seating;
    public CatPerchSpot seat;
    public float eyeHeightAboveSeat = 0.75f;

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        return base.IsSelectableBy(interactor) && seat && !seat.Occupant;
    }

    protected override bool GenerateTeleportRequest(IXRInteractor interactor,
        RaycastHit hit, ref TeleportRequest request)
    {
        if (!seating || !seating.origin || !seat || seat.Occupant ||
            !base.GenerateTeleportRequest(interactor, hit, ref request)) return false;
        var origin = seating.origin;
        // Provider positions the body ground point; compensate tracked eye height only once.
        var trackedHeight = Vector3.Dot(origin.Camera.transform.position - origin.transform.position, Vector3.up);
        request.destinationPosition.y = seat.transform.position.y + eyeHeightAboveSeat - trackedHeight;
        seating.Sit(this);
        return true;
    }
}
