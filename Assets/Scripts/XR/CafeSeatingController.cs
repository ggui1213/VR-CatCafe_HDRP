using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;

public class CafeSeatingController : MonoBehaviour
{
    public XROrigin origin;
    public CafeSeatTeleportAnchor CurrentSeat { get; private set; }
    public CatIndoorRoaming VisitingCat { get; private set; }
    readonly List<ContinuousMoveProvider> suspendedMovement = new List<ContinuousMoveProvider>();
    Coroutine invitation;

    public void Sit(CafeSeatTeleportAnchor seat)
    {
        LeaveSeat();
        CurrentSeat = seat;
        seat.seat.playerOccupied = true;
        foreach (var move in origin.GetComponentsInChildren<ContinuousMoveProvider>(true))
            if (move.enabled) { suspendedMovement.Add(move); move.enabled = false; }
        invitation = StartCoroutine(InviteCat());
    }

    IEnumerator InviteCat()
    {
        yield return new WaitForSeconds(1f);
        var cats = new List<CatIndoorRoaming>(FindObjectsByType<CatIndoorRoaming>(FindObjectsSortMode.None));
        for (int i = cats.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var swap = cats[i]; cats[i] = cats[j]; cats[j] = swap;
        }
        var spots = new List<CatPerchSpot>();
        foreach (var spot in FindObjectsByType<CatPerchSpot>(FindObjectsSortMode.None))
            if (spot.name.StartsWith("Table surface") &&
                Vector3.Distance(spot.transform.position, CurrentSeat.seat.transform.position) < 2.5f)
                spots.Add(spot);
        spots.Sort((a, b) => (a.transform.position - CurrentSeat.seat.transform.position).sqrMagnitude
            .CompareTo((b.transform.position - CurrentSeat.seat.transform.position).sqrMagnitude));
        foreach (var cat in cats)
            foreach (var spot in spots)
                if (cat && cat.RequestTableVisit(spot))
                {
                    VisitingCat = cat;
                    cat.player = origin.Camera.transform;
                    var gaze = cat.GetComponent<CatRestingGaze>();
                    if (gaze) gaze.player = origin.Camera.transform;
                    invitation = null;
                    yield break;
                }
        Debug.Log("No idle cat has a clear route to a nearby table.", this);
        invitation = null;
    }

    public void LeaveSeat()
    {
        if (invitation != null) { StopCoroutine(invitation); invitation = null; }
        if (CurrentSeat && CurrentSeat.seat) CurrentSeat.seat.playerOccupied = false;
        CurrentSeat = null;
        VisitingCat = null;
        foreach (var move in suspendedMovement) if (move) move.enabled = true;
        suspendedMovement.Clear();
    }

    void OnDisable() { LeaveSeat(); }
}
