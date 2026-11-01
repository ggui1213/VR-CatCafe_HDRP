using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>A grabbable squeeze treat. Only food actually delivered to one mouth is consumed.</summary>
public class CatTreatTube : MonoBehaviour
{
    public XRGrabInteractable grab;
    public Rigidbody body;
    public Transform tip, wrapper, paste, visuals;
    public Transform player, home;
    public InputActionReference leftTrigger, rightTrigger;
    public float attractionRadius = 5f;
    public float mouthEnterDistance = 0.13f, mouthExitDistance = 0.19f;
    public float foodPerSecond = 0.16f;
    [Range(0f, 1f)] public float remaining = 1f;
    public bool IsHeld => grab && grab.isSelected;
    public bool HasFood => remaining > 0.001f;
    public float Squeeze { get; private set; }
    public CatTreatFeeding FeedingCat { get; private set; }
    public Vector3 TipPosition => tip.position;
    public Quaternion GripRotation => Quaternion.Euler(55f, 0f, 0f);
    public int NearbyCats => attracted.Count;
    public bool IsOffered { get; private set; }
    public float TotalDelivered { get; private set; }
    readonly List<CatTreatFeeding> attracted = new List<CatTreatFeeding>();
    CatTreatFeeding[] cats;
    ControllerHandPoser hand;
    PettingHand palm;
    PettingHandSurfaceContact surface;
    IXRSelectInteractor interactor;
    Quaternion handRotation;
    bool restoreSurface, restorePalm, rightHand;
    Vector3 wrapperScale, pasteScale, lastTip, crowdOrigin, crowdForward;
    float nextSearch, nextHaptic;
    bool hasCrowdAnchor;
    static readonly float[] Angles = { -18f, 18f, -54f, 54f };
    static readonly ProfilerMarker RecruitMarker = new ProfilerMarker("CatCafe.Treat.Recruit");
    static readonly ProfilerMarker FeedMarker = new ProfilerMarker("CatCafe.Treat.Feed");

    void Awake()
    {
        if (!player && Camera.main) player = Camera.main.transform;
        cats = FindObjectsByType<CatTreatFeeding>(FindObjectsSortMode.None);
        wrapperScale = wrapper.localScale;
        pasteScale = paste.localScale;
        grab.throwOnDetach = false;
        grab.selectEntered.AddListener(PickedUp);
        grab.selectExited.AddListener(Dropped);
        SetVisible(false);
    }
    public void Offer()
    {
        if (IsHeld) return;
        remaining = 1f;
        IsOffered = true;
        SetVisible(true);
        ReturnHome();
    }
    public void PutAway()
    {
        if (IsHeld) return;
        IsOffered = false;
        ReleaseCats();
        SetVisible(false);
    }
    void SetVisible(bool visible)
    {
        visuals.gameObject.SetActive(visible);
        foreach (var col in grab.colliders) if (col) col.enabled = visible;
    }
    void PickedUp(SelectEnterEventArgs args)
    {
        RestoreHand();
        interactor = args.interactorObject;
        for (var parent = interactor.transform; parent && parent != player; parent = parent.parent)
        {
            hand = parent.GetComponentInChildren<ControllerHandPoser>(true);
            if (hand) break;
        }
        if (hand)
        {
            rightHand = hand.name.Contains("Right");
            hand.IsHoldingTreat = true;
            handRotation = hand.transform.localRotation;
            hand.transform.localRotation = GripRotation * handRotation;
            palm = hand.GetComponentInChildren<PettingHand>(true);
            restorePalm = palm && palm.enabled;
            if (restorePalm) palm.enabled = false;
            surface = hand.GetComponent<PettingHandSurfaceContact>();
            restoreSurface = surface && surface.enabled;
            if (restoreSurface) surface.enabled = false;
        }
        else rightHand = interactor.transform.name.Contains("Right");
        hasCrowdAnchor = false;
        nextSearch = 0f;
        lastTip = tip.position;
        Pulse(0.12f, 0.05f);
    }
    void Dropped(SelectExitEventArgs args)
    {
        ReleaseCats();
        RestoreHand();
        IsOffered = false;
        SetVisible(false);
        ReturnHome();
    }
    public bool TryGetGripPose(out Pose pose)
    {
        pose = default;
        if (!hand || !hand.transform.parent) return false;
        var grip = palm ? palm.transform : hand.transform;
        pose.position = grip.position + hand.transform.TransformVector(new Vector3(0f, -0.015f, 0.02f));
        pose.rotation = hand.transform.parent.rotation * GripRotation;
        return true;
    }
    void Update()
    {
        if (!IsHeld) { Squeeze = 0f; return; }
        var action = rightHand ? rightTrigger : leftTrigger;
        float pressure = hand ? hand.TriggerValue : action && action.action != null && action.action.enabled ? action.action.ReadValue<float>() : 0f;
        Squeeze = HasFood ? Mathf.InverseLerp(0.12f, 0.9f, pressure) : 0f;
        if (HasFood && Time.time >= nextSearch) { RecruitCats(); nextSearch = Time.time + 0.65f; }
        if (!HasFood) ReleaseCats();
        wrapper.localScale = Vector3.Scale(wrapperScale, new Vector3(1f + Squeeze * 0.035f, 1f, Mathf.Lerp(0.35f, 1f, remaining) * (1f - Squeeze * 0.22f)));
        paste.gameObject.SetActive(HasFood && Squeeze > 0.08f);
        paste.localScale = Vector3.Scale(pasteScale, new Vector3(1f, Mathf.Lerp(0.35f, 1f, Squeeze), 1f));
    }
    void LateUpdate()
    {
        using (FeedMarker.Auto()) UpdateFeedingContact();
    }
    void UpdateFeedingContact()
    {
        if (!IsHeld) { if (IsOffered) ReturnHome(); return; }
        float speed = Vector3.Distance(lastTip, tip.position) / Mathf.Max(0.001f, Time.deltaTime);
        lastTip = tip.position;
        if (Squeeze < 0.08f || speed > 0.8f || !HasFood) { FeedingCat = null; return; }
        CatTreatFeeding nearest = null;
        float best = mouthEnterDistance;
        if (FeedingCat && FeedingCat.CanReceiveFood(this, mouthExitDistance)) nearest = FeedingCat;
        else foreach (var cat in attracted)
        {
            if (!cat || !cat.CanReceiveFood(this, mouthEnterDistance)) continue;
            float distance = Vector3.Distance(tip.position, cat.MouthPosition);
            if (distance < best) { best = distance; nearest = cat; }
        }
        FeedingCat = nearest;
        if (!nearest) return;
        float amount = Mathf.Min(remaining, foodPerSecond * Squeeze * Time.deltaTime);
        if (!nearest.ReceiveFood(this, amount)) return;
        remaining = Mathf.Max(0f, remaining - amount);
        TotalDelivered += amount;
        if (Time.time >= nextHaptic)
        {
            nextHaptic = Time.time + 0.12f;
            Pulse(0.045f + 0.065f * Squeeze, 0.045f);
        }
    }
    void RecruitCats()
    {
        using (RecruitMarker.Auto()) RecruitNearbyCats();
    }
    void RecruitNearbyCats()
    {
        if (!player) return;
        attracted.RemoveAll(cat => !cat || cat.Treat != this);
        Vector3 forward = Vector3.ProjectOnPlane(player.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
        Vector3 origin = player.position;
        origin.y = 0f;
        bool moved = !hasCrowdAnchor || Vector3.Distance(origin, crowdOrigin) > 0.45f || Vector3.Angle(forward, crowdForward) > 50f;
        if (moved) { crowdOrigin = origin; crowdForward = forward; hasCrowdAnchor = true; }
        if (cats == null || cats.Length == 0) cats = FindObjectsByType<CatTreatFeeding>(FindObjectsSortMode.None);
        foreach (var cat in cats)
        {
            if (!cat || (cat.transform.position - player.position).sqrMagnitude > attractionRadius * attractionRadius) continue;
            if (cat.Treat == this) { if (moved) cat.UpdateWaitingSpot(WaitingSpot(cat.Slot)); continue; }
            for (int slot = 0; slot < Angles.Length; slot++)
            {
                bool occupied = attracted.Exists(other => other && other.Slot == slot);
                if (occupied) continue;
                if (cat.TryAttract(this, WaitingSpot(slot), slot)) { attracted.Add(cat); break; }
            }
        }
    }
    Vector3 WaitingSpot(int slot) => crowdOrigin + Quaternion.AngleAxis(Angles[Mathf.Clamp(slot, 0, 3)], Vector3.up) * crowdForward * 1.15f;
    public bool IsWaitingSpotClear(CatTreatFeeding cat, Vector3 position)
    {
        foreach (var other in attracted)
            if (other && other != cat && other.Treat == this && Vector3.Distance(other.WaitingPosition, position) < 0.42f) return false;
        return true;
    }
    void ReleaseCats()
    {
        FeedingCat = null;
        if (attracted.Count == 0) return;
        foreach (var cat in attracted.ToArray()) if (cat && cat.Treat == this) cat.EndAttraction();
        attracted.Clear();
    }
    void ReturnHome()
    {
        if (!home || !body) return;
        body.isKinematic = true;
        body.transform.SetPositionAndRotation(home.position, home.rotation);
    }
    void RestoreHand()
    {
        if (hand) { hand.IsHoldingTreat = false; hand.transform.localRotation = handRotation; }
        if (surface && restoreSurface) surface.enabled = true;
        if (palm && restorePalm) palm.enabled = true;
        hand = null; palm = null; surface = null; interactor = null;
        restorePalm = restoreSurface = false;
    }
    void Pulse(float amplitude, float duration)
    {
        var input = interactor as XRBaseInputInteractor;
        if (input) input.SendHapticImpulse(amplitude, duration);
    }
    void OnDisable() { ReleaseCats(); RestoreHand(); }
    void OnDestroy()
    {
        if (!grab) return;
        grab.selectEntered.RemoveListener(PickedUp);
        grab.selectExited.RemoveListener(Dropped);
    }
}
