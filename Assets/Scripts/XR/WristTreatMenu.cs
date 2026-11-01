using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Look at the raised left palm, select a treat, then grab the offered packet.</summary>
public class WristTreatMenu : MonoBehaviour
{
    public Transform leftHand, head, panel, pickupPoint;
    public CatTreatTube treat;
    public Button offerButton, putAwayButton;
    public TMP_Text status;
    public bool forceVisible;
    float visibleUntil;
    int statusMode = -1, displayedPercent = -1;
    void Awake()
    {
        offerButton.onClick.AddListener(OfferTreat);
        putAwayButton.onClick.AddListener(PutAwayTreat);
    }
    void LateUpdate()
    {
        if (!leftHand || !head) return;
        Vector3 toHead = head.position - leftHand.position;
        bool palmUp = Vector3.Dot(-leftHand.up, toHead.normalized) > 0.45f;
        bool raised = toHead.magnitude < 0.85f && toHead.magnitude > 0.18f && leftHand.position.y > head.position.y - 0.55f;
        if (palmUp && raised) visibleUntil = Time.time + 0.7f;
        bool visible = forceVisible || Time.time < visibleUntil;
        panel.gameObject.SetActive(visible);
        Vector3 right = Vector3.ProjectOnPlane(head.right, Vector3.up).normalized;
        panel.position = leftHand.position + Vector3.up * 0.10f + right * 0.10f;
        panel.rotation = Quaternion.LookRotation(panel.position - head.position, Vector3.up);
        pickupPoint.position = leftHand.position + right * 0.24f + Vector3.up * 0.015f;
        pickupPoint.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(head.forward, Vector3.up), Vector3.up);
        offerButton.interactable = !treat.IsHeld;
        putAwayButton.interactable = !treat.IsHeld && treat.IsOffered;
        if (!visible) return;
        int mode = treat.IsHeld ? (treat.HasFood ? 2 : 3) : treat.IsOffered ? 1 : 0;
        int percent = mode == 2 ? Mathf.CeilToInt(treat.remaining * 100f) : -1;
        if (statusMode == mode && displayedPercent == percent) return;
        statusMode = mode;
        displayedPercent = percent;
        status.text = treat.IsHeld ? (treat.HasFood ? "Bring the tip to a cat's mouth\nSqueeze TRIGGER gently  •  " + percent + "%" : "All finished!\nRelease GRIP for a fresh treat")
            : treat.IsOffered ? "GRIP to pick up\nTRIGGER to squeeze  •  Release GRIP to put away" : "Raise your left palm to choose\nInvite nearby cats with a creamy treat";
    }
    public void OfferTreat() { treat.Offer(); visibleUntil = Time.time + 3f; }
    public void PutAwayTreat() { treat.PutAway(); }
    void OnDestroy()
    {
        if (offerButton) offerButton.onClick.RemoveListener(OfferTreat);
        if (putAwayButton) putAwayButton.onClick.RemoveListener(PutAwayTreat);
    }
}
