using UnityEngine;

public class TestPlayCameraController : MonoBehaviour
{
    [Header("References")]
    public TestPlayController controller;
    public Camera controlledCamera;
    public FreeCam editorCamera;

    [Header("Original-style follow")]
    public bool useLockTargetWhenAvailable = true;
    public float playerPivotHeight = 1.4f;
    public float targetPivotHeight = 1.2f;
    public float followDistance = 7f;
    public float followHeight = 2.4f;
    public float shoulderOffset;
    public float unlockedLookAhead = 2.5f;
    [Range(0f, 1f)]
    public float targetFraming = 0.4f;
    public float targetDistanceScale = 0.12f;
    public float maxTargetDistanceBonus = 5f;
    public bool followLateralMovementHeading = true;
    [Range(0f, 1f)]
    public float lateralMovementHeadingWeight = 0.5f;
    [Range(0f, 90f)]
    public float maxLockedOrbitAngle = 60f;
    public bool followBackwardMovement = true;
    [Min(0f)]
    public float backwardDistanceBonus = 2f;
    [Min(0f)]
    public float backwardHeightBonus = 0.4f;

    [Header("Response")]
    [Min(0.001f)]
    public float positionSmoothTime = 0.1f;
    [Min(0f)]
    public float rotationSharpness = 14f;
    [Min(0f)]
    public float teleportSnapDistance = 12f;
    [Tooltip("原作投影行列で確認できる60度FOVをテストプレイ中だけ適用します。")]
    public bool overrideFieldOfView = true;
    [Range(20f, 90f)]
    public float fieldOfView = 60f;

    [Header("Obstacle avoidance (Unity alternative)")]
    public bool avoidObstacles = true;
    public LayerMask obstacleMask = ~0;
    [Min(0.01f)]
    public float collisionRadius = 0.25f;
    [Min(0f)]
    public float collisionPadding = 0.15f;
    [Min(0.1f)]
    public float minimumCameraDistance = 1.2f;

    [Header("CamEffect (Unity approximation)")]
    public bool approximateCameraEffects = true;
    [Min(0.01f)]
    public float cameraEffectDuration = 0.18f;
    [Min(0f)]
    public float cameraEffectPosition = 0.08f;
    [Min(0f)]
    public float cameraEffectRotation = 0.8f;
    [Min(0f)]
    public float cameraEffectFrequency = 32f;

    public bool cameraModeActive { get; private set; }
    public bool usingLockTarget { get; private set; }

    Vector3 savedPosition;
    Quaternion savedRotation;
    float savedFieldOfView;
    bool savedEditorCameraEnabled;
    bool savedEditorCameraCanMove;
    Vector3 followPosition;
    Quaternion followRotation;
    Vector3 positionVelocity;
    Vector3 previousPlayerPivot;
    bool hasPreviousPlayerPivot;
    Vector3 movementReferenceForward;
    bool hasMovementReference;
    float shakeTimeRemaining;
    float shakeStrength;
    readonly RaycastHit[] obstacleHits = new RaycastHit[16];

    void Awake()
    {
        ResolveReferences();
    }

    void OnDisable()
    {
        if (cameraModeActive)
            ExitTestPlayCamera();
    }

    void LateUpdate()
    {
        if (!cameraModeActive || controller == null || !controller.playModeActive)
            return;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot == null)
            return;

        UpdateFollowCamera(playerRoot, Time.deltaTime);
    }

    public void Bind(TestPlayController source)
    {
        if (controller == source)
        {
            if (controller != null)
            {
                controller.PresentationEventRaised -= HandlePresentationEvent;
                controller.PresentationEventRaised += HandlePresentationEvent;
            }
            return;
        }

        UnsubscribeController();
        controller = source;
        SubscribeController();
    }

    public bool TryGetPlanarMovementBasis(out Vector3 forward, out Vector3 right)
    {
        forward = Vector3.zero;
        right = Vector3.zero;
        if (!cameraModeActive)
            return false;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot != null && TryBuildLogicalMovementForward(playerRoot, out Vector3 liveForward))
        {
            movementReferenceForward = liveForward;
            hasMovementReference = true;
        }
        if (!hasMovementReference)
            return false;

        forward = FlattenDirection(movementReferenceForward, Vector3.forward);
        right = Vector3.Cross(Vector3.up, forward).normalized;
        return right.sqrMagnitude > 0.000001f;
    }


    public void EnterTestPlayCamera(TestPlayController source)
    {
        ResolveReferences();
        Bind(source);
        if (controlledCamera == null || cameraModeActive)
            return;

        savedPosition = controlledCamera.transform.position;
        savedRotation = controlledCamera.transform.rotation;
        savedFieldOfView = controlledCamera.fieldOfView;
        savedEditorCameraEnabled = editorCamera != null && editorCamera.enabled;
        savedEditorCameraCanMove = editorCamera != null && editorCamera.canMove;

        if (editorCamera != null)
        {
            editorCamera.canMove = false;
            editorCamera.enabled = false;
        }

        cameraModeActive = true;
        usingLockTarget = false;
        positionVelocity = Vector3.zero;
        hasPreviousPlayerPivot = false;
        movementReferenceForward = Vector3.zero;
        hasMovementReference = false;
        shakeTimeRemaining = 0f;
        shakeStrength = 0f;
        if (overrideFieldOfView)
            controlledCamera.fieldOfView = fieldOfView;

        Transform playerRoot = GetPlayerRoot();
        if (playerRoot != null)
            UpdateFollowCamera(playerRoot, 0f);
    }

    public void ExitTestPlayCamera()
    {
        if (!cameraModeActive)
            return;

        cameraModeActive = false;
        usingLockTarget = false;
        shakeTimeRemaining = 0f;
        shakeStrength = 0f;
        positionVelocity = Vector3.zero;
        hasPreviousPlayerPivot = false;
        movementReferenceForward = Vector3.zero;
        hasMovementReference = false;

        if (controlledCamera != null)
        {
            controlledCamera.transform.SetPositionAndRotation(savedPosition, savedRotation);
            controlledCamera.fieldOfView = savedFieldOfView;
        }

        if (editorCamera != null)
        {
            editorCamera.mousePosition = Input.mousePosition;
            editorCamera.canMove = savedEditorCameraCanMove;
            editorCamera.enabled = savedEditorCameraEnabled;
        }
    }

    void UpdateFollowCamera(Transform playerRoot, float deltaTime)
    {
        if (controlledCamera == null)
            return;

        Vector3 playerPivot = playerRoot.position + Vector3.up * playerPivotHeight;
        Transform lockTarget = GetLockTarget();
        usingLockTarget = lockTarget != null;

        Vector3 horizontalForward = FlattenDirection(playerRoot.forward, controlledCamera.transform.forward);
        Vector3 aimPoint;
        float distance = Mathf.Max(0.1f, followDistance);
        float height = followHeight;
        if (usingLockTarget)
        {
            Vector3 targetPivot = lockTarget.position + Vector3.up * targetPivotHeight;
            Vector3 targetForward = FlattenDirection(targetPivot - playerPivot, horizontalForward);
            horizontalForward = GetLockedOrbitForward(playerRoot, targetForward);
            float targetDistance = Vector3.Distance(playerPivot, targetPivot);
            distance += Mathf.Min(Mathf.Max(0f, maxTargetDistanceBonus), targetDistance * Mathf.Max(0f, targetDistanceScale));
            aimPoint = Vector3.Lerp(playerPivot, targetPivot, Mathf.Clamp01(targetFraming));
        }
        else
        {
            aimPoint = playerPivot + horizontalForward * unlockedLookAhead;
        }

        float backwardWeight = GetBackwardFollowWeight();
        distance += Mathf.Max(0f, backwardDistanceBonus) * backwardWeight;
        height += Mathf.Max(0f, backwardHeightBonus) * backwardWeight;

        Vector3 horizontalRight = Vector3.Cross(Vector3.up, horizontalForward).normalized;
        Vector3 desiredPosition = playerPivot - horizontalForward * distance + Vector3.up * height + horizontalRight * shoulderOffset;
        desiredPosition = ResolveObstaclePosition(playerPivot, desiredPosition, playerRoot, lockTarget);

        Vector3 lookDirection = aimPoint - desiredPosition;
        if (lookDirection.sqrMagnitude < 0.000001f)
            lookDirection = horizontalForward;
        Quaternion desiredRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);

        bool snap = deltaTime <= 0f || !hasPreviousPlayerPivot;
        if (!snap && teleportSnapDistance > 0f)
            snap = Vector3.Distance(previousPlayerPivot, playerPivot) >= teleportSnapDistance;

        if (snap)
        {
            followPosition = desiredPosition;
            followRotation = desiredRotation;
            positionVelocity = Vector3.zero;
        }
        else
        {
            followPosition = Vector3.SmoothDamp(
                followPosition,
                desiredPosition,
                ref positionVelocity,
                Mathf.Max(0.001f, positionSmoothTime),
                Mathf.Infinity,
                deltaTime);
            float rotationT = rotationSharpness <= 0f ? 1f : 1f - Mathf.Exp(-rotationSharpness * deltaTime);
            followRotation = Quaternion.Slerp(followRotation, desiredRotation, rotationT);
        }

        previousPlayerPivot = playerPivot;
        hasPreviousPlayerPivot = true;
        // 入力基準は視覚補間済みのfollowRotationではなく、このフレームの論理配置方向を使う。
        // これによりカメラのSmoothDamp/Slerpが自機の旋回入力へ1フレーム遅れて帰還しない。
        movementReferenceForward = horizontalForward;
        hasMovementReference = true;
        ApplyCameraEffect(deltaTime);
    }

    bool TryBuildLogicalMovementForward(Transform playerRoot, out Vector3 forward)
    {
        forward = Vector3.zero;
        if (playerRoot == null)
            return false;

        forward = FlattenDirection(playerRoot.forward, Vector3.forward);
        Transform lockTarget = GetLockTarget();
        if (lockTarget != null)
        {
            Vector3 playerPivot = playerRoot.position + Vector3.up * playerPivotHeight;
            Vector3 targetPivot = lockTarget.position + Vector3.up * targetPivotHeight;
            Vector3 targetForward = FlattenDirection(targetPivot - playerPivot, forward);
            forward = GetLockedOrbitForward(playerRoot, targetForward);
        }

        return forward.sqrMagnitude > 0.000001f;
    }

    Vector3 GetLockedOrbitForward(Transform playerRoot, Vector3 targetForward)
    {
        if (!followLateralMovementHeading || controller == null || controller.state == null)
            return targetForward;

        int direction = controller.state.GetInt(190);
        if (!HasLateralInput(direction))
            return targetForward;

        Vector3 movementForward = FlattenDirection(playerRoot.forward, targetForward);
        float weight = Mathf.Clamp01(lateralMovementHeadingWeight);
        float signedAngle = Vector3.SignedAngle(targetForward, movementForward, Vector3.up);
        float orbitAngle = Mathf.Clamp(
            signedAngle * weight,
            -Mathf.Clamp(maxLockedOrbitAngle, 0f, 90f),
            Mathf.Clamp(maxLockedOrbitAngle, 0f, 90f));
        return FlattenDirection(Quaternion.AngleAxis(orbitAngle, Vector3.up) * targetForward, targetForward);
    }

    static bool HasLateralInput(int direction)
    {
        return direction == 1 || direction == 3 || direction == 4 ||
               direction == 6 || direction == 7 || direction == 9;
    }

    float GetBackwardFollowWeight()
    {
        if (!followBackwardMovement || controller == null || controller.state == null)
            return 0f;

        switch (controller.state.GetInt(190))
        {
            case 2:
                return 1f;
            case 1:
            case 3:
                return 0.7071068f;
            default:
                return 0f;
        }
    }

    Vector3 ResolveObstaclePosition(Vector3 pivot, Vector3 desiredPosition, Transform playerRoot, Transform lockTarget)
    {
        if (!avoidObstacles)
            return desiredPosition;

        Vector3 offset = desiredPosition - pivot;
        float distance = offset.magnitude;
        if (distance <= 0.0001f)
            return desiredPosition;

        int hitCount = Physics.SphereCastNonAlloc(
            pivot,
            Mathf.Max(0.01f, collisionRadius),
            offset / distance,
            obstacleHits,
            distance,
            obstacleMask,
            QueryTriggerInteraction.Ignore);

        float nearestDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            Transform hitTransform = obstacleHits[i].collider != null ? obstacleHits[i].collider.transform : null;
            if (hitTransform == null || IsPartOf(hitTransform, playerRoot) || IsPartOf(hitTransform, lockTarget))
                continue;
            nearestDistance = Mathf.Min(nearestDistance, obstacleHits[i].distance);
        }

        if (nearestDistance >= distance)
            return desiredPosition;

        float resolvedDistance = Mathf.Max(minimumCameraDistance, nearestDistance - collisionPadding);
        resolvedDistance = Mathf.Min(resolvedDistance, distance);
        return pivot + offset.normalized * resolvedDistance;
    }

    void ApplyCameraEffect(float deltaTime)
    {
        Vector3 position = followPosition;
        Quaternion rotation = followRotation;
        if (approximateCameraEffects && shakeTimeRemaining > 0f)
        {
            shakeTimeRemaining = Mathf.Max(0f, shakeTimeRemaining - Mathf.Max(0f, deltaTime));
            float duration = Mathf.Max(0.01f, cameraEffectDuration);
            float envelope = Mathf.Clamp01(shakeTimeRemaining / duration);
            float phase = Time.unscaledTime * Mathf.Max(0f, cameraEffectFrequency);
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(phase, 0.17f) * 2f - 1f,
                Mathf.PerlinNoise(0.43f, phase) * 2f - 1f,
                0f);
            position += rotation * (noise * cameraEffectPosition * shakeStrength * envelope);
            rotation *= Quaternion.Euler(
                noise.y * cameraEffectRotation * shakeStrength * envelope,
                noise.x * cameraEffectRotation * shakeStrength * envelope,
                0f);
        }

        controlledCamera.transform.SetPositionAndRotation(position, rotation);
    }

    void HandlePresentationEvent(TestPlayPresentationEvent presentationEvent)
    {
        if (!approximateCameraEffects || presentationEvent.type != TestPlayPresentationEventType.CameraEffect)
            return;

        float value = Mathf.Abs(presentationEvent.output);
        if (value <= 0.0001f)
        {
            shakeTimeRemaining = 0f;
            shakeStrength = 0f;
            return;
        }

        // The original executable stores CamEffect as a byte, but the per-value visual
        // meanings are still unknown. Keep this as a bounded Unity approximation.
        shakeStrength = Mathf.Clamp(value, 1f, 3f);
        shakeTimeRemaining = Mathf.Max(0.01f, cameraEffectDuration);
    }

    void ResolveReferences()
    {
        if (controlledCamera == null)
            controlledCamera = GetComponent<Camera>();
        if (controlledCamera == null)
            controlledCamera = Camera.main;
        if (editorCamera == null && controlledCamera != null)
            editorCamera = controlledCamera.GetComponent<FreeCam>();
    }

    void SubscribeController()
    {
        if (controller != null)
            controller.PresentationEventRaised += HandlePresentationEvent;
    }

    void UnsubscribeController()
    {
        if (controller != null)
            controller.PresentationEventRaised -= HandlePresentationEvent;
    }

    Transform GetPlayerRoot()
    {
        return controller != null && controller.robo != null && controller.robo.root != null
            ? controller.robo.root.transform
            : null;
    }

    Transform GetLockTarget()
    {
        if (!useLockTargetWhenAvailable || controller == null)
            return null;
        return controller.GetLockedTargetTransform();
    }

    static Vector3 FlattenDirection(Vector3 direction, Vector3 fallback)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.000001f)
        {
            fallback.y = 0f;
            direction = fallback.sqrMagnitude > 0.000001f ? fallback : Vector3.forward;
        }
        return direction.normalized;
    }

    static bool IsPartOf(Transform candidate, Transform root)
    {
        return root != null && (candidate == root || candidate.IsChildOf(root));
    }
}
