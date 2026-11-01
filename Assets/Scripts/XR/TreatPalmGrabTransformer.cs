using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

public class TreatPalmGrabTransformer : XRBaseGrabTransformer
{
    public CatTreatTube treat;
    public override void Process(XRGrabInteractable interactable, XRInteractionUpdateOrder.UpdatePhase phase,
        ref Pose targetPose, ref Vector3 localScale)
    {
        if (!treat || interactable.interactorsSelecting.Count == 0) return;
        if (treat.TryGetGripPose(out var pose)) targetPose = pose;
        else
        {
            var attach = interactable.interactorsSelecting[0].GetAttachTransform(interactable);
            targetPose = new Pose(attach.position, attach.rotation * treat.GripRotation);
        }
    }
}
