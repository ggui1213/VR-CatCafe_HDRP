using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
[RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
public class RoomGrabbablePhysics : MonoBehaviour
{
    XRGrabInteractable grab;
    Rigidbody body;
    bool wasHeld, released;
    void Awake() { grab = GetComponent<XRGrabInteractable>(); body = GetComponent<Rigidbody>(); }
    void LateUpdate()
    {
        if (grab.isSelected) { wasHeld = true; return; }
        if (wasHeld) { wasHeld = false; released = true; }
        if (released && body.isKinematic) { body.isKinematic = false; body.useGravity = true; }
    }
}
