using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Runs the real grab, navigation, trigger, mouth and release pipeline in Play mode.</summary>
[InitializeOnLoad]
public static class CatFeedingVerification
{
    const string Key = "CatCafe.FeedingVerification";
    static CatTreatTube tube;
    static WristTreatMenu menu;
    static XRBaseInteractor interactor;
    static Transform controller;
    static ControllerHandPoser hand;
    static CatTreatFeeding[] cats;
    static CatTreatFeeding target;
    static int stage;
    static double started, phaseAt;
    static float amountBefore, foodBefore;
    static Quaternion originalHandRotation;
    static CatFeedingVerification()
    {
        if (SessionState.GetBool(Key,false)) EditorApplication.update += Tick;
    }
    public static void Begin()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Start feeding verification from Edit mode.");
        SessionState.SetBool(Key,true);
        EditorApplication.isPlaying = true;
    }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!tube)
            {
                tube = UnityEngine.Object.FindFirstObjectByType<CatTreatTube>();
                if (tube.wrapper.GetComponent<MeshFilter>().sharedMesh.vertexCount < 100)
                    throw new Exception("Saved treat mesh is empty after entering Play mode");
                menu = UnityEngine.Object.FindFirstObjectByType<WristTreatMenu>();
                controller = GameObject.Find("Right Controller").transform;
                hand = controller.GetComponentInChildren<ControllerHandPoser>(true);
                originalHandRotation = hand.transform.localRotation;
                var tracking = controller.GetComponent<TrackedPoseDriver>();
                if (tracking) tracking.enabled = false;
                interactor = controller.GetComponentsInChildren<XRBaseInteractor>(true)
                    .First(i => i.GetType().Name.Contains("NearFar") || i.GetType().Name.Contains("RayInteractor"));
                cats = UnityEngine.Object.FindObjectsByType<CatTreatFeeding>(FindObjectsSortMode.None);
                menu.forceVisible = true;
                menu.OfferTreat();
                if (!tube.IsOffered || !tube.visuals.gameObject.activeSelf) throw new Exception("Wrist menu failed to offer a packet");
                controller.position = tube.player.position + Vector3.ProjectOnPlane(tube.player.forward,Vector3.up).normalized * 0.4f - Vector3.up*0.25f;
                interactor.StartManualInteraction((IXRSelectInteractable)tube.grab);
                if (!tube.IsHeld || !hand.IsHoldingTreat) throw new Exception("Manual XR grab did not enter held-treat pose");
                stage = 1; started = phaseAt = EditorApplication.timeSinceStartup;
                Debug.Log("FEED_QA_START: menu offer and real XR grab passed; waiting for cats to navigate and sit.");
            }
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 55d) throw new Exception("Feeding verification timed out at stage " + stage);
            if (!tube.IsHeld) throw new Exception("XR manual grab was unexpectedly released");
            if (stage == 1)
            {
                target = cats.FirstOrDefault(c => c.IsWaiting && !c.IsContent);
                if (!target) return;
                if (tube.NearbyCats == 0) throw new Exception("No cats recruited");
                var reserved = cats.Where(c => c.Treat == tube).ToArray();
                for (int a=0;a<reserved.Length;a++) for (int b=a+1;b<reserved.Length;b++)
                    if (Vector3.Distance(reserved[a].WaitingPosition,reserved[b].WaitingPosition)<0.4f)
                        throw new Exception("Cats were assigned overlapping waiting spots");
                amountBefore = tube.remaining;
                foodBefore = cats.Sum(c => c.FoodReceived);
                stage = 2; phaseAt = now; hand.previewTrigger = 1f;
                Debug.Log("FEED_QA_APPROACH_PASS: recruited=" + tube.NearbyCats + "; seated=" + target.name);
            }
            if (stage == 2 || stage == 3 || stage == 5)
            {
                controller.rotation = Quaternion.LookRotation(-target.transform.forward,Vector3.up);
                Pose pose;
                if (tube.TryGetGripPose(out pose))
                {
                    Vector3 predicted = pose.position + pose.rotation * tube.tip.localPosition;
                    Vector3 desired = target.MouthPosition + target.transform.forward * 0.055f;
                    controller.position += Vector3.ClampMagnitude(desired-predicted,0.025f);
                }
            }
            if (stage == 2 && cats.Sum(c=>c.FoodReceived)-foodBefore > 0.035f)
            {
                if (tube.remaining >= amountBefore) throw new Exception("Eating did not consume the tube");
                if (cats.Count(c=>c.FoodReceived>0.001f)!=1) throw new Exception("One squeeze fed more than one cat");
                int layer=target.GetComponent<Animator>().GetLayerIndex("Treat Mouth");
                if (layer<0 || target.GetComponent<Animator>().GetLayerWeight(layer)<0.2f) throw new Exception("Mouth animation layer did not blend in");
                stage=3; phaseAt=now; hand.previewTrigger=0f;
                Debug.Log("FEED_QA_EAT_PASS: mouth-only lick layer, pressure-based consumption and one-cat arbitration passed.");
            }
            if (stage == 3 && now-phaseAt>0.6d) { amountBefore=tube.remaining; stage=4; phaseAt=now; }
            if (stage == 4 && now-phaseAt>0.5d)
            {
                if (Mathf.Abs(tube.remaining-amountBefore)>0.001f) throw new Exception("Food consumed without squeezing");
                hand.previewTrigger=1f;
                controller.position += Vector3.up*0.6f;
                amountBefore=tube.remaining; stage=6; phaseAt=now;
            }
            if (stage == 6 && now-phaseAt>0.65d)
            {
                if (Mathf.Abs(tube.remaining-amountBefore)>0.001f) throw new Exception("Food consumed away from a cat mouth");
                stage=5; phaseAt=now; tube.remaining=0.004f;
            }
            if (stage == 5 && !tube.HasFood)
            {
                if (tube.remaining<0f) throw new Exception("Negative remaining food");
                interactor.EndManualInteraction();
                if (hand.IsHoldingTreat || Quaternion.Angle(hand.transform.localRotation,originalHandRotation)>0.01f)
                    throw new Exception("Release failed to restore the rendered hand");
                if (cats.Any(c=>c.IsBusy)) throw new Exception("Cats did not release feeding control");
                menu.OfferTreat();
                if (!tube.IsOffered || tube.remaining<0.99f) throw new Exception("Fresh packet could not be selected after depletion");
                menu.PutAwayTreat();
                Debug.Log("FEED_QA_PASS: wrist offer, real XR grab, attraction/NavMesh seating, one-mouth feeding, trigger release, distance rejection, depletion, hand restoration and refill/put-away passed.");
                Finish();
            }
        }
        catch (Exception ex) { Debug.LogError("FEED_QA_FAIL: " + ex); Finish(); }
    }
    static void Finish()
    {
        if (hand) hand.previewTrigger=0f;
        if (interactor && interactor.isPerformingManualInteraction) interactor.EndManualInteraction();
        SessionState.SetBool(Key,false);
        EditorApplication.update-=Tick;
        EditorApplication.isPlaying=false;
    }
}
