using UnityEngine;
using UnityEngine.AI;

// Navigation chooses paths. Blendspaces and their root rotation choose the actual yaw.
[RequireComponent(typeof(NavMeshAgent), typeof(Animator), typeof(PettableCat))]
public class CatIndoorRoaming : MonoBehaviour
{
    public Transform player;
    public Bounds indoorBounds = new Bounds(new Vector3(-0.35f, 0f, 0.1f), new Vector3(9.5f, 1f, 8.8f));
    public float walkingSpeed = 0.35f;
    public Vector2 walkDuration = new Vector2(7f, 15f);
    public Vector2 sitDuration = new Vector2(4f, 9f);
    public float playerSpace = 0.8f;
    [Header("Animation turning (degrees per second)")]
    public float walkTurnLimit = 112.5f;
    public float stationaryTurnLimit = 139.21875f;
    public float turnResponseTime = 0.28f;
    [Min(30f)] public float turnAcceleration = 480f;
    [Min(0.05f)] public float locomotionBlendTime = 0.16f;
    public float standUpDuration = 1.1f;
    public float sitDownDuration = 1.1f;
    public string CurrentActivity => feeding && feeding.IsBusy ? "Treat: " + feeding.Activity : toyChaser && toyChaser.IsPlaying ? toyChaser.Activity : perching && perching.IsBusy ? perching.Activity : phase.ToString();
    public bool IsStationary => feeding && feeding.IsBusy ? feeding.IsWaiting : (!toyChaser || !toyChaser.IsPlaying) && (perching && perching.IsBusy ? perching.IsResting : (phase == Phase.Sitting || phase == Phase.Lying || phase == Phase.Grooming) && agent && agent.isOnNavMesh && agent.velocity.sqrMagnitude < 0.0004f);
    enum Phase { Sitting, StandingUp, Turning, Walking, SittingDown, LyingDown, Lying, SittingUp, Grooming, StandingIdleAction }
    Phase phase;
    NavMeshAgent agent;
    Animator animator;
    PettableCat petting;
    NavMeshPath candidatePath;
    float phaseUntil, walkUntil, nextDestinationAttempt, lastPetTime = -100f;
    bool facePlayerAfterStanding, jumpAfterTurning;
    bool invitedTableVisit;
    CatPerching perching;
    CatToyChaser toyChaser;
    CatTreatFeeding feeding;
    float nextVariation;
    float smoothedTurnRate;
    static readonly int Walk = Animator.StringToHash("Walk");
    static readonly int Sit = Animator.StringToHash("Sit");
    static readonly int StandUp = Animator.StringToHash("StandUp");
    static readonly int SitDown = Animator.StringToHash("SitDown");
    static readonly int Turn = Animator.StringToHash("Turn");
    static readonly int TurnRate = Animator.StringToHash("TurnRate");

    void Awake()
    {
        candidatePath = new NavMeshPath();
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        petting = GetComponent<PettableCat>();
        perching = GetComponent<CatPerching>();
        toyChaser = GetComponent<CatToyChaser>();
        feeding = GetComponent<CatTreatFeeding>();
        // OnAnimatorMove consumes root yaw; NavMesh keeps translation on the floor.
        animator.applyRootMotion = true;
        agent.updateRotation = false;
        agent.speed = walkingSpeed;
    }
    void OnEnable()
    {
        if (!petting) petting = GetComponent<PettableCat>();
        petting.MotionControlled = true;
    }
    void Start()
    {
        if (!player)
        {
            var rig = GameObject.Find("XR Origin (XR Rig)");
            if (rig) player = rig.transform;
        }
        NavMeshHit hit;
        if (NavMesh.SamplePosition(transform.position, out hit, 0.6f,
            new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask }))
            agent.Warp(hit.position);
        if (!agent.isOnNavMesh)
        {
            Debug.LogWarning(name + " is outside the indoor cat navigation area.", this);
            enabled = false;
            return;
        }
        StopAgent(false);
        phase = Phase.Sitting;
        Play(Sit);
        phaseUntil = Time.time + Random.Range(1.5f, 5f);
    }
    public void NotifyPettingContact()
    {
        if (feeding && feeding.IsBusy) return;
        if (toyChaser && toyChaser.IsPlaying) toyChaser.EndPlay();
        if (perching && perching.IsBusy) { perching.NotifyPettingContact(); return; }
        if (!agent || !agent.isOnNavMesh) return;
        if (perching) perching.CancelPlan();
        jumpAfterTurning = false;
        lastPetTime = Time.time;
        if (phase == Phase.Lying || phase == Phase.LyingDown) BeginSitUp();
            else if (phase != Phase.Sitting && phase != Phase.SittingDown && phase != Phase.SittingUp) BeginSit();
        StopAgent(false);
        if (phase == Phase.Sitting) phaseUntil = Mathf.Max(phaseUntil, Time.time + 3f);
    }
    void OnDisable()
    {
        if (petting) petting.MotionControlled = false;
        if (agent && agent.isOnNavMesh) StopAgent(false);
    }
    void Update()
    {
        if (feeding && feeding.IsBusy) return;
        if (toyChaser && toyChaser.IsPlaying) return;
        if (perching && perching.IsBusy) return;
        if (!agent || !agent.isOnNavMesh) return;
        float now = Time.time;
        if (petting.IsBeingPetted)
        {
            lastPetTime = now;
            if (phase == Phase.Lying || phase == Phase.LyingDown) BeginSitUp();
            else if (phase != Phase.Sitting && phase != Phase.SittingDown && phase != Phase.SittingUp) BeginSit();
            // Do not extend SittingDown: its transition must be allowed to finish.
            if (phase == Phase.Sitting) phaseUntil = Mathf.Max(phaseUntil, now + 3f);
        }
        if (phase == Phase.Walking && !invitedTableVisit && player && FlatDistance(transform.position, player.position) < playerSpace)
            BeginRest();

        if (phase == Phase.LyingDown && now >= phaseUntil)
        { phase = Phase.Lying; Play(Animator.StringToHash("Lie")); phaseUntil = now + Random.Range(8f, 18f); }
        else if (phase == Phase.Lying)
        { if (now >= phaseUntil && !petting.IsBeingPetted) BeginSitUp(); }
        else if (phase == Phase.SittingUp && now >= phaseUntil)
        { phase = Phase.Sitting; Play(Sit); phaseUntil = now + Random.Range(sitDuration.x, sitDuration.y); nextVariation = now + 3f; }
        else if (phase == Phase.Grooming && now >= phaseUntil)
        { phase = Phase.Sitting; Play(Sit); phaseUntil = now + 2f; }
        else if (phase == Phase.SittingDown)
        {
            if (now >= phaseUntil)
            {
                phase = Phase.Sitting;
                Play(Sit);
                phaseUntil = now + Random.Range(sitDuration.x, sitDuration.y);
            }
        }
        else if (phase == Phase.Sitting)
        {
            // No sliding rotation while seated: stand, use turn clips, then sit again.
            if (now - lastPetTime > 3f && player && Mathf.Abs(HeadingError(player.position - transform.position)) > 30f)
                BeginStand(true);
            else if (now >= nextVariation && now - lastPetTime > 3f && Random.value < Time.deltaTime * 0.3f)
            {
                nextVariation = now + Random.Range(10f, 18f);
                if (Random.value < 0.65f) { phase = Phase.LyingDown; Play(Animator.StringToHash("SitToLie")); phaseUntil = now + 1.333334f; }
                else { phase = Phase.Grooming; Play(Animator.StringToHash(Random.value < 0.5f ? "LickPaw" : "LickChest")); phaseUntil = now + 4f; }
            }
            else if (now >= phaseUntil && now - lastPetTime > 3f)
            {
                if (PickDestination()) BeginStand(false);
                else phaseUntil = now + 2f;
            }
        }
        else if (phase == Phase.StandingIdleAction && now >= phaseUntil)
        { phase = Phase.Turning; Play(Turn); }
        else if (phase == Phase.StandingUp && now >= phaseUntil)
        {
            if (!facePlayerAfterStanding && Random.value < .15f) { phase = Phase.StandingIdleAction; Play(Animator.StringToHash("Yawn")); phaseUntil = now + 4.4f; }
            else { phase = Phase.Turning; Play(Turn); }
        }
        else if (phase == Phase.Turning)
        {
            Vector3 direction = jumpAfterTurning && perching ? perching.TakeoffDirection : facePlayerAfterStanding && player
                ? player.position - transform.position
                : agent.steeringTarget - transform.position;
            float error = HeadingError(direction);
            SetTurnRate(error, stationaryTurnLimit);
            // Resume a forward step before a small heading correction becomes an idle shuffle.
            float finishAngle = facePlayerAfterStanding || jumpAfterTurning ? 6f : 22f;
            if (Mathf.Abs(error) < finishAngle)
            {
                animator.SetFloat(TurnRate, 0f);
                if (jumpAfterTurning && perching) { jumpAfterTurning = false; if (!perching.BeginJump()) BeginSit(); }
                else if (facePlayerAfterStanding) BeginSit();
                else
                {
                    phase = Phase.Walking;
                    agent.isStopped = false;
                    agent.speed = walkingSpeed;
                    Play(Walk);
                    if (walkUntil <= now) walkUntil = now + Random.Range(walkDuration.x, walkDuration.y);
                    if (perching && perching.HasPlan) walkUntil = Mathf.Max(walkUntil, now + agent.remainingDistance / Mathf.Max(.1f, walkingSpeed) + 8f);
                    nextDestinationAttempt = now + 1f;
                }
            }
        }
        else if (phase == Phase.Walking)
        {
            Vector3 heading = agent.desiredVelocity.sqrMagnitude > 0.0025f
                ? agent.desiredVelocity : agent.steeringTarget - transform.position;
            float error = HeadingError(heading);
            if (now >= walkUntil || (!agent.pathPending && agent.pathStatus != NavMeshPathStatus.PathComplete))
                BeginRest();
            else if (Mathf.Abs(error) > 80f)
            {
                StopAgent(true);
                facePlayerAfterStanding = false;
                phase = Phase.Turning;
                Play(Turn);
            }
            else
            {
                SetTurnRate(error, walkTurnLimit);
                // Slow down for a curve; do not slide sideways around a tight corner.
                float targetSpeed = walkingSpeed * Mathf.Clamp(Mathf.Cos(error * Mathf.Deg2Rad), 0.2f, 1f);
                agent.speed = Mathf.MoveTowards(agent.speed, targetSpeed, walkingSpeed * 2f * Time.deltaTime);
                if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.08f && now >= nextDestinationAttempt)
                {
                    if (perching && perching.HasPlan) { StopAgent(true); jumpAfterTurning = true; phase = Phase.Turning; Play(Turn); }
                    else if (!PickDestination()) BeginRest();
                    nextDestinationAttempt = now + 1f;
                }
            }
        }
    }
    void OnAnimatorMove()
    {
        if (feeding && feeding.IsBusy) { feeding.ApplyRootTurn(animator.deltaRotation); return; }
        if (toyChaser && toyChaser.IsPlaying) { toyChaser.ApplyRootTurn(animator.deltaRotation); return; }
        if (perching && perching.IsBusy) { perching.ApplyRootTurn(animator.deltaRotation); return; }
        if (!enabled || !agent || !agent.isOnNavMesh) return;
        if (phase != Phase.Walking && phase != Phase.Turning) return;
        // Preserve the imported clip's instantaneous rotation, including blended arcs.
        Vector3 forward = (transform.rotation * animator.deltaRotation) * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(forward);
    }
    void SetTurnRate(float error, float limit)
    {
        UpdateTurnAnimation(error, limit);
    }
    public void UpdateTurnAnimation(float error, float limit)
    {
        float rate = Mathf.Abs(error) < 2f ? 0f : Mathf.Clamp(error / Mathf.Max(0.1f, turnResponseTime), -limit, limit);
        smoothedTurnRate = Mathf.MoveTowards(smoothedTurnRate, rate, turnAcceleration * Time.deltaTime);
        animator.SetFloat(TurnRate, smoothedTurnRate, 0.06f, Time.deltaTime);
    }
    public void ResetTurnAnimation()
    {
        smoothedTurnRate = 0f;
        animator.SetFloat(TurnRate, 0f);
    }
    float HeadingError(Vector3 direction)
    {
        direction.y = 0;
        return direction.sqrMagnitude > 0.001f ? Vector3.SignedAngle(transform.forward, direction, Vector3.up) : 0f;
    }
    void BeginStand(bool facePlayer)
    {
        facePlayerAfterStanding = facePlayer;
        StopAgent(true);
        animator.SetFloat(TurnRate, 0f);
        smoothedTurnRate = 0f;
        phase = Phase.StandingUp;
        Play(StandUp);
        phaseUntil = Time.time + standUpDuration;
        
        if (!facePlayer) walkUntil = 0f;
    }
    void BeginRest()
    {
        invitedTableVisit = false;
        if (perching) perching.CancelPlan();
        jumpAfterTurning = false;
        StopAgent(false);
        if (player && !petting.IsBeingPetted && Mathf.Abs(HeadingError(player.position - transform.position)) > 6f)
        {
            facePlayerAfterStanding = true;
            phase = Phase.Turning;
            Play(Turn);
        }
        else BeginSit();
    }
    bool PickDestination()
    {
        if (perching && perching.TryPlan(candidatePath)) return true;
        for (int i = 0; i < 24; i++)
        {
            var point = new Vector3(Random.Range(indoorBounds.min.x, indoorBounds.max.x), transform.position.y,
                                    Random.Range(indoorBounds.min.z, indoorBounds.max.z));
            NavMeshHit hit;
            if (!NavMesh.SamplePosition(point, out hit, 0.4f,
                new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask })) continue;
            if (!indoorBounds.Contains(hit.position) || Mathf.Abs(hit.position.y - transform.position.y) > 0.15f ||
                FlatDistance(hit.position, transform.position) < 1f ||
                (player && FlatDistance(hit.position, player.position) < playerSpace)) continue;
            if (agent.CalculatePath(hit.position, candidatePath) && candidatePath.status == NavMeshPathStatus.PathComplete)
                return agent.SetPath(candidatePath);
        }
        return false;
    }
    void StopAgent(bool preservePath)
    {
        if (!preservePath) agent.ResetPath();
        agent.isStopped = true;
        agent.velocity = Vector3.zero;
    }
    public bool RequestTableVisit(CatPerchSpot target)
    {
        if (!isActiveAndEnabled || (feeding && feeding.IsBusy) || (toyChaser && toyChaser.IsPlaying) || !perching || perching.IsBusy || petting.IsBeingPetted ||
            !agent || !agent.enabled || !agent.isOnNavMesh) return false;
        if (!perching.TryPlan(candidatePath, target)) return false;
        invitedTableVisit = true;
        jumpAfterTurning = false;
        BeginStand(false);
        return true;
    }
    public void ResumeAfterFeeding(bool seated)
    {
        invitedTableVisit = false;
        jumpAfterTurning = false;
        facePlayerAfterStanding = false;
        if (!seated) { BeginSit(); return; }
        StopAgent(false);
        phase = Phase.Sitting;
        Play(Sit);
        phaseUntil = Time.time + Random.Range(sitDuration.x, sitDuration.y);
    }
    public void ResumeAfterPerch() { invitedTableVisit = false; BeginSit(); }
    void BeginSitUp() { StopAgent(false); phase = Phase.SittingUp; Play(Animator.StringToHash("LieToSit")); phaseUntil = Time.time + 1.333334f; }
    void BeginSit()
    {
        StopAgent(false);
        animator.SetFloat(TurnRate, 0f);
        phase = Phase.SittingDown;
        Play(SitDown);
        phaseUntil = Time.time + sitDownDuration;
    }
    void Play(int state)
    {
        if (animator.HasState(0, state)) animator.CrossFadeInFixedTime(state,
            state == Walk || state == Turn ? locomotionBlendTime : 0.15f, 0);
    }
    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y = 0;
        return Vector3.Distance(a, b);
    }
}


