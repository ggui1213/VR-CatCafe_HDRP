using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

/// <summary>Places the grip in the visible palm instead of the controller's ray attach point.</summary>
public class WandPalmGrabTransformer : XRBaseGrabTransformer
{
    public CatTeaserWand wand;

    public override void Process(XRGrabInteractable interactable,
        XRInteractionUpdateOrder.UpdatePhase phase, ref Pose targetPose, ref Vector3 localScale)
    {
        if (!wand || interactable.interactorsSelecting.Count == 0) return;
        if (wand.TryGetPalmGripPose(out var pose)) targetPose = pose;
        else
        {
            var attach = interactable.interactorsSelecting[0].GetAttachTransform(interactable);
            targetPose = new Pose(attach.position, attach.rotation * wand.HeldGripRotation);
        }
    }
}
