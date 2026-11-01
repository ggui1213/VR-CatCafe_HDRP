using UnityEngine;
using UnityEngine.AI;
using Unity.Profiling;

/// <summary>Owns locomotion while a cat approaches, waits for, and eats a held treat.</summary>
[RequireComponent(typeof(CatIndoorRoaming), typeof(NavMeshAgent), typeof(PettableCat))]
public class CatTreatFeeding : MonoBehaviour
{
    public Transform mouth;
    public AudioClip[] hungryMeows;
    public float foodUntilSatisfied = 0.28f;
    public float satisfiedCooldown = 18f;
    public CatTreatTube Treat { get; private set; }
    public int Slot { get; private set; }
    public bool IsBusy => Treat != null;
    public bool IsWaiting => IsBusy && (stage == Stage.Waiting || stage == Stage.Eating || stage == Stage.Satisfied);
    public bool IsEating => IsBusy && stage == Stage.Eating && Time.time < lastFoodAt + 0.22f;
    public bool IsContent => IsBusy && stage == Stage.Satisfied;
    public string Activity => stage.ToString();
    public Vector3 MouthPosition => mouth ? mouth.position : transform.TransformPoint(new Vector3(0f, 0.24f, 0.2f));
    public Vector3 LookTarget => Treat ? Treat.TipPosition : transform.position + transform.forward;
    public Vector3 WaitingPosition => waitingSpot;
    public float FoodReceived { get; private set; }
    enum Stage { LieToSit, StandUp, Approaching, Facing, SitDown, Waiting, Eating, Satisfied }
    Stage stage;
    CatIndoorRoaming roaming;
    CatPerching perching;
    CatToyChaser toy;
    PettableCat cat;
    Animator animator;
    NavMeshAgent agent;
    NavMeshPath path;
    Vector3 waitingSpot;
    float readyAt, nextMeow, gestureUntil, lastFoodAt = -100f, mealFood, cooldownUntil;
    int mouthLayer = -1;
    string playingState;
    static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("CatCafe.Treat.CatAI");
    static readonly ProfilerMarker NavigationMarker = new ProfilerMarker("CatCafe.Treat.Navigation");

    void Awake()
    {
        roaming = GetComponent<CatIndoorRoaming>();
        perching = GetComponent<CatPerching>();
        toy = GetComponent<CatToyChaser>();
        cat = GetComponent<PettableCat>();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        path = new NavMeshPath();
        mouthLayer = animator.GetLayerIndex("Treat Mouth");
    }
    public bool TryAttract(CatTreatTube treat, Vector3 spot, int slot)
    {
        if (!isActiveAndEnabled || IsBusy || !treat || !treat.IsHeld || !treat.HasFood || Time.time < cooldownUntil ||
            cat.IsBeingPetted || !agent.enabled || !agent.isOnNavMesh || (perching && perching.IsBusy)) return false;
        if (!FindSpot(spot, out var sampled) || !treat.IsWaitingSpotClear(this, sampled)) return false;
        if (toy && toy.IsPlaying) toy.EndPlay();
        if (perching) perching.CancelPlan();
        Treat = treat;
        Slot = slot;
        waitingSpot = sampled;
        mealFood = 0f;
        lastFoodAt = -100f;
        agent.ResetPath();
        agent.isStopped = true;
        roaming.ResetTurnAnimation();
        var state = animator.GetCurrentAnimatorStateInfo(0);
        bool lying = state.IsName("Lie") || state.IsName("Sleep") || state.IsName("SitToLie");
        bool upright = state.IsName("Walk") || state.IsName("Turn") || state.IsName("StandUp");
        stage = lying ? Stage.LieToSit : upright ? Stage.Approaching : Stage.StandUp;
        Play(lying ? "LieToSit" : upright ? "Turn" : "StandUp");
        readyAt = Time.time + (lying ? 1.4f : upright ? 0.18f : roaming.standUpDuration);
        nextMeow = Time.time + Random.Range(2f, 5f) + slot * 0.55f;
        return true;
    }
    bool FindSpot(Vector3 wanted, out Vector3 sampled)
    {
        using (NavigationMarker.Auto()) return FindNavigableSpot(wanted, out sampled);
    }
    bool FindNavigableSpot(Vector3 wanted, out Vector3 sampled)
    {
        wanted.y = transform.position.y;
        sampled = default;
        if (!NavMesh.SamplePosition(wanted, out var hit, 0.4f,
            new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask }) ||
            Mathf.Abs(hit.position.y - transform.position.y) > 0.18f || !roaming.indoorBounds.Contains(hit.position) ||
            !agent.CalculatePath(hit.position, path) || path.status != NavMeshPathStatus.PathComplete) return false;
        sampled = hit.position;
        return true;
    }
    public void UpdateWaitingSpot(Vector3 wanted)
    {
        if (!IsBusy || stage == Stage.Satisfied || !agent.enabled || !agent.isOnNavMesh || !FindSpot(wanted, out var spot) || !Treat.IsWaitingSpotClear(this, spot)) return;
        if (Vector3.Distance(spot, waitingSpot) < 0.35f) return;
        waitingSpot = spot;
        if (IsWaiting || stage == Stage.SitDown)
        {
            stage = Stage.StandUp;
            Play("StandUp");
            readyAt = Time.time + roaming.standUpDuration;
        }
        else if (stage == Stage.Approaching || stage == Stage.Facing) { stage = Stage.Approaching; agent.SetPath(path); }
    }
    void Update()
    {
        using (UpdateMarker.Auto()) UpdateFeeding();
    }
    void UpdateFeeding()
    {
        if (!IsBusy) return;
        if (!Treat.IsHeld || !Treat.HasFood || !agent.enabled || !agent.isOnNavMesh ||
            Vector3.Distance(transform.position, Treat.player.position) > Treat.attractionRadius + 2f)
        { EndAttraction(); return; }
        if (mouthLayer >= 0)
            animator.SetLayerWeight(mouthLayer, Mathf.MoveTowards(animator.GetLayerWeight(mouthLayer), IsEating ? 1f : 0f, Time.deltaTime * 7f));
        if (stage == Stage.Satisfied) cat.ReportFoodInterest(0.5f);
        if (Time.time < readyAt) return;
        if (stage == Stage.LieToSit)
        { stage = Stage.StandUp; Play("StandUp"); readyAt = Time.time + roaming.standUpDuration; return; }
        if (stage == Stage.StandUp) stage = Stage.Approaching;
        if (stage == Stage.Approaching)
        {
            if (cat.IsBeingPetted) { EndAttraction(); return; }
            if (!agent.hasPath)
            {
                if (!FindSpot(waitingSpot, out waitingSpot)) { EndAttraction(); return; }
                agent.SetPath(path);
            }
            if (agent.pathPending) return;
            if (agent.pathStatus != NavMeshPathStatus.PathComplete) { EndAttraction(); return; }
            float distance = Vector3.ProjectOnPlane(waitingSpot - transform.position, Vector3.up).magnitude;
            if (distance < 0.2f || agent.remainingDistance < agent.stoppingDistance + 0.06f)
            {
                agent.isStopped = true;
                agent.ResetPath();
                stage = Stage.Facing;
            }
            else
            {
                Vector3 direction = agent.steeringTarget - transform.position;
                float error = Heading(direction);
                bool walk = Mathf.Abs(error) < 70f;
                agent.isStopped = !walk;
                agent.speed = roaming.walkingSpeed * 1.1f * Mathf.Clamp(Mathf.Cos(error * Mathf.Deg2Rad), 0.25f, 1f);
                roaming.UpdateTurnAnimation(error, walk ? roaming.walkTurnLimit : roaming.stationaryTurnLimit);
                Play(walk ? "Walk" : "Turn");
                return;
            }
        }
        if (stage == Stage.Facing)
        {
            float error = Heading(Treat.player.position - transform.position);
            roaming.UpdateTurnAnimation(error, roaming.stationaryTurnLimit);
            Play("Turn");
            if (Mathf.Abs(error) < 8f)
            {
                roaming.ResetTurnAnimation();
                stage = Stage.SitDown;
                Play("SitDown");
                readyAt = Time.time + roaming.sitDownDuration;
            }
            return;
        }
        if (stage == Stage.SitDown) { stage = Stage.Waiting; Play("Sit"); }
        if (stage == Stage.Satisfied) { EndAttraction(); return; }
        if (stage == Stage.Eating && !IsEating) { stage = Stage.Waiting; Play("Sit"); }
        if (IsWaiting)
        {
            cat.ReportFoodInterest(IsEating ? 0.7f : 0.22f);
            if (IsEating || cat.IsBeingPetted) return;
            if (Time.time >= gestureUntil && playingState != "Sit") Play("Sit");
            if (Time.time >= nextMeow)
            {
                nextMeow = Time.time + Random.Range(5.5f, 9f);
                gestureUntil = Time.time + 1.6f;
                Play("MeowSit");
                if (cat.voiceSource && !cat.voiceSource.isPlaying && hungryMeows != null && hungryMeows.Length > 0)
                    cat.voiceSource.PlayOneShot(hungryMeows[Random.Range(0, hungryMeows.Length)], 0.55f);
            }
        }
    }
    public bool CanReceiveFood(CatTreatTube treat, float distance)
    {
        if (Treat != treat || !IsWaiting || stage == Stage.Satisfied || cat.IsBeingPetted ||
            !mouth || Time.time < readyAt || Vector3.Distance(MouthPosition, treat.TipPosition) > distance) return false;
        Vector3 toTreat = treat.TipPosition - MouthPosition;
        if (toTreat.sqrMagnitude > 0.0004f && Vector3.Dot(transform.forward, toTreat.normalized) < -0.1f) return false;
        return true;
    }
    public bool ReceiveFood(CatTreatTube treat, float amount)
    {
        if (amount <= 0f || !CanReceiveFood(treat, treat.mouthExitDistance)) return false;
        stage = Stage.Eating;
        lastFoodAt = Time.time;
        Play("Sit");
        cat.ReportFoodInterest(0.7f);
        cat.happiness = Mathf.Clamp01(cat.happiness + amount * 0.45f);
        mealFood += amount;
        FoodReceived += amount;
        if (mealFood >= foodUntilSatisfied)
        {
            stage = Stage.Satisfied;
            cooldownUntil = Time.time + satisfiedCooldown;
            Play("TreatSatisfied");
            readyAt = Time.time + 4f;
        }
        return true;
    }
    public void ApplyRootTurn(Quaternion delta)
    {
        if (!IsBusy || Time.time < readyAt || (stage != Stage.Approaching && stage != Stage.Facing)) return;
        Vector3 forward = transform.rotation * delta * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(forward);
    }
    public void EndAttraction()
    {
        if (!IsBusy) return;
        bool seated = IsWaiting || stage == Stage.SitDown;
        Treat = null;
        if (mouthLayer >= 0 && animator) animator.SetLayerWeight(mouthLayer, 0f);
        if (!agent || !agent.enabled || !agent.isOnNavMesh) return;
        agent.isStopped = true;
        agent.ResetPath();
        roaming.ResetTurnAnimation();
        if (roaming.isActiveAndEnabled) roaming.ResumeAfterFeeding(seated);
    }
    void Play(string state)
    {
        if (playingState == state) return;
        playingState = state;
        if (animator.HasState(0, Animator.StringToHash(state)))
            animator.CrossFadeInFixedTime(state, state == "Walk" || state == "Turn" ? roaming.locomotionBlendTime : 0.2f, 0);
    }
    float Heading(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.001f ? Vector3.SignedAngle(transform.forward, direction, Vector3.up) : 0f;
    }
    void OnDisable() { EndAttraction(); }
}
