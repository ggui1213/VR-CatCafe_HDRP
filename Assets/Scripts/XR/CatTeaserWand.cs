using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class CatTeaserWand : MonoBehaviour
{
    public XRGrabInteractable grab;
    public Rigidbody handle, lure;
    public Transform tetherAnchor, player, hintRoot;
    public Rigidbody[] tetherBodies;
    public LineRenderer tetherLine;
    public InputActionReference[] locomotionActions;
    [Range(0f, 85f)] public float heldForwardTilt = 70f;
    // The shaft runs along +Y and bends toward +X. Roll its bend toward +Z
    // before tilting it forward to match the controller-driven hand model.
    public Quaternion HeldGripRotation => Quaternion.Euler(heldForwardTilt, 0f, 0f) *
        Quaternion.AngleAxis(-90f, Vector3.up);
    public Vector3 palmGripOffset = new Vector3(0f, -0.018f, 0.025f);
    public Vector3 ToyAimPosition => lure.worldCenterOfMass + lure.transform.TransformVector(new Vector3(0, -0.07f, 0));
    public CatToyChaser AttractedCat { get; private set; }
    public bool IsHeld => grab && grab.isSelected;
    public int BatCount { get; private set; }
    float hintUntil, nextSearch, nextBat;
    Vector3 previousPlayer, previousAnchor, spawnPosition;
    Quaternion spawnRotation;
    Vector3[] tetherSpawnOffsets;
    Quaternion[] tetherSpawnRotations;
    Transform heldHandModel;
    Transform gripPalm;
    ControllerHandPoser heldHandPoser;
    PettingHandSurfaceContact heldSurfaceContact;
    bool restoreSurfaceContact;
    Vector3 originalHandPosition;
    Quaternion originalHandRotation;
    bool session;

    void Awake()
    {
        if (!player)
        {
            var origin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (origin) player = origin.transform;
        }
        spawnPosition = handle.position;
        spawnRotation = handle.rotation;
        tetherSpawnOffsets = new Vector3[tetherBodies.Length];
        tetherSpawnRotations = new Quaternion[tetherBodies.Length];
        for (int i = 0; i < tetherBodies.Length; i++)
        {
            tetherSpawnOffsets[i] = tetherBodies[i].position - spawnPosition;
            tetherSpawnRotations[i] = tetherBodies[i].rotation;
        }
        grab.throwOnDetach = false;
        if (grab.attachTransform)
            grab.attachTransform.localRotation = Quaternion.Inverse(HeldGripRotation);
        previousAnchor = tetherAnchor.position;
        if (player) previousPlayer = player.position;
        grab.selectEntered.AddListener(OnPickedUp);
        grab.selectExited.AddListener(OnDropped);
        // Non-adjacent links should not fight one another when the line folds.
        var ownColliders = GetComponentsInChildren<Collider>();
        foreach (var a in ownColliders)
            foreach (var b in ownColliders)
                if (a != b && a.attachedRigidbody && b.attachedRigidbody)
                    Physics.IgnoreCollision(a, b);
    }

    void OnPickedUp(SelectEnterEventArgs args)
    {
        RestoreHandPose();
        // Tilt only the rendered hand; controller tracking and ray origins remain accurate.
        for (var parent = args.interactorObject.transform; parent && parent != player; parent = parent.parent)
        {
            var poser = parent.GetComponentInChildren<ControllerHandPoser>(true);
            if (!poser) continue;
            heldHandModel = poser.transform;
            heldHandPoser = poser;
            heldHandPoser.IsHoldingWand = true;
            heldSurfaceContact = poser.GetComponent<PettingHandSurfaceContact>();
            restoreSurfaceContact = heldSurfaceContact && heldSurfaceContact.enabled;
            if (restoreSurfaceContact) heldSurfaceContact.enabled = false;
            originalHandPosition = heldHandModel.localPosition;
            originalHandRotation = heldHandModel.localRotation;
            heldHandModel.localRotation = Quaternion.Euler(heldForwardTilt, 0f, 0f) * originalHandRotation;
            var palm = poser.GetComponentInChildren<PettingHand>(true);
            gripPalm = palm ? palm.transform : heldHandModel;
            break;
        }
        session = true;
        nextSearch = 0f;
    }
    void OnDropped(SelectExitEventArgs _)
    {
        session = false;
        if (AttractedCat) AttractedCat.EndPlay();
        AttractedCat = null;
        RestoreHandPose();
        ResetToStand();
        ShowPickupHint();
    }

    void Update()
    {
        if (!player || !handle || !lure) return;
        bool moving = (player.position - previousPlayer).sqrMagnitude > 0.0004f;
        previousPlayer = player.position;
        if (locomotionActions != null)
            foreach (var reference in locomotionActions)
            {
                if (!reference || reference.action == null || !reference.action.enabled) continue;
                var value = reference.action.ReadValueAsObject();
                moving |= value is Vector2 axis && axis.sqrMagnitude > 0.04f;
                moving |= value is float button && button > 0.2f;
            }
        if (!IsHeld && moving) ShowPickupHint();
        if (session && IsHeld && !AttractedCat && Time.time >= nextSearch)
        {
            nextSearch = Time.time + 1f;
            var candidates = FindObjectsByType<CatToyChaser>(FindObjectsSortMode.None);
            System.Array.Sort(candidates, (a, b) =>
                (a.transform.position - player.position).sqrMagnitude.CompareTo(
                    (b.transform.position - player.position).sqrMagnitude));
            foreach (var candidate in candidates)
                if (candidate.BeginPlay(this)) { AttractedCat = candidate; break; }
        }
        if (AttractedCat && !AttractedCat.IsPlaying) AttractedCat = null;
        if (!IsHeld && handle.position.y < -1f) ResetToStand();
    }

    public void ShowPickupHint() { hintUntil = Time.time + 7f; }

    public bool TryGetPalmGripPose(out Pose pose)
    {
        pose = default;
        if (!heldHandModel || !gripPalm) return false;
        pose.position = gripPalm.position + heldHandModel.TransformVector(palmGripOffset);
        pose.rotation = heldHandModel.parent.rotation * HeldGripRotation;
        return true;
    }

    void FixedUpdate()
    {
        // A teleport moves the hand discontinuously. Move the tether too, rather than stretching joints across the room.
        Vector3 delta = tetherAnchor.position - previousAnchor;
        if (delta.sqrMagnitude > 1.44f)
            foreach (var body in tetherBodies)
            {
                body.position += delta;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        previousAnchor = tetherAnchor.position;
    }

    void LateUpdate()
    {
        if (tetherLine && tetherBodies != null)
        {
            tetherLine.positionCount = tetherBodies.Length + 1;
            tetherLine.SetPosition(0, tetherAnchor.position);
            for (int i = 0; i < tetherBodies.Length; i++) tetherLine.SetPosition(i + 1, tetherBodies[i].position);
        }
        if (!hintRoot || !player) return;
        hintRoot.gameObject.SetActive(!IsHeld && Time.time < hintUntil &&
            (player.position - handle.position).sqrMagnitude < 64f);
        hintRoot.position = handle.position + Vector3.up * 0.28f;
        var camera = Camera.main;
        if (camera) hintRoot.rotation = Quaternion.LookRotation(hintRoot.position - camera.transform.position);
    }

    public bool Bat(Vector3 pawPosition, Vector3 direction, bool catchToy)
    {
        if (!IsHeld || Time.time < nextBat || Vector3.Distance(pawPosition, lure.position) > 0.3f) return false;
        nextBat = Time.time + 0.35f;
        BatCount++;
        // A short spring tug reads as a catch; releasing it produces the elastic rebound.
        if (catchToy && AttractedCat) AttractedCat.StartCatch();
        lure.AddForce((Vector3.up * 1.3f + direction.normalized * 0.55f) * lure.mass, ForceMode.Impulse);
        lure.AddTorque(Random.insideUnitSphere * 0.00015f, ForceMode.Impulse);
        return true;
    }

    public void ResetToStand()
    {
        if (!handle.isKinematic)
        {
            handle.linearVelocity = Vector3.zero;
            handle.angularVelocity = Vector3.zero;
        }
        handle.isKinematic = true;
        handle.useGravity = false;
        handle.position = spawnPosition;
        handle.rotation = spawnRotation;
        handle.transform.SetPositionAndRotation(spawnPosition, spawnRotation);
        for (int i = 0; i < tetherBodies.Length; i++)
        {
            var body = tetherBodies[i];
            body.position = spawnPosition + tetherSpawnOffsets[i];
            body.rotation = tetherSpawnRotations[i];
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        previousAnchor = tetherAnchor.position;
    }

    void RestoreHandPose()
    {
        if (heldHandModel)
        {
            heldHandModel.localRotation = originalHandRotation;
            heldHandModel.localPosition = originalHandPosition;
        }
        if (heldSurfaceContact && restoreSurfaceContact) heldSurfaceContact.enabled = true;
        if (heldHandPoser) heldHandPoser.IsHoldingWand = false;
        heldHandPoser = null;
        heldSurfaceContact = null;
        restoreSurfaceContact = false;
        gripPalm = null;
        heldHandModel = null;
    }

    void OnDisable()
    {
        RestoreHandPose();
        session = false;
        if (AttractedCat) AttractedCat.EndPlay();
        AttractedCat = null;
    }

    void OnDestroy()
    {
        if (!grab) return;
        grab.selectEntered.RemoveListener(OnPickedUp);
        grab.selectExited.RemoveListener(OnDropped);
    }
}
