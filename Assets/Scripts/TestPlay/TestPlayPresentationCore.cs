using System;
using System.Collections.Generic;

public enum TestPlayPresentationEventType
{
    Sound,
    Voice,
    Burner,
    Proc,
    Texture,
    Visual,
    CameraEffect,
    Diagnostic
}

public enum TestPlayPresentationEvidence
{
    OriginalExecutableConfirmed,
    OriginalDataObserved,
    IncompleteInference,
    Unknown
}

public enum TestPlayPresentationAdapterKind
{
    None,
    AudioClip,
    BurnerCone,
    ParticleSystem,
    MappedPrefab,
    OriginalTextureQuad,
    PrimitiveFallback,
    CameraShakeApproximation,
    CombatOnly,
    OriginalTextureTrail
}

public struct TestPlayPresentationEvent
{
    public TestPlayPresentationEventType type;
    public TestPlayPresentationEvidence evidence;
    public TestPlayPresentationAdapterKind adapter;
    public int tick;
    public int actionIndex;
    public int scriptIndex;
    public string command;
    public string source;
    public string symbol;
    public string resourceName;
    public string diagnostic;
    public int originalId;
    public int procType;
    public int subtype;
    public int textureId;
    public float output;
    public TestPlayScriptValue[] arguments;
}

public struct TestPlaySwordBeamParameters
{
    public int weaponPointId;
    public float targetLength;
    public int primaryTextureId;
    public int lineTextureId;
    public float initialLength;
    public bool replaceManagedBeam;
    public int lifetimeTicks;
}

public struct TestPlayThunderEffectParameters
{
    public int weaponPointId;
    public float width;
    public int length;
    public float forwardSpeedPerTick;
    public int textureId;
    public float scatterRadius;
    public int activeTicks;
    public int unusedP9;
    public int unusedP10;
    public int unusedP11;
}

public struct TestPlayHinokoParameters
{
    public int weaponPointId;
    public float size;
    public float signedForwardInput;
    public float scatterX;
    public float scatterY;
    public float scatterZ;
    public int unusedP9;
    public int unusedP10;
    public int unusedP11;
}

public struct TestPlayHinokoState
{
    public int elapsedTicks;
    public int alphaByte;
    public float accumulatedDrawDegrees;
}

public struct TestPlayWindRingSpecialParameters
{
    public int weaponPointId;
    public int unusedP4;
    public int unusedP5;
    public int unusedP6;
    public int unusedP7;
    public int unusedP8;
    public int unusedP9;
    public int unusedP10;
    public int unusedP11;
}

public struct TestPlayWindRingSpecialState
{
    public int elapsedTicks;
    public float size;
    public int alphaByte;
}

public struct TestPlayBurnerBurstParameters
{
    public int weaponPointId;
    public float primarySize;
    public float primaryLength;
    public float secondarySize;
    public float secondaryLocalZ;
    public int variant;
    public int primaryTextureId;
    public int secondaryTextureId;
    public int unusedP7;
    public int unusedP8;
    public int unusedP9;
    public int unusedP10;
    public int unusedP11;
}

public struct TestPlayBurnerBurstState
{
    public int primaryUpdateCount;
    public int secondaryRemainingCounter;
    public float primaryExpansionPerSide;
    public float secondaryBasisScale;
    public bool primaryAlive;
    public bool secondaryAlive;
}

public struct TestPlayMagicShieldParameters
{
    public int weaponPointId;
    public int modelSlotIndex;
    public int releaseGateValue;
    public int activeTicks;
    public bool followWeaponPoint;
    public int unusedP7;
    public int unusedP8;
    public int unusedP9;
    public int unusedP10;
    public int unusedP11;
}

public enum TestPlayMagicShieldPhase
{
    Grow,
    Active,
    Fade,
    Expired
}

public struct TestPlayMagicShieldState
{
    public TestPlayMagicShieldPhase phase;
    public float scale;
    public float opacity;
    public int remainingActiveTicks;
}

public struct TestPlayOriginalBurnerDrawParameters
{
    public TestPlayOriginalBurnerOwnerKind ownerKind;
    public bool requested;
    public float aniOutput;
    public float drawValue;
    public float primarySizeArgument;
    public float primaryLengthArgument;
    public float secondarySizeArgument;
    public float secondaryLocalZArgument;
}

public enum TestPlayOriginalBurnerOwnerKind
{
    NormalRobot,
    Ship
}

public struct TestPlayOriginalBurnerPrimaryUpdateResult
{
    public int updateCount;
    public float matrixAdjustmentArgument;
    public bool expiredAfterUpdate;
}

public struct TestPlayOriginalBurnerBallUpdateResult
{
    public int remainingCounter;
    public float matrixBasisMultiplier;
    public bool expiredAfterUpdate;
}

public static class TestPlayPresentationCore
{
    public const int MinimumBurnerId = 0;
    public const int MaximumBurnerId = 19;
    public const int WindLineProcType = 53;
    public const int WindRingProcType = 54;
    public const int OriginalWindLineCount = 7;
    public const float OriginalWindLineWidth = 0.07f;
    public const float OriginalWindLineLength = 3f;
    public const float OriginalWindRingRadius = 1f;
    public const int SwordBeamProcType = 55;
    public const float OriginalSwordBeamWidth = 0.075f;
    public const float OriginalSwordBeamGrowthPerTick = 0.2f;
    public const int OriginalSwordBeamInfiniteLifetime = 999999999;
    public const int ThunderEffectProcType = 60;
    public const int OriginalThunderFadeTicks = 10;
    public const int SpecialEffectProcType = 62;
    public const int WindRingSpecialSubtype = 2;
    public const int BurnerBurstSubtype = 3;
    public const int OriginalBurnerBurstVariant0PrimaryTextureId = 8;
    public const int OriginalBurnerBurstVariant0SecondaryTextureId = 9;
    public const int OriginalBurnerBurstVariant1PrimaryTextureId = 43;
    public const int OriginalBurnerBurstVariant1SecondaryTextureId = 44;
    public const string OriginalWindRingTextureFileName = "WindRing.png";
    public const int OriginalWindRingTextureId = 20;
    public const float OriginalWindRingInitialSize = 1f;
    public const float OriginalWindRingGrowthPerTick = 0.3f;
    public const int OriginalWindRingInitialAlphaByte = 255;
    public const int OriginalWindRingAlphaDeltaPerTick = -24;
    public const int HinokoSubtype = 6;
    public const int OriginalHinokoTextureId = 40;
    public const int OriginalHinokoInitialAlphaByte = 255;
    public const int OriginalHinokoFadeDelayTicks = 30;
    public const int OriginalHinokoAlphaFadePerTick = 4;
    public const float OriginalHinokoDrawDegreesPerTick = 5f;
    public const int MagicShieldSubtype = 8;
    public const float OriginalMagicShieldInitialScale = 0.1f;
    public const float OriginalMagicShieldGrowPerTick = 0.1f;
    public const float OriginalMagicShieldActiveScaleThreshold = 0.9f;
    public const float OriginalMagicShieldOpacityPerTick = 0.1f;
    public const float OriginalMagicShieldFadeScalePerTick = 0.05f;
    public const int OriginalBurnerPrimaryExpiryUpdate = 11;
    public const int OriginalBurnerBallInitialCounter = 4;
    public const float OriginalBurnerBallMatrixBasisMultiplier = 0.95f;

    public static TestPlayPresentationEvent CreateSound(
        IReadOnlyList<TestPlayScriptValue> arguments,
        TestPlayPresentationAdapterKind adapter)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Sound,
            "Snd",
            arguments,
            TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            adapter);
        value.originalId = GetInt(arguments, 0, -1);
        value.symbol = GetText(arguments, 0);
        return value;
    }

    public static TestPlayPresentationEvent CreateVoice(
        IReadOnlyList<TestPlayScriptValue> arguments,
        TestPlayPresentationAdapterKind adapter)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Voice,
            "Voice",
            arguments,
            TestPlayPresentationEvidence.OriginalDataObserved,
            adapter);
        value.symbol = GetText(arguments, 0);
        return value;
    }

    public static bool TryCreateBurner(
        IReadOnlyList<TestPlayScriptValue> arguments,
        TestPlayPresentationAdapterKind adapter,
        out TestPlayPresentationEvent value)
    {
        value = CreateBase(
            TestPlayPresentationEventType.Burner,
            "BURNER",
            arguments,
            TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            adapter);
        value.originalId = GetInt(arguments, 0, -1);
        value.output = arguments != null && arguments.Count > 1
            ? arguments[1].AsFloat(1f)
            : 1f;

        if (arguments == null || arguments.Count == 0)
        {
            value.type = TestPlayPresentationEventType.Diagnostic;
            value.diagnostic = "MissingBurnerId";
            return false;
        }

        if (value.originalId < MinimumBurnerId || value.originalId > MaximumBurnerId)
        {
            value.type = TestPlayPresentationEventType.Diagnostic;
            value.diagnostic = "BurnerIdOutsideOriginalRange";
            return false;
        }

        if (arguments.Count == 1)
            value.diagnostic = "UnityCompatibilityDefaultOutput";
        return true;
    }

    public static TestPlayOriginalBurnerDrawParameters CreateOriginalNormalBurnerDrawParameters(
        bool requested,
        float configuredSptValue,
        float aniOutput)
    {
        // Scr_BunerOut stores ANI output separately, but CRobot_Normal's draw call
        // gates on the request byte and passes the BURNERSET third value unchanged.
        return CreateOriginalBurnerDrawParameters(
            TestPlayOriginalBurnerOwnerKind.NormalRobot,
            requested,
            configuredSptValue,
            aniOutput);
    }

    public static TestPlayOriginalBurnerDrawParameters CreateOriginalShipBurnerDrawParameters(
        float configuredSptValue,
        float speed)
    {
        // FUN_0049c790 constructs CShip. Its observed draw site has no ANI request gate.
        float drawValue = configuredSptValue * Math.Max(speed * 10f, 0f);
        return CreateOriginalBurnerDrawParameters(
            TestPlayOriginalBurnerOwnerKind.Ship,
            true,
            drawValue,
            0f);
    }

    // Compatibility alias retained for callers added before U-009c identified CShip.
    public static TestPlayOriginalBurnerDrawParameters CreateOriginalFlightBurnerDrawParameters(
        float configuredSptValue,
        float speed)
    {
        return CreateOriginalShipBurnerDrawParameters(configuredSptValue, speed);
    }

    public static TestPlayOriginalBurnerPrimaryUpdateResult AdvanceOriginalBurnerPrimary(
        bool ownerMarkedForDeletion,
        float primarySizeArgument,
        int currentUpdateCount)
    {
        if (ownerMarkedForDeletion)
        {
            return new TestPlayOriginalBurnerPrimaryUpdateResult
            {
                updateCount = currentUpdateCount,
                matrixAdjustmentArgument = 0f,
                expiredAfterUpdate = true
            };
        }

        int nextUpdateCount = currentUpdateCount + 1;
        return new TestPlayOriginalBurnerPrimaryUpdateResult
        {
            updateCount = nextUpdateCount,
            // FUN_00491580 passes this value to both float arguments of FUN_004905d0.
            matrixAdjustmentArgument = -primarySizeArgument / 15f,
            expiredAfterUpdate = nextUpdateCount >= OriginalBurnerPrimaryExpiryUpdate
        };
    }

    public static TestPlayOriginalBurnerBallUpdateResult AdvanceOriginalBurnerBall(
        bool ownerMarkedForDeletion,
        int currentRemainingCounter)
    {
        if (ownerMarkedForDeletion)
        {
            return new TestPlayOriginalBurnerBallUpdateResult
            {
                remainingCounter = currentRemainingCounter,
                matrixBasisMultiplier = 1f,
                expiredAfterUpdate = true
            };
        }

        int nextRemainingCounter = currentRemainingCounter - 1;
        return new TestPlayOriginalBurnerBallUpdateResult
        {
            remainingCounter = nextRemainingCounter,
            matrixBasisMultiplier = OriginalBurnerBallMatrixBasisMultiplier,
            expiredAfterUpdate = nextRemainingCounter < 0
        };
    }

    static TestPlayOriginalBurnerDrawParameters CreateOriginalBurnerDrawParameters(
        TestPlayOriginalBurnerOwnerKind ownerKind,
        bool requested,
        float drawValue,
        float aniOutput)
    {
        return new TestPlayOriginalBurnerDrawParameters
        {
            ownerKind = ownerKind,
            requested = requested,
            aniOutput = aniOutput,
            drawValue = drawValue,
            primarySizeArgument = drawValue / 2f,
            primaryLengthArgument = drawValue,
            secondarySizeArgument = drawValue / 3f,
            secondaryLocalZArgument = drawValue / 8f
        };
    }

    public static TestPlayPresentationEvent CreateUnsupported(
        string command,
        IReadOnlyList<TestPlayScriptValue> arguments,
        string diagnostic)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Diagnostic,
            command,
            arguments,
            TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            TestPlayPresentationAdapterKind.None);
        value.diagnostic = diagnostic ?? "";
        return value;
    }

    public static TestPlayPresentationEvent CreateCameraEffect(
        IReadOnlyList<TestPlayScriptValue> arguments,
        float effectId,
        TestPlayPresentationAdapterKind adapter)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.CameraEffect,
            "CamEffect",
            arguments,
            TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            adapter);
        value.originalId = UnityEngine.Mathf.RoundToInt(effectId);
        value.output = effectId;
        if (adapter == TestPlayPresentationAdapterKind.CameraShakeApproximation)
            value.diagnostic = "OriginalPerValueCameraFormulaUnknown";
        return value;
    }

    public static TestPlayPresentationEvent CreateProc(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        TestPlayPresentationAdapterKind adapter)
    {
        int procType = GetInt(arguments, 1, -1);
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Proc,
            extended ? "RunProc2" : "RunProc",
            arguments,
            IsOriginalWindProc(extended, procType) ||
            IsOriginalSwordBeamProc(extended, procType) ||
            IsOriginalThunderEffectProc(extended, procType) ||
            IsOriginalSpecialEffectProc(extended, procType, GetInt(arguments, 3, -1))
                ? TestPlayPresentationEvidence.OriginalExecutableConfirmed
                : procType == 57
                    ? TestPlayPresentationEvidence.OriginalDataObserved
                    : TestPlayPresentationEvidence.IncompleteInference,
            adapter);
        value.originalId = GetInt(arguments, 0, -1);
        value.procType = procType;
        value.subtype = procType == 62 ? GetInt(arguments, 3, -1) : -1;
        return value;
    }

    public static bool IsOriginalSpecialEffectProc(bool extended, int procType, int subtype)
    {
        // FUN_004b74a0 type 62 dispatches to FUN_004fa830. The handler has
        // explicit branches for subtype 0..11; this confirms the dispatch
        // boundary without assigning meanings to every forwarded argument.
        return extended && procType == SpecialEffectProcType && subtype >= 0 && subtype <= 11;
    }

    public static bool IsOriginalMagicShieldProc(bool extended, int procType, int subtype)
    {
        return IsOriginalSpecialEffectProc(extended, procType, subtype) &&
               subtype == MagicShieldSubtype;
    }

    public static bool IsOriginalWindRingSpecialProc(bool extended, int procType, int subtype)
    {
        return IsOriginalSpecialEffectProc(extended, procType, subtype) &&
               subtype == WindRingSpecialSubtype;
    }

    public static bool IsOriginalBurnerBurstProc(bool extended, int procType, int subtype)
    {
        return IsOriginalSpecialEffectProc(extended, procType, subtype) &&
               subtype == BurnerBurstSubtype;
    }

    public static bool TryCreateOriginalBurnerBurstParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlayBurnerBurstParameters value)
    {
        value = default(TestPlayBurnerBurstParameters);
        if (!IsOriginalBurnerBurstProc(
                extended,
                GetInt(arguments, 1, -1),
                GetInt(arguments, 3, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        int variant = GetInt(arguments, 6, 0);
        if (variant != 0 && variant != 1)
            return false;

        float primarySize = GetInt(arguments, 4, 0) / 100f;
        float primaryLength = GetInt(arguments, 5, 0) / 100f;
        value = new TestPlayBurnerBurstParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            primarySize = primarySize,
            primaryLength = primaryLength,
            secondarySize = primarySize / 2f,
            secondaryLocalZ = primaryLength / 8f,
            variant = variant,
            primaryTextureId = variant == 0
                ? OriginalBurnerBurstVariant0PrimaryTextureId
                : OriginalBurnerBurstVariant1PrimaryTextureId,
            secondaryTextureId = variant == 0
                ? OriginalBurnerBurstVariant0SecondaryTextureId
                : OriginalBurnerBurstVariant1SecondaryTextureId,
            unusedP7 = GetInt(arguments, 7, 0),
            unusedP8 = GetInt(arguments, 8, 0),
            unusedP9 = GetInt(arguments, 9, 0),
            unusedP10 = GetInt(arguments, 10, 0),
            unusedP11 = GetInt(arguments, 11, 0)
        };
        return true;
    }

    public static TestPlayBurnerBurstState CreateOriginalBurnerBurstState()
    {
        return new TestPlayBurnerBurstState
        {
            primaryUpdateCount = 0,
            secondaryRemainingCounter = OriginalBurnerBallInitialCounter,
            primaryExpansionPerSide = 0f,
            secondaryBasisScale = 1f,
            primaryAlive = true,
            secondaryAlive = true
        };
    }

    public static bool AdvanceOriginalBurnerBurst(
        bool ownerMarkedForDeletion,
        TestPlayBurnerBurstParameters parameters,
        ref TestPlayBurnerBurstState state)
    {
        if (state.primaryAlive)
        {
            TestPlayOriginalBurnerPrimaryUpdateResult primary = AdvanceOriginalBurnerPrimary(
                ownerMarkedForDeletion,
                parameters.primarySize,
                state.primaryUpdateCount);
            state.primaryUpdateCount = primary.updateCount;
            state.primaryExpansionPerSide -= primary.matrixAdjustmentArgument;
            state.primaryAlive = !primary.expiredAfterUpdate;
        }

        if (state.secondaryAlive)
        {
            TestPlayOriginalBurnerBallUpdateResult secondary = AdvanceOriginalBurnerBall(
                ownerMarkedForDeletion,
                state.secondaryRemainingCounter);
            state.secondaryRemainingCounter = secondary.remainingCounter;
            state.secondaryBasisScale *= secondary.matrixBasisMultiplier;
            state.secondaryAlive = !secondary.expiredAfterUpdate;
        }

        return state.primaryAlive || state.secondaryAlive;
    }

    public static bool TryCreateOriginalWindRingSpecialParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlayWindRingSpecialParameters value)
    {
        value = default(TestPlayWindRingSpecialParameters);
        if (!IsOriginalWindRingSpecialProc(
                extended,
                GetInt(arguments, 1, -1),
                GetInt(arguments, 3, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        // FUN_004fa830 reads p2 only. It passes fixed 1.0 size, draw mode 1,
        // +0.3 growth and -24 alpha delta to BB_WindRing; p4-p11 are unread.
        value = new TestPlayWindRingSpecialParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            unusedP4 = GetInt(arguments, 4, 0),
            unusedP5 = GetInt(arguments, 5, 0),
            unusedP6 = GetInt(arguments, 6, 0),
            unusedP7 = GetInt(arguments, 7, 0),
            unusedP8 = GetInt(arguments, 8, 0),
            unusedP9 = GetInt(arguments, 9, 0),
            unusedP10 = GetInt(arguments, 10, 0),
            unusedP11 = GetInt(arguments, 11, 0)
        };
        return true;
    }

    public static TestPlayWindRingSpecialState CreateOriginalWindRingSpecialState()
    {
        // FUN_0048bef0 initializes a 1.0 quad with vertex alpha 255.
        return new TestPlayWindRingSpecialState
        {
            elapsedTicks = 0,
            size = OriginalWindRingInitialSize,
            alphaByte = OriginalWindRingInitialAlphaByte
        };
    }

    public static bool AdvanceOriginalWindRingSpecial(ref TestPlayWindRingSpecialState state)
    {
        // FUN_00491af0 grows the quad first, then applies signed alpha delta.
        // The candidate alpha is not written when negative; update 11 removes it.
        state.elapsedTicks++;
        state.size += OriginalWindRingGrowthPerTick;
        int nextAlpha = state.alphaByte + OriginalWindRingAlphaDeltaPerTick;
        if (nextAlpha < 0)
            return false;
        state.alphaByte = nextAlpha;
        return true;
    }

    public static bool IsOriginalHinokoProc(bool extended, int procType, int subtype)
    {
        return IsOriginalSpecialEffectProc(extended, procType, subtype) &&
               subtype == HinokoSubtype;
    }

    public static bool TryCreateOriginalHinokoParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlayHinokoParameters value)
    {
        value = default(TestPlayHinokoParameters);
        if (!IsOriginalHinokoProc(
                extended,
                GetInt(arguments, 1, -1),
                GetInt(arguments, 3, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        // FUN_004fa830 resolves p2 as WEAPONPOINT, passes p4/100 as the
        // BB_Hinoko size, uses p5/10000 along the WEAPONPOINT Z basis, and
        // applies independent +/-p6..p8/100 spawn scatter. p9-p11 are unread.
        value = new TestPlayHinokoParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            size = GetInt(arguments, 4, 0) / 100f,
            signedForwardInput = GetInt(arguments, 5, 0) / 10000f,
            scatterX = GetInt(arguments, 6, 0) / 100f,
            scatterY = GetInt(arguments, 7, 0) / 100f,
            scatterZ = GetInt(arguments, 8, 0) / 100f,
            unusedP9 = GetInt(arguments, 9, 0),
            unusedP10 = GetInt(arguments, 10, 0),
            unusedP11 = GetInt(arguments, 11, 0)
        };
        return true;
    }

    public static TestPlayHinokoState CreateOriginalHinokoState()
    {
        // FUN_005036e0 initializes elapsed/angle to zero and alpha to 255.
        return new TestPlayHinokoState
        {
            elapsedTicks = 0,
            alphaByte = OriginalHinokoInitialAlphaByte,
            accumulatedDrawDegrees = 0f
        };
    }

    public static bool AdvanceOriginalHinoko(ref TestPlayHinokoState state)
    {
        // FUN_0048e0b0 advances the draw angle and elapsed counter first.
        // Fade begins when the incremented counter is greater than 30; alpha
        // falls by four and the object is removed on update 94.
        state.accumulatedDrawDegrees += OriginalHinokoDrawDegreesPerTick;
        state.elapsedTicks++;
        if (state.elapsedTicks > OriginalHinokoFadeDelayTicks)
            state.alphaByte = Math.Max(0, state.alphaByte - OriginalHinokoAlphaFadePerTick);
        return state.alphaByte > 0;
    }

    public static bool TryCreateOriginalMagicShieldParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlayMagicShieldParameters value)
    {
        value = default(TestPlayMagicShieldParameters);
        if (!IsOriginalMagicShieldProc(
                extended,
                GetInt(arguments, 1, -1),
                GetInt(arguments, 3, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        // FUN_004fa830 resolves p2 as WEAPONPOINT, p4 as the 0xAC-byte model
        // slot, stores the low 16 bits of p5 as the active-stage release gate,
        // and uses p6 both to enable WEAPONPOINT-matrix following and as the
        // countdown. p7-p11 are not read by the subtype-8 branch.
        value = new TestPlayMagicShieldParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            modelSlotIndex = GetInt(arguments, 4, -1),
            releaseGateValue = unchecked((short)GetInt(arguments, 5, 0)),
            activeTicks = GetInt(arguments, 6, 0),
            followWeaponPoint = GetInt(arguments, 6, 0) != 0,
            unusedP7 = GetInt(arguments, 7, 0),
            unusedP8 = GetInt(arguments, 8, 0),
            unusedP9 = GetInt(arguments, 9, 0),
            unusedP10 = GetInt(arguments, 10, 0),
            unusedP11 = GetInt(arguments, 11, 0)
        };
        return true;
    }

    public static TestPlayMagicShieldState CreateOriginalMagicShieldState(
        TestPlayMagicShieldParameters parameters)
    {
        // FUN_00479120 initializes +0x12C to 0.1 and +0x144 to zero.
        return new TestPlayMagicShieldState
        {
            phase = TestPlayMagicShieldPhase.Grow,
            scale = OriginalMagicShieldInitialScale,
            opacity = 0f,
            remainingActiveTicks = parameters.activeTicks
        };
    }

    public static bool AdvanceOriginalMagicShield(
        TestPlayMagicShieldParameters parameters,
        ref TestPlayMagicShieldState state)
    {
        switch (state.phase)
        {
            case TestPlayMagicShieldPhase.Grow:
                // FUN_00479370 grows scale and opacity together, then switches
                // callback as soon as scale reaches 0.9.
                state.scale = Math.Min(1f, state.scale + OriginalMagicShieldGrowPerTick);
                state.opacity = Math.Min(1f, state.opacity + OriginalMagicShieldOpacityPerTick);
                if (state.scale >= OriginalMagicShieldActiveScaleThreshold)
                    state.phase = TestPlayMagicShieldPhase.Active;
                return true;

            case TestPlayMagicShieldPhase.Active:
                // FUN_00479560 decrements before testing the p6 boundary. A
                // non-positive low-16 p5 gate enters fade on this first update.
                state.opacity = Math.Min(1f, state.opacity + OriginalMagicShieldOpacityPerTick);
                state.remainingActiveTicks--;
                if (parameters.releaseGateValue < 1 || state.remainingActiveTicks < 0)
                    state.phase = TestPlayMagicShieldPhase.Fade;
                return true;

            case TestPlayMagicShieldPhase.Fade:
                // FUN_00479740 removes 0.1 opacity first. It expands by 0.05
                // only while the remaining opacity is at least 0.0001.
                state.opacity = Math.Max(0f, state.opacity - OriginalMagicShieldOpacityPerTick);
                if (state.opacity < 0.0001f)
                {
                    state.phase = TestPlayMagicShieldPhase.Expired;
                    return false;
                }
                state.scale += OriginalMagicShieldFadeScalePerTick;
                return true;

            default:
                return false;
        }
    }

    public static bool IsOriginalWindProc(bool extended, int procType)
    {
        return !extended && (procType == WindLineProcType || procType == WindRingProcType);
    }

    public static int GetOriginalWindVisualCount(bool extended, int procType)
    {
        if (!IsOriginalWindProc(extended, procType))
            return 0;
        return procType == WindLineProcType ? OriginalWindLineCount : 1;
    }

    public static bool IsOriginalSwordBeamProc(bool extended, int procType)
    {
        return extended && procType == SwordBeamProcType;
    }

    public static bool TryCreateOriginalSwordBeamParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlaySwordBeamParameters value)
    {
        value = default(TestPlaySwordBeamParameters);
        if (!IsOriginalSwordBeamProc(extended, GetInt(arguments, 1, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        int scriptedLifetime = GetInt(arguments, 11, 0);
        value = new TestPlaySwordBeamParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            targetLength = arguments[3].AsFloat() / 100f,
            primaryTextureId = GetInt(arguments, 4, -1),
            lineTextureId = GetInt(arguments, 5, -1),
            initialLength = arguments[6].AsFloat() / 100f,
            replaceManagedBeam = GetInt(arguments, 10, 0) != 0,
            lifetimeTicks = scriptedLifetime == 0
                ? OriginalSwordBeamInfiniteLifetime
                : scriptedLifetime
        };
        return true;
    }

    public static bool AdvanceOriginalSwordBeam(
        ref float currentLength,
        float targetLength,
        ref int remainingTicks)
    {
        // BB_SwordBeam::Update (FUN_00497cd0) keeps the sentinel unchanged,
        // otherwise decrements before checking the expiry boundary.
        if (remainingTicks < OriginalSwordBeamInfiniteLifetime)
            remainingTicks--;
        if (remainingTicks < 1)
            return false;

        if (currentLength < targetLength)
        {
            currentLength += OriginalSwordBeamGrowthPerTick;
            if (currentLength > targetLength)
                currentLength = targetLength;
        }
        return true;
    }

    public static bool IsOriginalThunderEffectProc(bool extended, int procType)
    {
        return extended && procType == ThunderEffectProcType;
    }

    public static bool TryCreateOriginalThunderEffectParameters(
        bool extended,
        IReadOnlyList<TestPlayScriptValue> arguments,
        out TestPlayThunderEffectParameters value)
    {
        value = default(TestPlayThunderEffectParameters);
        if (!IsOriginalThunderEffectProc(extended, GetInt(arguments, 1, -1)) ||
            arguments == null || arguments.Count < 12)
            return false;

        // FUN_004fa690 passes p2 as the WEAPONPOINT id, scales p3/p5/p7 by
        // 1/100, resolves p6 through the loaded texture table, and forwards p8
        // as the active tick count. p9-p11 are not read by the original handler.
        value = new TestPlayThunderEffectParameters
        {
            weaponPointId = GetInt(arguments, 2, -1),
            width = arguments[3].AsFloat() / 100f,
            length = GetInt(arguments, 4, 0),
            forwardSpeedPerTick = arguments[5].AsFloat() / 100f,
            textureId = GetInt(arguments, 6, -1),
            scatterRadius = arguments[7].AsFloat() / 100f,
            activeTicks = GetInt(arguments, 8, 0),
            unusedP9 = GetInt(arguments, 9, 0),
            unusedP10 = GetInt(arguments, 10, 0),
            unusedP11 = GetInt(arguments, 11, 0)
        };
        return true;
    }

    public static bool AdvanceOriginalThunderEffect(
        float initialWidth,
        float forwardSpeedPerTick,
        ref float currentWidth,
        ref int remainingActiveTicks,
        ref float travelDistance)
    {
        // LZ_ThunderEffect::Update candidate FUN_0047c380 matches every field
        // written by FUN_0048c470: movement is applied first, p8 is decremented,
        // then p3/10 is removed per tick until the scalar drops below 0.001.
        travelDistance += forwardSpeedPerTick;
        remainingActiveTicks--;
        if (remainingActiveTicks < 1)
        {
            remainingActiveTicks = 0;
            currentWidth -= initialWidth / OriginalThunderFadeTicks;
            if (currentWidth < 0.001f)
                return false;
        }
        return true;
    }

    public static TestPlayPresentationEvent CreateTexture(
        string source,
        int textureId,
        string resourceName,
        TestPlayPresentationAdapterKind adapter)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Texture,
            "Texture",
            null,
            TestPlayPresentationEvidence.OriginalDataObserved,
            adapter);
        value.source = source ?? "";
        value.textureId = textureId;
        value.resourceName = resourceName ?? "";
        return value;
    }

    public static TestPlayPresentationEvent CreateVisual(
        string source,
        int textureId,
        TestPlayPresentationAdapterKind adapter)
    {
        return CreateVisual(
            source,
            textureId,
            adapter,
            textureId >= 0
                ? TestPlayPresentationEvidence.OriginalDataObserved
                : TestPlayPresentationEvidence.IncompleteInference,
            "");
    }

    public static TestPlayPresentationEvent CreateVisual(
        string source,
        int textureId,
        TestPlayPresentationAdapterKind adapter,
        TestPlayPresentationEvidence evidence,
        string diagnostic)
    {
        TestPlayPresentationEvent value = CreateBase(
            TestPlayPresentationEventType.Visual,
            "Visual",
            null,
            evidence,
            adapter);
        value.source = source ?? "";
        value.textureId = textureId;
        value.diagnostic = diagnostic ?? "";
        return value;
    }

    static TestPlayPresentationEvent CreateBase(
        TestPlayPresentationEventType type,
        string command,
        IReadOnlyList<TestPlayScriptValue> arguments,
        TestPlayPresentationEvidence evidence,
        TestPlayPresentationAdapterKind adapter)
    {
        return new TestPlayPresentationEvent
        {
            type = type,
            evidence = evidence,
            adapter = adapter,
            command = command ?? "",
            source = "",
            symbol = "",
            resourceName = "",
            diagnostic = "",
            originalId = -1,
            procType = -1,
            subtype = -1,
            textureId = -1,
            arguments = CopyArguments(arguments)
        };
    }

    static TestPlayScriptValue[] CopyArguments(IReadOnlyList<TestPlayScriptValue> arguments)
    {
        int count = arguments != null ? arguments.Count : 0;
        TestPlayScriptValue[] result = new TestPlayScriptValue[count];
        for (int i = 0; i < count; i++)
            result[i] = arguments[i];
        return result;
    }

    static int GetInt(IReadOnlyList<TestPlayScriptValue> arguments, int index, int fallback)
    {
        return arguments != null && index >= 0 && index < arguments.Count
            ? arguments[index].AsInt(fallback)
            : fallback;
    }

    static string GetText(IReadOnlyList<TestPlayScriptValue> arguments, int index)
    {
        return arguments != null && index >= 0 && index < arguments.Count
            ? arguments[index].ToString()
            : "";
    }
}
