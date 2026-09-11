using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public static class TestPlayGoldenTraceVerification
{
    const string MechId = "ガンダムTR-1ヘイズル改";
    const string SelectedMechFolderEditorPref = "WindomXP.TestPlay.SelectedOriginalMechFolder";

    sealed class RunCapture
    {
        public TestPlayGoldenTraceSession session;
        public readonly HashSet<int> observedActions = new HashSet<int>();
        public int riseActionEntries;
        public int riseActionTicks;
        public int riseFrameCount;
        public string riseScriptTiming = "";
        public int maximumRiseFrameIndex = -1;
        public int consecutiveRiseFinalFrameTicks;
        public int maximumRiseFinalFrameTicks;
        public bool sawRiseFinalFrameWhileHeld;
        public bool sawAirIdleAfterRiseRelease;
        public int lastAnimationIndex = int.MinValue;
        public int lastLogicalAction = int.MinValue;
        public bool sawRiseAction;
        public bool sawFirstStepTapWithoutStep;
        public int stepActionEntries;
        public int stepActionTicks;
        public int stepEntryTick = -1;
        public int stepEntryAction = -1;
        public int stepExitTick = -1;
        public int stepExitAction = -1;
        public bool stepEntryOnSecondTap;
        public bool stepExitedAfterRelease;
        public bool stepHorizontalMovementObserved;
        public bool stepEnergySemanticsValid = true;
        public float stepEnergyConsumed;
        public int stepEnergyDrainTicks;
        public bool sawFirstBoostTapWithoutBoost;
        public int boostActionEntries;
        public int boostActionTicks;
        public int boostEntryTick = -1;
        public int boostExitTick = -1;
        public int boostExitAction = -1;
        public bool boostEntryOnSecondTap;
        public bool boostExitedAfterRelease;
        public bool boostInitialFiveTicksStationary = true;
        public bool boostMovedAfterInitialWindow;
        public bool boostEnergySemanticsValid = true;
        public float boostEntryEnergyCost;
        public float boostPerTickEnergyConsumed;
        public int boostEnergyDrainTicks;
        public int boostWindLineProcEvents;
        public int boostWindRingProcEvents;
        public int boostWindProjectileEvents;
        public int airMoveActionEntries;
        public int airMoveActionTicks;
        public int airIdleEntriesAfterAirMove;
        public int airIdleTicksAfterAirMove;
        public bool airMoveSemanticsValid = true;
        public bool airIdleSemanticsValid = true;
        public bool airMoveExitedOnRelease;
        public bool sawAirIdleMoveDecay;
        public bool sawAirIdleVerticalBrake;
        public bool sawAirIdleBrakeBoundary;
        public bool sawAirIdleDescent;
        public bool hasGt004PreviousStep;
        public TestPlayMotionStep previousGt004Step;
        public int shotInputTicks;
        public int shotActionEntries;
        public int shotActionTicks;
        public int shotEntryTick = -1;
        public int shotScriptBundleTick = -1;
        public int shotRecoveryEntryTick = -1;
        public int shotRecoveryTicks;
        public int shotIdleReturnTick = -1;
        public int shotCooldownZeroTick = -1;
        public int shotCooldownSetEvents;
        public int shotProfileEvents;
        public int shotAttackFlagEvents;
        public int shotProjectileEvents;
        public int shotProcTypeOneEvents;
        public bool shotEntryOnPress;
        public bool shotExitedToRecovery;
        public bool shotReturnedToIdle;
        public bool shotScriptBundleSemanticsValid = true;
        public bool shotType1CoreSemanticsValid = true;
        public bool shotType1MuzzleSemanticsValid = true;
        public bool shotType1EnergySemanticsValid = true;
        public bool shotCooldownDecayValid = true;
        public bool shotCooldownTracking;
        public int previousShotCooldown;
        public int switchMeleeInputTicks;
        public int switchToSwordEntries;
        public int switchToSwordTicks;
        public int switchToSwordEntryTick = -1;
        public int switchToSwordCompleteTick = -1;
        public bool switchToSwordEntryOnPress;
        public bool switchToSwordSemanticsValid = true;
        public readonly List<int> switchProcType55Ticks = new List<int>();
        public int forwardMeleeEntries;
        public int forwardMeleeTicks;
        public int forwardMeleeEntryTick = -1;
        public bool forwardMeleeEntryOnPress;
        public bool forwardMeleeSemanticsValid = true;
        public int forwardMeleeMovementTicks;
        public float forwardMeleeApproachDistance;
        public float forwardMeleeFollowupDistance;
        public float forwardMeleeMinimumTargetDistance = float.PositiveInfinity;
        public bool forwardMeleeEnteredTargetRadius;
        public bool forwardMeleePassedTargetPlane;
        public bool forwardMeleeHeadingCaptured;
        public Vector3 forwardMeleeHeading;
        public int forwardMeleeEnergyDrainTicks;
        public float forwardMeleeEnergyConsumed;
        public int forwardMeleeFollowupEvents;
        public int forwardMeleeFollowupTick = -1;
        public bool forwardMeleeFollowupValid;
        public int forwardMeleeFollowupEntries;
        public int forwardMeleeFollowupTicks;
        public readonly List<int> forwardMeleeProcType57Ticks = new List<int>();
        public int forwardMeleeRecoveryTick = -1;
        public int forwardMeleeRecoveryTicks;
        public int forwardMeleeIdleTick = -1;
        public int comboMeleeInputTicks;
        public int combo131Entries;
        public int combo131Ticks;
        public int combo132Entries;
        public int combo132Ticks;
        public int combo133Entries;
        public int combo133Ticks;
        public float combo131Distance;
        public float combo132Distance;
        public float combo133Distance;
        public float comboMinimumTargetDistance = float.PositiveInfinity;
        public bool comboEnteredTargetRadius;
        public bool comboPassedTargetPlane;
        public bool comboHeadingCaptured;
        public Vector3 comboHeading;
        public int comboEntryTick = -1;
        public bool comboEntryOnPress;
        public readonly List<int> comboQueueTicks = new List<int>();
        public int swordCancel132Tick = -1;
        public int swordCancel133Tick = -1;
        public int swordCancel132TransitionTick = -1;
        public int swordCancel133TransitionTick = -1;
        public int swordCancelTransitionEvents;
        public int comboAttackProfileEvents;
        public int comboAttackFlagEvents;
        public bool comboProfileSemanticsValid = true;
        public bool comboSequenceSemanticsValid = true;
        public readonly List<int> comboProcType57Ticks = new List<int>();
        public int comboRecoveryTick = -1;
        public int comboRecoveryTicks;
        public int comboIdleTick = -1;
        public int lockInputTicks;
        public int lockAcquiredTick = -1;
        public TestPlayTargetDummy lockedTargetReference;
        public bool lockStateSemanticsValid = true;
        public int targetRelativeShotEntries;
        public int targetRelativeShotTicks;
        public int targetRelativeShotEntryTick = -1;
        public bool targetRelativeShotEntryOnPress;
        public bool targetRelativeShotSelectionValid = true;
        public int targetRelativeShotTurnTicks;
        public bool targetRelativeShotTurnSemanticsValid = true;
        public bool targetRelativeShotNoTurnOutsideAction = true;
        public int targetRelativeShotRecoveryTick = -1;
        public int targetRelativeShotRecoveryTicks;
        public int targetRelativeShotIdleTick = -1;
    }

    [MenuItem("Tools/WindomXP/Test Play/Run Real-Mech Golden Traces")]
    public static void RunFromMenu()
    {
        WindomVerificationRunner.StartVerification(WindomVerificationSelection.Golden | WindomVerificationSelection.BaselineComparison);
    }

    [MenuItem("Tools/WindomXP/Test Play/Run Selected-Mech GT-001 Reference Trace")]
    public static void RunSelectedMechGt001FromMenu()
    {
        string selectedFolder = EditorUtility.OpenFolderPanel(
            "原作EXEで使用する機体フォルダーを選択",
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Windom_Data", "Robo"), "");
        if (string.IsNullOrEmpty(selectedFolder)) return;
        string id = WindomVerificationRunner.StartVerification(WindomVerificationSelection.SelectedGolden, selectedFolder);
        EditorPrefs.SetString(SelectedMechFolderEditorPref, selectedFolder);
        Debug.Log("[TestPlayGolden] Started " + id);
    }

    [MenuItem("Tools/WindomXP/Test Play/Run Last Selected-Mech GT-001 Reference Trace")]
    public static void RunLastSelectedMechGt001FromMenu()
    {
        string selectedFolder = EditorPrefs.GetString(SelectedMechFolderEditorPref, "");
        if (string.IsNullOrWhiteSpace(selectedFolder))
            throw new InvalidOperationException("Run Selected-Mech GT-001 Reference Trace first.");
        WindomVerificationRunner.StartVerification(WindomVerificationSelection.SelectedGolden, selectedFolder);
    }

    internal static async Task<int> RunForJobAsync(string outputFolder, System.Threading.CancellationToken cancellationToken)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string mechFolder = Path.Combine(projectRoot, "Windom_Data", "Robo", MechId);
        string aniPath = Path.Combine(mechFolder, "Script.ani");
        string sptPath = Path.Combine(mechFolder, "Script.spt");
        if (!File.Exists(aniPath) || !File.Exists(sptPath))
            throw new FileNotFoundException("Real-mech Script.ani/Script.spt is missing: " + mechFolder);

        string aniHash = ComputeFileSha256(aniPath);
        string sptHash = ComputeFileSha256(sptPath);
        Debug.Log("[TestPlayGolden] Loading real ANI: " + MechId + " sha256=" + aniHash);

        ani2 data = new ani2();
        if (!await data.load(aniPath))
            throw new InvalidDataException("ani2.load returned false for " + aniPath);
        if (data.animations == null || data.animations.Count < 200)
            throw new InvalidDataException("Expected the original 200-slot ANI table.");

        CypherTranscoder transcoder = new CypherTranscoder();
        string sptText = USEncoder.ToEncoding.ToUnicode(transcoder.Transcode(sptPath));
        SptRuntimeData sptData = SptParser.Parse(sptText);

        Directory.CreateDirectory(outputFolder);

        int passed = 0;
        IReadOnlyList<TestPlayGoldenScenarioDefinition> definitions = TestPlayGoldenScenarioCatalog.All;
        for (int i = 0; i < definitions.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TestPlayGoldenScenarioDefinition definition = definitions[i];
            ValidateRealDataRequirements(data, definition);
            RunCapture first = RunScenario(data, sptData, definition, aniHash, sptHash, MechId, mechFolder);
            RunCapture second = RunScenario(data, sptData, definition, aniHash, sptHash, MechId, mechFolder);
            int mismatch = TestPlayGoldenTraceSession.FindFirstMismatch(first.session, second.session);
            if (mismatch >= 0)
                throw new InvalidOperationException(definition.id + " diverged at tick index " + mismatch);
            ValidateObservedActions(definition, first.observedActions);
            ValidateFocusedScenarioOutcome(definition, first);

            string firstHash = first.session.ComputeTraceHash();
            string secondHash = second.session.ComputeTraceHash();
            if (!string.Equals(firstHash, secondHash, StringComparison.Ordinal))
                throw new InvalidOperationException(definition.id + " session hashes differ.");

            File.WriteAllText(
                Path.Combine(outputFolder, definition.id + ".unity-reference.jsonl"),
                first.session.SerializeJsonLines(),
                new UTF8Encoding(false));
            WriteMeleeTravelDiagnostic(outputFolder, definition, first);
            Debug.Log("[TestPlayGolden] " + definition.id + " passed ticks=" +
                      first.session.TickCount + " sha256=" + firstHash);
            passed++;
            await Task.Yield();
        }

        Debug.Log("[TestPlayGolden] Passed " + passed + "/" + definitions.Count +
                  " real-mech scenarios twice with exact tick-trace equality. Output=" + outputFolder);
        return passed;
    }

    static void WriteMeleeTravelDiagnostic(
        string outputFolder,
        TestPlayGoldenScenarioDefinition definition,
        RunCapture capture)
    {
        if (definition.id != "GT-008" && definition.id != "GT-009")
            return;

        StringBuilder json = new StringBuilder();
        json.AppendLine("{");
        json.AppendLine("  \"schemaVersion\": 1,");
        json.AppendLine("  \"evidence\": \"RealAniObserved+UnityAdapterDiagnostic\",");
        json.AppendLine("  \"scenarioId\": \"" + definition.id + "\",");
        json.AppendLine("  \"targetRadius\": 1.5,");
        if (definition.id == "GT-008")
        {
            json.AppendLine("  \"coveredActions\": [130, 136],");
            json.AppendLine("  \"action130Distance\": " + JsonFloat(capture.forwardMeleeApproachDistance) + ",");
            json.AppendLine("  \"action136Distance\": " + JsonFloat(capture.forwardMeleeFollowupDistance) + ",");
            json.AppendLine("  \"minimumTargetCenterDistance\": " + JsonFloat(capture.forwardMeleeMinimumTargetDistance) + ",");
            json.AppendLine("  \"enteredTargetRadius\": " + JsonBool(capture.forwardMeleeEnteredTargetRadius) + ",");
            json.AppendLine("  \"passedTargetPlane\": " + JsonBool(capture.forwardMeleePassedTargetPlane));
        }
        else
        {
            json.AppendLine("  \"coveredActions\": [131, 132, 133],");
            json.AppendLine("  \"action131Distance\": " + JsonFloat(capture.combo131Distance) + ",");
            json.AppendLine("  \"action132Distance\": " + JsonFloat(capture.combo132Distance) + ",");
            json.AppendLine("  \"action133Distance\": " + JsonFloat(capture.combo133Distance) + ",");
            json.AppendLine("  \"minimumTargetCenterDistance\": " + JsonFloat(capture.comboMinimumTargetDistance) + ",");
            json.AppendLine("  \"enteredTargetRadius\": " + JsonBool(capture.comboEnteredTargetRadius) + ",");
            json.AppendLine("  \"passedTargetPlane\": " + JsonBool(capture.comboPassedTargetPlane));
        }
        json.AppendLine("}");

        string outputPath = Path.Combine(
            outputFolder, definition.id + ".melee-travel-diagnostic.json");
        File.WriteAllText(outputPath, json.ToString(), new UTF8Encoding(false));
        Debug.Log("[TestPlayGolden] " + definition.id +
                  " melee travel diagnostic written: " + outputPath);
    }

    static string JsonFloat(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    static string JsonBool(bool value)
    {
        return value ? "true" : "false";
    }

    public static Task RunSelectedMechGt001Async(string mechFolder)
    {
        return RunSelectedMechGt001Async(mechFolder, null);
    }

    internal static async Task RunSelectedMechGt001Async(string mechFolder, string outputFolder)
    {
        string aniPath = Path.Combine(mechFolder, "Script.ani");
        string sptPath = Path.Combine(mechFolder, "Script.spt");
        if (!File.Exists(aniPath) || !File.Exists(sptPath))
            throw new FileNotFoundException("Selected folder requires Script.ani and Script.spt: " + mechFolder);

        string mechId = new DirectoryInfo(mechFolder).Name;
        string aniHash = ComputeFileSha256(aniPath);
        string sptHash = ComputeFileSha256(sptPath);
        ani2 data = new ani2();
        if (!await data.load(aniPath))
            throw new InvalidDataException("ani2.load returned false for " + aniPath);
        if (data.animations == null || data.animations.Count < 200)
            throw new InvalidDataException("Expected the original 200-slot ANI table.");

        CypherTranscoder transcoder = new CypherTranscoder();
        string sptText = USEncoder.ToEncoding.ToUnicode(transcoder.Transcode(sptPath));
        SptRuntimeData sptData = SptParser.Parse(sptText);
        TestPlayGoldenScenarioDefinition definition = TestPlayGoldenScenarioCatalog.Find("GT-001");
        ValidateRealDataRequirements(data, definition);

        RunCapture first = RunScenario(data, sptData, definition, aniHash, sptHash, mechId, mechFolder);
        RunCapture second = RunScenario(data, sptData, definition, aniHash, sptHash, mechId, mechFolder);
        int mismatch = TestPlayGoldenTraceSession.FindFirstMismatch(first.session, second.session);
        if (mismatch >= 0)
            throw new InvalidOperationException("GT-001 diverged at tick index " + mismatch);
        ValidateObservedActions(definition, first.observedActions);
        if (!string.Equals(first.session.ComputeTraceHash(), second.session.ComputeTraceHash(),
                StringComparison.Ordinal))
            throw new InvalidOperationException("GT-001 session hashes differ.");

        if (outputFolder == null) outputFolder = GetHashSpecificOutputFolder(aniHash, sptHash);
        Directory.CreateDirectory(outputFolder);
        string outputPath = Path.Combine(outputFolder, "GT-001.unity-reference.jsonl");
        File.WriteAllText(outputPath, first.session.SerializeJsonLines(), new UTF8Encoding(false));
        Debug.Log("[TestPlayGolden] Selected-mech GT-001 passed twice with exact tick-trace equality. " +
                  "mech=" + mechId + " ani=" + aniHash + " spt=" + sptHash + " Output=" + outputPath);
    }

    public static string GetHashSpecificOutputFolder(string aniHash, string sptHash)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        return Path.Combine(projectRoot, "Logs", "TestPlayGolden", "ByDataHash",
            NormalizeHash(aniHash).Substring(0, 16) + "_" + NormalizeHash(sptHash).Substring(0, 16));
    }

    static RunCapture RunScenario(
        ani2 data,
        SptRuntimeData sptData,
        TestPlayGoldenScenarioDefinition definition,
        string aniHash,
        string sptHash,
        string mechId,
        string mechFolder)
    {
        GameObject host = new GameObject("TestPlayGolden_" + definition.id);
        GameObject root = new GameObject("TestPlayGoldenRoot");
        GameObject targetObject = new GameObject("TestPlayGoldenTarget");
        GameObject type1Muzzle = null;
        try
        {
            RoboStructure robo = host.AddComponent<RoboStructure>();
            robo.root = root;
            robo.parts = new List<GameObject> { root };
            robo.ani = data;
            robo.folder = mechFolder;
            robo.transcoder = new CypherTranscoder();

            UI_SPT spt = host.AddComponent<UI_SPT>();
            spt.robo = robo;
            if (definition.id == "GT-007" &&
                sptData.WeaponPoints.TryGetValue(0, out WeaponPointInfo type1WeaponPoint))
            {
                type1Muzzle = new GameObject("TestPlayGoldenWeaponPoint0");
                type1Muzzle.transform.SetParent(root.transform, false);
                type1WeaponPoint.BoneTr = type1Muzzle.transform;
            }
            PropertyInfo property = typeof(UI_SPT).GetProperty(
                "LastSptData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            property.SetValue(spt, sptData, null);

            TestPlayTargetDummy target = targetObject.AddComponent<TestPlayTargetDummy>();
            target.logHits = false;
            targetObject.transform.position = definition.targetPosition;

            TestPlayController controller = host.AddComponent<TestPlayController>();
            controller.robo = robo;
            controller.sptSource = spt;
            controller.target = target;
            controller.useColliderGrounding = false;
            controller.logUnhandledCommands = false;
            controller.logMotionAssignments = false;
            controller.blendActionTransitions = false;
            controller.BeginDeterministicTraceSession(definition.setup);

            RunCapture capture = new RunCapture
            {
                session = new TestPlayGoldenTraceSession(new TestPlayGoldenSessionHeader
                {
                    schemaVersion = 1,
                    source = "unity-core",
                    scenarioId = definition.id,
                    mechId = mechId,
                    aniHash = aniHash,
                    sptHash = sptHash,
                    tickRate = Mathf.RoundToInt(controller.originalTickRate),
                    baseline = definition.baseline
                })
            };

            TestPlayGoldenInputFrame[] frames = definition.ExpandInputFrames();
            for (int i = 0; i < frames.Length; i++)
            {
                float energyBeforeTick = controller.currentEnergy;
                float auxiliaryEnergyBeforeTick = controller.currentAuxiliaryEnergy;
                Vector3 positionBeforeTick = root.transform.position;
                Quaternion rotationBeforeTick = root.transform.rotation;
                string tickTrace = controller.SimulateDeterministicTraceTick(frames[i]);
                capture.session.AddTick(tickTrace);
                capture.observedActions.Add(controller.currentAnimationIndex);
                capture.observedActions.Add(controller.CurrentActionSelection.logicalActionId);
                CaptureFocusedScenarioState(
                    definition.id,
                    capture,
                    controller,
                    frames[i],
                    i + 1,
                    energyBeforeTick,
                    auxiliaryEnergyBeforeTick,
                    positionBeforeTick,
                    root.transform.position,
                    rotationBeforeTick,
                    root.transform.rotation,
                    targetObject.transform.position,
                    target.hitRadius,
                    tickTrace);
            }
            controller.EndDeterministicTraceSession();
            return capture;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(targetObject);
            if (type1Muzzle != null)
                UnityEngine.Object.DestroyImmediate(type1Muzzle);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(host);
        }
    }

    static void ValidateRealDataRequirements(ani2 data, TestPlayGoldenScenarioDefinition definition)
    {
        for (int i = 0; i < definition.requiredActionIds.Length; i++)
        {
            int actionId = definition.requiredActionIds[i];
            if (!HasUsableAction(data, actionId))
                throw new InvalidDataException(definition.id + " requires missing ANI action " + actionId);
        }

        for (int i = 0; i < definition.requiredCommands.Length; i++)
        {
            string command = definition.requiredCommands[i];
            if (!ContainsCommand(data, definition.requiredActionIds, command))
                throw new InvalidDataException(definition.id + " requires missing ANI command " + command);
        }
    }

    static void ValidateObservedActions(
        TestPlayGoldenScenarioDefinition definition,
        HashSet<int> observedActions)
    {
        for (int i = 0; i < definition.requiredActionIds.Length; i++)
        {
            int actionId = definition.requiredActionIds[i];
            if (!observedActions.Contains(actionId))
                throw new InvalidOperationException(definition.id + " did not observe action " + actionId);
        }
    }

    static void CaptureFocusedScenarioState(
        string scenarioId,
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        float energyBeforeTick,
        float auxiliaryEnergyBeforeTick,
        Vector3 positionBeforeTick,
        Vector3 positionAfterTick,
        Quaternion rotationBeforeTick,
        Quaternion rotationAfterTick,
        Vector3 targetPosition,
        float targetRadius,
        string tickTrace)
    {
        int action = controller.currentAnimationIndex;
        int logicalAction = controller.CurrentActionSelection.logicalActionId;
        if (action == controller.riseAction)
        {
            capture.sawRiseAction = true;
            if (capture.lastAnimationIndex != controller.riseAction)
                capture.riseActionEntries++;
            capture.riseActionTicks++;

            animation riseAnimation = controller.robo != null && controller.robo.ani != null &&
                                      controller.robo.ani.animations != null &&
                                      controller.riseAction >= 0 &&
                                      controller.riseAction < controller.robo.ani.animations.Count
                ? controller.robo.ani.animations[controller.riseAction]
                : null;
            int frameCount = riseAnimation != null && riseAnimation.frames != null
                ? riseAnimation.frames.Count
                : 0;
            capture.riseFrameCount = frameCount;
            if (string.IsNullOrEmpty(capture.riseScriptTiming) &&
                riseAnimation != null && riseAnimation.scripts != null)
            {
                StringBuilder timing = new StringBuilder();
                for (int i = 0; i < riseAnimation.scripts.Count; i++)
                {
                    if (i > 0)
                        timing.Append(',');
                    timing.Append(i).Append(':')
                        .Append(riseAnimation.scripts[i].unk).Append('/')
                        .Append(riseAnimation.scripts[i].time.ToString("R", CultureInfo.InvariantCulture));
                }
                capture.riseScriptTiming = timing.ToString();
            }
            capture.maximumRiseFrameIndex = Mathf.Max(
                capture.maximumRiseFrameIndex, controller.frameIndex);
            if (input.rise &&
                capture.riseActionTicks >= controller.riseMinimumReleaseTicks &&
                frameCount > 0 &&
                controller.frameIndex == frameCount - 1)
            {
                capture.consecutiveRiseFinalFrameTicks++;
                capture.maximumRiseFinalFrameTicks = Mathf.Max(
                    capture.maximumRiseFinalFrameTicks,
                    capture.consecutiveRiseFinalFrameTicks);
                // The representative real ANI holds its last HOD frame in a
                // long zero-progress script block instead of reaching the
                // synthetic end-pose flag. Require a sustained hold so a
                // transient final frame or animation loop cannot pass.
                if (capture.consecutiveRiseFinalFrameTicks >= 10)
                    capture.sawRiseFinalFrameWhileHeld = true;
            }
            else
                capture.consecutiveRiseFinalFrameTicks = 0;
        }
        else if (capture.sawRiseAction && !input.rise && action == controller.airIdleAction)
        {
            capture.sawAirIdleAfterRiseRelease = true;
        }

        float energyDelta = energyBeforeTick - controller.currentEnergy;
        float auxiliaryEnergyDelta = auxiliaryEnergyBeforeTick - controller.currentAuxiliaryEnergy;
        Vector3 horizontalDelta = positionAfterTick - positionBeforeTick;
        horizontalDelta.y = 0f;

        if (scenarioId == "GT-004")
        {
            CaptureAirMoveReleaseScenarioState(
                capture, controller, input, logicalAction, energyDelta);
        }
        else if (scenarioId == "GT-005")
        {
            CaptureStepScenarioState(
                capture, controller, input, traceTick, logicalAction, energyDelta, horizontalDelta);
        }
        else if (scenarioId == "GT-006")
        {
            CaptureBoostScenarioState(
                capture, controller, input, traceTick, logicalAction, energyDelta, horizontalDelta, tickTrace);
        }
        else if (scenarioId == "GT-007")
        {
            CaptureShotScenarioState(
                capture, controller, input, traceTick, logicalAction,
                energyDelta, auxiliaryEnergyDelta, tickTrace);
        }
        else if (scenarioId == "GT-008")
        {
            CaptureSwitchAndForwardMeleeScenarioState(
                capture,
                controller,
                input,
                traceTick,
                logicalAction,
                energyDelta,
                horizontalDelta,
                tickTrace);
            CaptureMeleeTravel(
                capture,
                logicalAction,
                controller.meleeAction,
                controller.meleeApproachFollowupAction,
                -1,
                positionBeforeTick,
                positionAfterTick,
                targetPosition,
                targetRadius,
                false);
        }
        else if (scenarioId == "GT-009")
        {
            CaptureSwordCancelComboScenarioState(
                capture, controller, input, traceTick, logicalAction, tickTrace);
            CaptureMeleeTravel(
                capture,
                logicalAction,
                controller.neutralMeleeAction,
                132,
                133,
                positionBeforeTick,
                positionAfterTick,
                targetPosition,
                targetRadius,
                true);
        }
        else if (scenarioId == "GT-010")
        {
            CaptureTargetRelativeShotScenarioState(
                capture,
                controller,
                input,
                traceTick,
                logicalAction,
                rotationBeforeTick,
                rotationAfterTick);
        }

        capture.lastAnimationIndex = action;
        capture.lastLogicalAction = logicalAction;
    }

    static void CaptureAirMoveReleaseScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int logicalAction,
        float energyDelta)
    {
        TestPlayMotionStep step = controller.LastMotionStep;
        bool wasAirMove = capture.lastLogicalAction == controller.airMoveAction;
        bool isAirMove = logicalAction == controller.airMoveAction;
        bool isAirIdleAfterMove = capture.airMoveActionTicks > 0 &&
                                  logicalAction == controller.airIdleAction;

        if (isAirMove)
        {
            if (!wasAirMove)
                capture.airMoveActionEntries++;
            capture.airMoveActionTicks++;

            capture.airMoveSemanticsValid &= input.direction == 8 &&
                NearlyEqual(energyDelta, 0f) &&
                VectorsNearlyEqual(step.forcePerTick, Vector3.zero) &&
                NearlyEqual(step.scriptedVelocityBeforeRetention.x, 0f) &&
                NearlyEqual(step.scriptedVelocityBeforeRetention.y, 0f) &&
                NearlyEqual(step.scriptedVelocityBeforeRetention.z, 0.06f) &&
                NearlyEqual(step.velocityAfterForce.y, step.velocityAfterRiseClamp.y);
        }

        if (wasAirMove && isAirIdleAfterMove)
        {
            capture.airIdleEntriesAfterAirMove++;
            capture.airMoveExitedOnRelease = input.direction == 0;
        }

        if (isAirIdleAfterMove)
        {
            capture.airIdleTicksAfterAirMove++;
            capture.airIdleSemanticsValid &= input.direction == 0 &&
                NearlyEqual(energyDelta, 0f) &&
                VectorsNearlyEqual(step.forcePerTick, Vector3.zero) &&
                NearlyEqual(step.scriptedMoveRetention, 0.99f) &&
                VectorsNearlyEqual(
                    step.scriptedVelocityAfterRetention,
                    step.scriptedVelocityBeforeRetention * 0.99f);

            if (step.scriptedVelocityAfterRetention.sqrMagnitude + 0.00000001f <
                step.scriptedVelocityBeforeRetention.sqrMagnitude)
            {
                capture.sawAirIdleMoveDecay = true;
            }

            if (capture.hasGt004PreviousStep)
            {
                capture.airIdleSemanticsValid &= VectorsNearlyEqual(
                    step.scriptedVelocityBeforeRetention,
                    capture.previousGt004Step.scriptedVelocityAfterRetention);

                bool brakeExpected =
                    capture.airIdleTicksAfterAirMove < controller.airIdleVerticalBrakeTicks &&
                    capture.previousGt004Step.velocityAfterMultiplier.y <
                        controller.airIdleVerticalBrakeVelocityThreshold;
                float expectedVerticalAdjustment = brakeExpected
                    ? controller.airIdleVerticalBrakePerTick
                    : 0f;
                float actualVerticalAdjustment =
                    step.velocityBefore.y - capture.previousGt004Step.velocityAfterMultiplier.y;
                capture.airIdleSemanticsValid &= NearlyEqual(
                    actualVerticalAdjustment, expectedVerticalAdjustment);
                if (brakeExpected && NearlyEqual(
                        actualVerticalAdjustment, controller.airIdleVerticalBrakePerTick))
                {
                    capture.sawAirIdleVerticalBrake = true;
                }
                if (capture.airIdleTicksAfterAirMove == controller.airIdleVerticalBrakeTicks &&
                    NearlyEqual(actualVerticalAdjustment, 0f))
                {
                    capture.sawAirIdleBrakeBoundary = true;
                }
            }

            if (step.velocityAfterMultiplier.y < 0f)
                capture.sawAirIdleDescent = true;
        }

        capture.previousGt004Step = step;
        capture.hasGt004PreviousStep = true;
    }

    static void CaptureShotScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        float movementEnergyDelta,
        float auxiliaryEnergyDelta,
        string tickTrace)
    {
        bool wasShot = capture.lastLogicalAction == controller.shotAction;
        bool isShot = logicalAction == controller.shotAction;
        if (input.shot)
            capture.shotInputTicks++;

        if (isShot)
        {
            if (!wasShot)
            {
                capture.shotActionEntries++;
                capture.shotEntryTick = traceTick;
                capture.shotEntryOnPress = input.shot;
            }
            capture.shotActionTicks++;
        }

        if (wasShot && logicalAction == controller.stepLandingAction)
        {
            capture.shotExitedToRecovery = true;
            capture.shotRecoveryEntryTick = traceTick;
        }
        if (capture.shotExitedToRecovery && logicalAction == controller.stepLandingAction)
            capture.shotRecoveryTicks++;
        if (capture.lastLogicalAction == controller.stepLandingAction &&
            logicalAction == controller.idleAction)
        {
            capture.shotReturnedToIdle = true;
            capture.shotIdleReturnTick = traceTick;
        }

        bool cooldownEvent = TraceContains(tickTrace, "\"type\":\"CooldownSet\"");
        bool profileEvent = TraceContains(tickTrace, "\"source\":\"ATTACK\"");
        bool attackFlagEvent = TraceContains(tickTrace, "\"source\":\"AttackFlag\"");
        bool projectileEvent = TraceContains(tickTrace, "\"type\":\"ProjectileSpawned\"");
        bool procTypeOneEvent =
            TraceContains(tickTrace, "\"type\":\"Proc\"") &&
            TraceContains(tickTrace, "\"command\":\"RunProc2\"") &&
            TraceContains(tickTrace, "\"procType\":1");

        if (cooldownEvent) capture.shotCooldownSetEvents++;
        if (profileEvent) capture.shotProfileEvents++;
        if (attackFlagEvent) capture.shotAttackFlagEvents++;
        if (projectileEvent) capture.shotProjectileEvents++;
        if (procTypeOneEvent) capture.shotProcTypeOneEvents++;

        if (projectileEvent)
        {
            FieldInfo transientField = typeof(TestPlayController).GetField(
                "spawnedTransientObjects",
                BindingFlags.Instance | BindingFlags.NonPublic);
            List<GameObject> transients = transientField != null
                ? transientField.GetValue(controller) as List<GameObject>
                : null;
            GameObject projectileObject = transients != null && transients.Count > 0
                ? transients[transients.Count - 1]
                : null;
            TestPlayProjectile projectile = projectileObject != null
                ? projectileObject.GetComponent<TestPlayProjectile>()
                : null;
            capture.shotType1CoreSemanticsValid &= projectile != null &&
                projectile.useOriginalType1Core &&
                projectile.weaponPointId == 0 &&
                projectile.remainingActiveTicks == TestPlayCombatCore.OriginalType1ActiveTicks &&
                projectile.trailPointCount == 30 &&
                NearlyEqual(projectile.distancePerTick, 1f) &&
                NearlyEqual(projectile.visualWidth, 0.1f) &&
                projectile.collisionKind == TestPlayAttackCollisionKind.OriginalType1;
            capture.shotType1MuzzleSemanticsValid &= projectileObject != null &&
                controller.robo != null && controller.robo.root != null &&
                VectorsNearlyEqual(projectileObject.transform.position, controller.robo.root.transform.position);
            capture.shotType1EnergySemanticsValid &=
                NearlyEqual(movementEnergyDelta, 0f) &&
                NearlyEqual(auxiliaryEnergyDelta, 200f);
        }

        if (cooldownEvent || profileEvent || attackFlagEvent || projectileEvent || procTypeOneEvent)
        {
            bool completeBundle = cooldownEvent && profileEvent && attackFlagEvent &&
                                  projectileEvent && procTypeOneEvent;
            if (completeBundle)
                capture.shotScriptBundleTick = traceTick;

            TestPlayAttackProfile profile = controller.attackProfile;
            capture.shotScriptBundleSemanticsValid &= completeBundle &&
                controller.GetAttackCooldownTicks(0) == 100 &&
                profile != null &&
                profile.power == 100 &&
                profile.down == 200 &&
                NearlyEqual(profile.force, 0.4f) &&
                NearlyEqual(profile.forceY, 0f) &&
                TraceContains(tickTrace, "\"attackFlag\":2") &&
                TraceContains(tickTrace,
                    "\"slot\":0,\"cooldown\":100,\"source\":\"AttackDelay\"") &&
                TraceContains(tickTrace,
                    "\"source\":\"RunProc2:1\",\"damage\":100,\"down\":200," +
                    "\"force\":0.4,\"forceY\":0,\"valueSource\":\"OriginalScriptProfile\"");
        }

        int cooldown = controller.GetAttackCooldownTicks(0);
        if (cooldownEvent)
        {
            capture.shotCooldownTracking = true;
            capture.previousShotCooldown = cooldown;
            capture.shotCooldownDecayValid &= cooldown == 100;
        }
        else if (capture.shotCooldownTracking)
        {
            int expected = Mathf.Max(0, capture.previousShotCooldown - 1);
            capture.shotCooldownDecayValid &= cooldown == expected;
            if (capture.shotCooldownZeroTick < 0 &&
                capture.previousShotCooldown > 0 && cooldown == 0)
            {
                capture.shotCooldownZeroTick = traceTick;
            }
            capture.previousShotCooldown = cooldown;
        }
        else
        {
            capture.shotCooldownDecayValid &= cooldown == 0;
        }
    }

    static void CaptureTargetRelativeShotScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        Quaternion rotationBeforeTick,
        Quaternion rotationAfterTick)
    {
        const int rearShotAction = 103;
        bool wasRearShot = capture.lastLogicalAction == rearShotAction;
        bool isRearShot = logicalAction == rearShotAction;

        if (input.lockTarget)
            capture.lockInputTicks++;
        if (capture.lockAcquiredTick < 0 && controller.targetLockActive)
        {
            capture.lockAcquiredTick = traceTick;
            capture.lockedTargetReference = controller.lockedTarget;
        }
        if (capture.lockAcquiredTick >= 0)
        {
            capture.lockStateSemanticsValid &=
                controller.targetLockActive &&
                ReferenceEquals(controller.lockedTarget, capture.lockedTargetReference) &&
                capture.lockedTargetReference != null &&
                NearlyEqual(controller.activeTargetDistance, 2f) &&
                NearlyEqual(controller.state.GetFloat(99), 2f) &&
                controller.state.GetInt(154) == controller.lockedTarget.stateId;
        }

        float signedYaw = Vector3.SignedAngle(
            rotationBeforeTick * Vector3.forward,
            rotationAfterTick * Vector3.forward,
            Vector3.up);
        if (isRearShot)
        {
            if (!wasRearShot)
            {
                capture.targetRelativeShotEntries++;
                capture.targetRelativeShotEntryTick = traceTick;
                capture.targetRelativeShotEntryOnPress = input.shot && input.direction == 4;
            }
            capture.targetRelativeShotTicks++;
            capture.targetRelativeShotTurnTicks++;
            TestPlayActionSelection selection = controller.CurrentActionSelection;
            capture.targetRelativeShotSelectionValid &=
                selection.requestedActionId == controller.shotAction &&
                selection.logicalActionId == rearShotAction &&
                selection.poseActionId == rearShotAction &&
                selection.scriptActionId == rearShotAction &&
                selection.weaponMode == TestPlayWeaponMode.Gun &&
                selection.primaryChannel == 0 &&
                selection.secondaryChannel == 1 &&
                selection.UsesDualChannels;
            capture.targetRelativeShotTurnSemanticsValid &=
                input.direction == 4 && NearlyEqual(signedYaw, -20f);
        }
        else
        {
            capture.targetRelativeShotNoTurnOutsideAction &= NearlyEqual(signedYaw, 0f);
        }

        if (wasRearShot && logicalAction == controller.stepLandingAction)
            capture.targetRelativeShotRecoveryTick = traceTick;
        if (capture.targetRelativeShotRecoveryTick >= 0 &&
            logicalAction == controller.stepLandingAction)
        {
            capture.targetRelativeShotRecoveryTicks++;
        }
        if (capture.lastLogicalAction == controller.stepLandingAction &&
            logicalAction == controller.idleAction)
        {
            capture.targetRelativeShotIdleTick = traceTick;
        }
    }

    static void CaptureSwitchAndForwardMeleeScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        float energyDelta,
        Vector3 horizontalDelta,
        string tickTrace)
    {
        bool wasSwitch = capture.lastLogicalAction == controller.switchToSwordAction;
        bool isSwitch = logicalAction == controller.switchToSwordAction;
        bool wasForwardMelee = capture.lastLogicalAction == controller.meleeAction;
        bool isForwardMelee = logicalAction == controller.meleeAction;
        bool wasFollowup = capture.lastLogicalAction == controller.meleeApproachFollowupAction;
        bool isFollowup = logicalAction == controller.meleeApproachFollowupAction;

        if (input.melee)
            capture.switchMeleeInputTicks++;

        if (isSwitch)
        {
            if (!wasSwitch)
            {
                capture.switchToSwordEntries++;
                capture.switchToSwordEntryTick = traceTick;
                capture.switchToSwordEntryOnPress = input.melee;
            }
            capture.switchToSwordTicks++;
            capture.switchToSwordSemanticsValid &=
                controller.CurrentActionSelection.weaponMode == TestPlayWeaponMode.Gun &&
                controller.CurrentActionSelection.poseActionId == controller.switchToSwordAction &&
                logicalAction == controller.switchToSwordAction;
        }

        if (wasSwitch && logicalAction == controller.idleAction)
        {
            capture.switchToSwordCompleteTick = traceTick;
            capture.switchToSwordSemanticsValid &=
                controller.CurrentActionSelection.weaponMode == TestPlayWeaponMode.Sword &&
                controller.CurrentActionSelection.poseActionId == controller.idleAction + 50;
        }

        if (IsProcEvent(tickTrace, 55))
            capture.switchProcType55Ticks.Add(traceTick);

        if (isForwardMelee)
        {
            if (!wasForwardMelee)
            {
                capture.forwardMeleeEntries++;
                capture.forwardMeleeEntryTick = traceTick;
                capture.forwardMeleeEntryOnPress = input.melee && input.direction == 8;
                capture.forwardMeleeSemanticsValid &= NearlyEqual(energyDelta, 0f);
            }
            else
            {
                capture.forwardMeleeSemanticsValid &= NearlyEqual(energyDelta, 5f);
                capture.forwardMeleeEnergyDrainTicks++;
                capture.forwardMeleeEnergyConsumed += energyDelta;
            }

            capture.forwardMeleeTicks++;
            if (horizontalDelta.sqrMagnitude > 0.00000001f)
                capture.forwardMeleeMovementTicks++;
            capture.forwardMeleeSemanticsValid &=
                input.direction == 8 &&
                controller.CurrentActionSelection.weaponMode == TestPlayWeaponMode.Sword &&
                TraceContains(tickTrace, "\"meleeApproachActive\":true") &&
                TraceContains(tickTrace, "\"sequenceActive\":true");
        }

        if (wasForwardMelee && isFollowup)
        {
            capture.forwardMeleeEnergyDrainTicks++;
            capture.forwardMeleeEnergyConsumed += energyDelta;
            capture.forwardMeleeFollowupTick = traceTick;
            if (TraceContains(tickTrace, "\"type\":\"ActionTransition\"") &&
                TraceContains(tickTrace, "\"reason\":\"MeleeApproachFollowup\""))
            {
                capture.forwardMeleeFollowupEvents++;
            }
            capture.forwardMeleeFollowupValid =
                NearlyEqual(energyDelta, 5f) &&
                TraceContains(tickTrace, "\"meleeApproachActive\":false") &&
                TraceContains(tickTrace, "\"sequenceActive\":true");
        }

        if (isFollowup)
        {
            if (!wasFollowup)
                capture.forwardMeleeFollowupEntries++;
            capture.forwardMeleeFollowupTicks++;
        }
        if (IsProcEvent(tickTrace, 57))
            capture.forwardMeleeProcType57Ticks.Add(traceTick);

        if (wasFollowup && logicalAction == controller.stepLandingAction)
        {
            capture.forwardMeleeRecoveryTick = traceTick;
            capture.forwardMeleeSemanticsValid &=
                controller.CurrentActionSelection.weaponMode == TestPlayWeaponMode.Sword &&
                controller.CurrentActionSelection.poseActionId == controller.stepLandingAction + 50 &&
                TraceContains(tickTrace, "\"reason\":\"AttackFinished\"");
        }
        if (capture.forwardMeleeRecoveryTick >= 0 &&
            logicalAction == controller.stepLandingAction)
        {
            capture.forwardMeleeRecoveryTicks++;
        }
        if (capture.lastLogicalAction == controller.stepLandingAction &&
            logicalAction == controller.idleAction)
        {
            capture.forwardMeleeIdleTick = traceTick;
            capture.forwardMeleeSemanticsValid &=
                controller.CurrentActionSelection.poseActionId == controller.idleAction + 50;
        }
    }

    static void CaptureMeleeTravel(
        RunCapture capture,
        int logicalAction,
        int firstAction,
        int secondAction,
        int thirdAction,
        Vector3 positionBefore,
        Vector3 positionAfter,
        Vector3 targetPosition,
        float targetRadius,
        bool combo)
    {
        bool isFirst = logicalAction == firstAction;
        bool isSecond = logicalAction == secondAction;
        bool isThird = thirdAction >= 0 && logicalAction == thirdAction;
        if (!isFirst && !isSecond && !isThird)
            return;

        Vector3 beforeHorizontal = positionBefore;
        Vector3 afterHorizontal = positionAfter;
        Vector3 targetHorizontal = targetPosition;
        beforeHorizontal.y = 0f;
        afterHorizontal.y = 0f;
        targetHorizontal.y = 0f;

        Vector3 movement = afterHorizontal - beforeHorizontal;
        float distance = movement.magnitude;
        if (combo)
        {
            if (isFirst) capture.combo131Distance += distance;
            else if (isSecond) capture.combo132Distance += distance;
            else capture.combo133Distance += distance;
        }
        else
        {
            if (isFirst) capture.forwardMeleeApproachDistance += distance;
            else capture.forwardMeleeFollowupDistance += distance;
        }

        Vector3 heading = combo ? capture.comboHeading : capture.forwardMeleeHeading;
        bool headingCaptured = combo
            ? capture.comboHeadingCaptured
            : capture.forwardMeleeHeadingCaptured;
        if (!headingCaptured)
        {
            heading = targetHorizontal - beforeHorizontal;
            if (heading.sqrMagnitude > 0.00000001f)
            {
                heading.Normalize();
                if (combo)
                {
                    capture.comboHeading = heading;
                    capture.comboHeadingCaptured = true;
                }
                else
                {
                    capture.forwardMeleeHeading = heading;
                    capture.forwardMeleeHeadingCaptured = true;
                }
                headingCaptured = true;
            }
        }

        float targetDistance = Vector3.Distance(afterHorizontal, targetHorizontal);
        if (combo)
        {
            capture.comboMinimumTargetDistance = Mathf.Min(
                capture.comboMinimumTargetDistance, targetDistance);
            capture.comboEnteredTargetRadius |= targetDistance <= targetRadius;
            if (headingCaptured)
            {
                capture.comboPassedTargetPlane |=
                    Vector3.Dot(targetHorizontal - beforeHorizontal, heading) > 0f &&
                    Vector3.Dot(targetHorizontal - afterHorizontal, heading) <= 0f;
            }
        }
        else
        {
            capture.forwardMeleeMinimumTargetDistance = Mathf.Min(
                capture.forwardMeleeMinimumTargetDistance, targetDistance);
            capture.forwardMeleeEnteredTargetRadius |= targetDistance <= targetRadius;
            if (headingCaptured)
            {
                capture.forwardMeleePassedTargetPlane |=
                    Vector3.Dot(targetHorizontal - beforeHorizontal, heading) > 0f &&
                    Vector3.Dot(targetHorizontal - afterHorizontal, heading) <= 0f;
            }
        }
    }

    static void CaptureSwordCancelComboScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        string tickTrace)
    {
        const int secondComboAction = 132;
        const int thirdComboAction = 133;
        bool was131 = capture.lastLogicalAction == controller.neutralMeleeAction;
        bool was132 = capture.lastLogicalAction == secondComboAction;
        bool was133 = capture.lastLogicalAction == thirdComboAction;
        bool is131 = logicalAction == controller.neutralMeleeAction;
        bool is132 = logicalAction == secondComboAction;
        bool is133 = logicalAction == thirdComboAction;

        if (input.melee)
            capture.comboMeleeInputTicks++;

        if (is131)
        {
            if (!was131)
            {
                capture.combo131Entries++;
                capture.comboEntryTick = traceTick;
                capture.comboEntryOnPress = input.melee;
                CaptureComboProfileBundle(
                    capture, controller, tickTrace, 20, 50, 0.5f, 0f, 1);
            }
            capture.combo131Ticks++;
        }
        if (is132)
        {
            if (!was132)
            {
                capture.combo132Entries++;
                CaptureComboProfileBundle(
                    capture, controller, tickTrace, 50, 50, 0.5f, 0f, 1);
            }
            capture.combo132Ticks++;
        }
        if (is133)
        {
            if (!was133)
            {
                capture.combo133Entries++;
                CaptureComboProfileBundle(
                    capture, controller, tickTrace, 100, 200, 0.5f, 0f, 8);
            }
            capture.combo133Ticks++;
        }

        if (is131 || is132 || is133)
        {
            capture.comboSequenceSemanticsValid &=
                controller.CurrentActionSelection.weaponMode == TestPlayWeaponMode.Sword &&
                TraceContains(tickTrace, "\"sequenceActive\":true");
        }

        if (TraceContains(tickTrace, "\"type\":\"ComboQueued\""))
        {
            capture.comboQueueTicks.Add(traceTick);
            capture.comboSequenceSemanticsValid &= input.melee &&
                controller.attackProfile != null &&
                GetControllerPrivateInt(controller, "swordCancelAction") < 0 &&
                TraceContains(tickTrace, "\"comboPending\":true");
        }

        if (GetControllerPrivateInt(controller, "swordCancelAction") == secondComboAction)
        {
            capture.swordCancel132Tick = traceTick;
            capture.comboSequenceSemanticsValid &= is131 &&
                TraceContains(tickTrace, "\"comboPending\":true");
        }
        if (GetControllerPrivateInt(controller, "swordCancelAction") == thirdComboAction)
        {
            capture.swordCancel133Tick = traceTick;
            capture.comboSequenceSemanticsValid &= is132 &&
                TraceContains(tickTrace, "\"comboPending\":true");
        }

        if (was131 && is132)
        {
            capture.swordCancel132TransitionTick = traceTick;
            if (IsSwordCancelTransition(tickTrace, secondComboAction))
                capture.swordCancelTransitionEvents++;
            capture.comboSequenceSemanticsValid &=
                controller.attackProfile != null &&
                GetControllerPrivateInt(controller, "swordCancelAction") < 0 &&
                TraceContains(tickTrace, "\"comboPending\":false");
        }
        if (was132 && is133)
        {
            capture.swordCancel133TransitionTick = traceTick;
            if (IsSwordCancelTransition(tickTrace, thirdComboAction))
                capture.swordCancelTransitionEvents++;
            capture.comboSequenceSemanticsValid &=
                controller.attackProfile != null &&
                GetControllerPrivateInt(controller, "swordCancelAction") < 0 &&
                TraceContains(tickTrace, "\"comboPending\":false");
        }

        if (IsProcEvent(tickTrace, 57))
            capture.comboProcType57Ticks.Add(traceTick);

        if (was133 && logicalAction == controller.stepLandingAction)
        {
            capture.comboRecoveryTick = traceTick;
            capture.comboSequenceSemanticsValid &=
                controller.CurrentActionSelection.poseActionId == controller.stepLandingAction + 50 &&
                TraceContains(tickTrace, "\"reason\":\"AttackFinished\"") &&
                TraceContains(tickTrace, "\"sequenceActive\":false");
        }
        if (capture.comboRecoveryTick >= 0 && logicalAction == controller.stepLandingAction)
            capture.comboRecoveryTicks++;
        if (capture.lastLogicalAction == controller.stepLandingAction &&
            logicalAction == controller.idleAction)
        {
            capture.comboIdleTick = traceTick;
            capture.comboSequenceSemanticsValid &=
                controller.CurrentActionSelection.poseActionId == controller.idleAction + 50;
        }
    }

    static void CaptureComboProfileBundle(
        RunCapture capture,
        TestPlayController controller,
        string tickTrace,
        int power,
        int down,
        float force,
        float forceY,
        int attackFlag)
    {
        bool profileEvent = TraceContains(tickTrace, "\"source\":\"ATTACK\"");
        bool flagEvent = TraceContains(tickTrace, "\"source\":\"AttackFlag\"");
        if (profileEvent) capture.comboAttackProfileEvents++;
        if (flagEvent) capture.comboAttackFlagEvents++;

        TestPlayAttackProfile profile = controller.attackProfile;
        capture.comboProfileSemanticsValid &=
            profileEvent && flagEvent && profile != null &&
            profile.power == power &&
            profile.down == down &&
            NearlyEqual(profile.force, force) &&
            NearlyEqual(profile.forceY, forceY) &&
            TraceContains(tickTrace, "\"attackFlag\":" + attackFlag);
    }

    static bool IsProcEvent(string tickTrace, int procType)
    {
        return TraceContains(tickTrace, "\"type\":\"Proc\"") &&
               TraceContains(tickTrace, "\"command\":\"RunProc2\"") &&
               TraceContains(tickTrace, "\"procType\":" + procType);
    }

    static bool IsSwordCancelTransition(string tickTrace, int targetAction)
    {
        return TraceContains(tickTrace, "\"type\":\"ActionTransition\"") &&
               TraceContains(tickTrace, "\"targetAction\":" + targetAction) &&
               TraceContains(tickTrace, "\"reason\":\"SwordCancel\"");
    }

    static void CaptureStepScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        float energyDelta,
        Vector3 horizontalDelta)
    {
        bool wasStep = IsStepAction(controller, capture.lastLogicalAction);
        bool isStep = IsStepAction(controller, logicalAction);
        if (traceTick == 1 && input.direction != 0 && !isStep)
            capture.sawFirstStepTapWithoutStep = true;

        if (isStep)
        {
            if (!wasStep)
            {
                capture.stepActionEntries++;
                capture.stepEntryTick = traceTick;
                capture.stepEntryAction = logicalAction;
                capture.stepEntryOnSecondTap =
                    capture.sawFirstStepTapWithoutStep && input.direction == 8;
                capture.stepEnergySemanticsValid &= NearlyEqual(energyDelta, 0f);
            }

            capture.stepActionTicks++;
            if (horizontalDelta.sqrMagnitude > 0.000001f)
                capture.stepHorizontalMovementObserved = true;
        }

        // UpdateOriginalMovementEnergy runs before action selection. Therefore
        // the entry tick is free, every following active tick costs 4, and the
        // transition tick pays the final 4 before leaving the step action.
        if (wasStep)
        {
            capture.stepEnergyDrainTicks++;
            capture.stepEnergyConsumed += energyDelta;
            capture.stepEnergySemanticsValid &=
                NearlyEqual(energyDelta, Mathf.Max(0f, controller.stepEnergyPerTick));
        }

        if (wasStep && !isStep)
        {
            capture.stepExitTick = traceTick;
            capture.stepExitAction = logicalAction;
            capture.stepExitedAfterRelease =
                input.direction == 0 && logicalAction == controller.stepLandingAction;
        }
    }

    static void CaptureBoostScenarioState(
        RunCapture capture,
        TestPlayController controller,
        TestPlayGoldenInputFrame input,
        int traceTick,
        int logicalAction,
        float energyDelta,
        Vector3 horizontalDelta,
        string tickTrace)
    {
        bool wasBoost = capture.lastLogicalAction == controller.boostAction;
        bool isBoost = logicalAction == controller.boostAction;
        if (traceTick == 1 && input.rise && !isBoost)
            capture.sawFirstBoostTapWithoutBoost = true;

        if (isBoost)
        {
            if (!wasBoost)
            {
                capture.boostActionEntries++;
                capture.boostEntryTick = traceTick;
                capture.boostEntryOnSecondTap =
                    capture.sawFirstBoostTapWithoutBoost && input.rise;
                capture.boostEntryEnergyCost = energyDelta;
                float expectedEntryCost = Mathf.Floor(Mathf.Max(0f, controller.maximumEnergy) / 5f);
                capture.boostEnergySemanticsValid &= NearlyEqual(energyDelta, expectedEntryCost);
            }

            capture.boostActionTicks++;
            if (capture.boostActionTicks <= 5)
            {
                if (horizontalDelta.sqrMagnitude > 0.000001f)
                    capture.boostInitialFiveTicksStationary = false;
            }
            else if (horizontalDelta.sqrMagnitude > 0.000001f)
            {
                capture.boostMovedAfterInitialWindow = true;
            }
        }

        // As with step, the normal per-tick drain begins on the tick after
        // entry and includes the tick that transitions out of action 22.
        if (wasBoost)
        {
            capture.boostEnergyDrainTicks++;
            capture.boostPerTickEnergyConsumed += energyDelta;
            capture.boostEnergySemanticsValid &=
                NearlyEqual(energyDelta, Mathf.Max(0f, controller.boostEnergyPerTick));
        }

        if (wasBoost && !isBoost)
        {
            capture.boostExitTick = traceTick;
            capture.boostExitAction = logicalAction;
            capture.boostExitedAfterRelease =
                !input.rise && logicalAction == controller.airIdleAction;
        }

        bool windLineProc =
            TraceContains(tickTrace, "\"type\":\"Proc\"") &&
            TraceContains(tickTrace, "\"command\":\"RunProc\"") &&
            TraceContains(tickTrace, "\"procType\":53");
        bool windRingProc =
            TraceContains(tickTrace, "\"type\":\"Proc\"") &&
            TraceContains(tickTrace, "\"command\":\"RunProc\"") &&
            TraceContains(tickTrace, "\"procType\":54");
        if (windLineProc) capture.boostWindLineProcEvents++;
        if (windRingProc) capture.boostWindRingProcEvents++;

        bool windProjectile =
            TraceContains(tickTrace, "\"type\":\"ProjectileSpawned\"") &&
            (TraceContains(tickTrace, "\"source\":\"RunProc:53\"") ||
             TraceContains(tickTrace, "\"source\":\"RunProc:54\""));
        if (windProjectile) capture.boostWindProjectileEvents++;
    }

    static bool IsStepAction(TestPlayController controller, int action)
    {
        return action == controller.forwardStepAction ||
               action == controller.backStepAction ||
               action == controller.leftStepAction ||
               action == controller.rightStepAction;
    }

    static bool NearlyEqual(float actual, float expected)
    {
        return Mathf.Abs(actual - expected) < 0.0001f;
    }

    static bool VectorsNearlyEqual(Vector3 actual, Vector3 expected)
    {
        return (actual - expected).sqrMagnitude < 0.00000001f;
    }

    static bool TraceContains(string tickTrace, string value)
    {
        return !string.IsNullOrEmpty(tickTrace) &&
               tickTrace.IndexOf(value, StringComparison.Ordinal) >= 0;
    }

    static int GetControllerPrivateInt(TestPlayController controller, string fieldName)
    {
        FieldInfo field = typeof(TestPlayController).GetField(
            fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(typeof(TestPlayController).Name, fieldName);
        return (int)field.GetValue(controller);
    }

    static void ValidateFocusedScenarioOutcome(
        TestPlayGoldenScenarioDefinition definition,
        RunCapture capture)
    {
        if (definition.id == "GT-002")
        {
            if (capture.riseActionEntries != 1 || !capture.sawAirIdleAfterRiseRelease)
            {
                throw new InvalidOperationException(
                    "GT-002 must enter rise action 7 once and leave it for air-stop action 8 after Z release.");
            }
            return;
        }

        if (definition.id == "GT-003" &&
            (capture.riseActionEntries != 1 ||
             !capture.sawRiseFinalFrameWhileHeld ||
             !capture.sawAirIdleAfterRiseRelease))
        {
            throw new InvalidOperationException(
                "GT-003 must play rise action 7 once, keep its final HOD frame while Z is held, " +
                "and leave it for air-stop action 8 after release. " +
                "entries=" + capture.riseActionEntries +
                " riseTicks=" + capture.riseActionTicks +
                " maxFrame=" + capture.maximumRiseFrameIndex +
                " frameCount=" + capture.riseFrameCount +
                " scriptTiming=" + capture.riseScriptTiming +
                " finalFrameTicks=" + capture.maximumRiseFinalFrameTicks +
                " finalFrameHeld=" + capture.sawRiseFinalFrameWhileHeld +
                " airIdleAfterRelease=" + capture.sawAirIdleAfterRiseRelease);
        }

        if (definition.id == "GT-004")
        {
            if (capture.airMoveActionEntries != 1 ||
                capture.airMoveActionTicks != 15 ||
                capture.airIdleEntriesAfterAirMove != 1 ||
                capture.airIdleTicksAfterAirMove != 35 ||
                !capture.airMoveExitedOnRelease ||
                !capture.airMoveSemanticsValid ||
                !capture.airIdleSemanticsValid ||
                !capture.sawAirIdleMoveDecay ||
                !capture.sawAirIdleVerticalBrake ||
                !capture.sawAirIdleBrakeBoundary ||
                !capture.sawAirIdleDescent)
            {
                throw new InvalidOperationException(
                    "GT-004 must run real-ANI air-move action 4 for 15 ticks with Move(0,0,0.06) " +
                    "while filtering its positive raw Force Y 0.04 then 0.02 from physical lift, " +
                    "release to air-stop action 8 for 35 ticks, retain " +
                    "inherited Move by 0.99, apply +0.012 vertical braking only before tick 31 " +
                    "below Y 0.05, then resume gravity-driven descent. " +
                    "airMoveEntries=" + capture.airMoveActionEntries +
                    " airMoveTicks=" + capture.airMoveActionTicks +
                    " airIdleEntries=" + capture.airIdleEntriesAfterAirMove +
                    " airIdleTicks=" + capture.airIdleTicksAfterAirMove +
                    " released=" + capture.airMoveExitedOnRelease +
                    " airMoveValid=" + capture.airMoveSemanticsValid +
                    " airIdleValid=" + capture.airIdleSemanticsValid +
                    " moveDecay=" + capture.sawAirIdleMoveDecay +
                    " verticalBrake=" + capture.sawAirIdleVerticalBrake +
                    " brakeBoundary=" + capture.sawAirIdleBrakeBoundary +
                    " descended=" + capture.sawAirIdleDescent);
            }
            return;
        }

        if (definition.id == "GT-005")
        {
            bool durationWithinOriginalBoundary =
                capture.stepActionTicks >= 16 && capture.stepActionTicks <= 61;
            bool totalEnergyMatches = NearlyEqual(
                capture.stepEnergyConsumed,
                capture.stepEnergyDrainTicks * 4f);
            if (!capture.sawFirstStepTapWithoutStep ||
                capture.stepActionEntries != 1 ||
                capture.stepEntryAction != 11 ||
                !capture.stepEntryOnSecondTap ||
                !durationWithinOriginalBoundary ||
                !capture.stepExitedAfterRelease ||
                !capture.stepHorizontalMovementObserved ||
                !capture.stepEnergySemanticsValid ||
                capture.stepEnergyDrainTicks != capture.stepActionTicks ||
                !totalEnergyMatches)
            {
                throw new InvalidOperationException(
                    "GT-005 must reject the first direction tap, enter forward step action 11 once " +
                    "on the second tap, move horizontally, drain 4 movement-energy units per step tick, " +
                    "and leave through grounded step landing action 6 after release inside ticks 16-61. " +
                    "firstTapRejected=" + capture.sawFirstStepTapWithoutStep +
                    " entries=" + capture.stepActionEntries +
                    " entryTick=" + capture.stepEntryTick +
                    " entryAction=" + capture.stepEntryAction +
                    " entryOnSecondTap=" + capture.stepEntryOnSecondTap +
                    " actionTicks=" + capture.stepActionTicks +
                    " exitTick=" + capture.stepExitTick +
                    " exitAction=" + capture.stepExitAction +
                    " moved=" + capture.stepHorizontalMovementObserved +
                    " energyValid=" + capture.stepEnergySemanticsValid +
                    " drainTicks=" + capture.stepEnergyDrainTicks +
                    " consumed=" + capture.stepEnergyConsumed.ToString("R", CultureInfo.InvariantCulture));
            }
            return;
        }

        if (definition.id == "GT-006")
        {
            bool durationPastOriginalReleaseBoundary = capture.boostActionTicks >= 31;
            bool totalEnergyMatches = NearlyEqual(
                capture.boostPerTickEnergyConsumed,
                capture.boostEnergyDrainTicks * 5f);
            if (!capture.sawFirstBoostTapWithoutBoost ||
                capture.boostActionEntries != 1 ||
                !capture.boostEntryOnSecondTap ||
                !durationPastOriginalReleaseBoundary ||
                !capture.boostExitedAfterRelease ||
                !capture.boostInitialFiveTicksStationary ||
                !capture.boostMovedAfterInitialWindow ||
                !capture.boostEnergySemanticsValid ||
                capture.boostEnergyDrainTicks != capture.boostActionTicks ||
                !totalEnergyMatches ||
                capture.boostWindLineProcEvents < 1 ||
                capture.boostWindRingProcEvents < 1 ||
                capture.boostWindProjectileEvents != 0)
            {
                throw new InvalidOperationException(
                    "GT-006 must reject the first Z tap, enter boost action 22 once on the second tap, " +
                    "charge Generator/5 once, drain 5 movement-energy units per boost tick, keep the " +
                    "representative ANI stationary for its first five action ticks, then move and leave " +
                    "through air-stop action 8 after release beyond tick 30. " +
                    "firstTapRejected=" + capture.sawFirstBoostTapWithoutBoost +
                    " entries=" + capture.boostActionEntries +
                    " entryTick=" + capture.boostEntryTick +
                    " entryOnSecondTap=" + capture.boostEntryOnSecondTap +
                    " actionTicks=" + capture.boostActionTicks +
                    " exitTick=" + capture.boostExitTick +
                    " exitAction=" + capture.boostExitAction +
                    " entryCost=" + capture.boostEntryEnergyCost.ToString("R", CultureInfo.InvariantCulture) +
                    " initialStationary=" + capture.boostInitialFiveTicksStationary +
                    " movedAfterInitial=" + capture.boostMovedAfterInitialWindow +
                    " energyValid=" + capture.boostEnergySemanticsValid +
                    " drainTicks=" + capture.boostEnergyDrainTicks +
                    " perTickConsumed=" + capture.boostPerTickEnergyConsumed.ToString("R", CultureInfo.InvariantCulture) +
                    " windLineProc=" + capture.boostWindLineProcEvents +
                    " windRingProc=" + capture.boostWindRingProcEvents +
                    " windProjectiles=" + capture.boostWindProjectileEvents);
            }
            return;
        }

        if (definition.id == "GT-007")
        {
            if (capture.shotInputTicks != 1 ||
                capture.shotActionEntries != 1 ||
                capture.shotActionTicks != 39 ||
                capture.shotEntryTick != 3 ||
                !capture.shotEntryOnPress ||
                capture.shotScriptBundleTick != 18 ||
                capture.shotCooldownSetEvents != 1 ||
                capture.shotProfileEvents != 1 ||
                capture.shotAttackFlagEvents != 1 ||
                capture.shotProjectileEvents != 1 ||
                capture.shotProcTypeOneEvents != 1 ||
                !capture.shotScriptBundleSemanticsValid ||
                !capture.shotType1CoreSemanticsValid ||
                !capture.shotType1MuzzleSemanticsValid ||
                !capture.shotType1EnergySemanticsValid ||
                !capture.shotCooldownDecayValid ||
                capture.shotCooldownZeroTick != 118 ||
                !capture.shotExitedToRecovery ||
                capture.shotRecoveryEntryTick != 42 ||
                capture.shotRecoveryTicks != 34 ||
                !capture.shotReturnedToIdle ||
                capture.shotIdleReturnTick != 76)
            {
                throw new InvalidOperationException(
                    "GT-007 must accept one X press edge at tick 3, run shot action 100 once for " +
                    "39 ticks, and execute the representative real ANI bundle at tick 18: " +
                    "AttackDelay(0,100), ATTACK(100,200,0.4,0), AttackFlag=2, and one RunProc2 " +
                    "type-1 projectile using its WEAPONPOINT, p0 energy charge, p2/100 movement, " +
                    "p1 trail count, fixed 300-tick Core, and OriginalScriptProfile. Cooldown slot 0 must decay " +
                    "once per 60 Hz tick to zero at tick 118, while the action exits through " +
                    "grounded recovery action 6 at tick 42 and returns to idle 0 at tick 76. " +
                    "inputTicks=" + capture.shotInputTicks +
                    " entries=" + capture.shotActionEntries +
                    " actionTicks=" + capture.shotActionTicks +
                    " entryTick=" + capture.shotEntryTick +
                    " entryOnPress=" + capture.shotEntryOnPress +
                    " bundleTick=" + capture.shotScriptBundleTick +
                    " cooldownEvents=" + capture.shotCooldownSetEvents +
                    " profileEvents=" + capture.shotProfileEvents +
                    " attackFlagEvents=" + capture.shotAttackFlagEvents +
                    " projectileEvents=" + capture.shotProjectileEvents +
                    " procTypeOneEvents=" + capture.shotProcTypeOneEvents +
                    " bundleValid=" + capture.shotScriptBundleSemanticsValid +
                    " type1CoreValid=" + capture.shotType1CoreSemanticsValid +
                    " type1MuzzleValid=" + capture.shotType1MuzzleSemanticsValid +
                    " type1EnergyValid=" + capture.shotType1EnergySemanticsValid +
                    " cooldownValid=" + capture.shotCooldownDecayValid +
                    " cooldownZeroTick=" + capture.shotCooldownZeroTick +
                    " recoveryTick=" + capture.shotRecoveryEntryTick +
                    " recoveryTicks=" + capture.shotRecoveryTicks +
                    " idleTick=" + capture.shotIdleReturnTick);
            }
            return;
        }

        if (definition.id == "GT-008")
        {
            if (capture.switchMeleeInputTicks != 2 ||
                capture.switchToSwordEntries != 1 ||
                capture.switchToSwordTicks != 21 ||
                capture.switchToSwordEntryTick != 1 ||
                !capture.switchToSwordEntryOnPress ||
                capture.switchToSwordCompleteTick != 22 ||
                !capture.switchToSwordSemanticsValid ||
                !TicksEqual(capture.switchProcType55Ticks, 6) ||
                capture.forwardMeleeEntries != 1 ||
                capture.forwardMeleeTicks != 6 ||
                capture.forwardMeleeEntryTick != 62 ||
                !capture.forwardMeleeEntryOnPress ||
                !capture.forwardMeleeSemanticsValid ||
                capture.forwardMeleeMovementTicks < 1 ||
                capture.forwardMeleeMovementTicks > 6 ||
                capture.forwardMeleeEnergyDrainTicks != 6 ||
                !NearlyEqual(capture.forwardMeleeEnergyConsumed, 30f) ||
                capture.forwardMeleeFollowupEvents != 1 ||
                capture.forwardMeleeFollowupTick != 68 ||
                !capture.forwardMeleeFollowupValid ||
                capture.forwardMeleeFollowupEntries != 1 ||
                capture.forwardMeleeFollowupTicks != 59 ||
                !TicksEqual(capture.forwardMeleeProcType57Ticks, 68, 78, 83, 88, 93, 98, 103) ||
                capture.forwardMeleeRecoveryTick != 127 ||
                capture.forwardMeleeRecoveryTicks != 34 ||
                capture.forwardMeleeIdleTick != 161 ||
                capture.forwardMeleeMinimumTargetDistance < 1.4999f ||
                capture.forwardMeleePassedTargetPlane)
            {
                throw new InvalidOperationException(
                    "GT-008 must accept one C edge at tick 1, run switch action 18 once for 21 " +
                    "ticks, and return at tick 22 using sword idle pose 50. Direction 8 plus a " +
                    "second C edge at tick 62 must enter approach action 130 once for six ticks, " +
                    "retain the six-tick real-ANI approach while its Unity Adapter stops applied " +
                    "root movement at the target sphere, drain 5 movement-energy units on six updates, " +
                    "and transition to action 136 at tick 68. The representative real ANI must " +
                    "schedule RunProc2 type 55/57 at the recorded ticks, then recover through " +
                    "sword pose 56 at tick 127 and idle pose 50 at tick 161. Proc scheduling is " +
                    "checked as RealAniObserved; type-57 hit geometry and hit success are excluded. " +
                    "inputs=" + capture.switchMeleeInputTicks +
                    " switchEntries=" + capture.switchToSwordEntries +
                    " switchTicks=" + capture.switchToSwordTicks +
                    " switchEntry=" + capture.switchToSwordEntryTick +
                    " switchComplete=" + capture.switchToSwordCompleteTick +
                    " switchValid=" + capture.switchToSwordSemanticsValid +
                    " type55=" + FormatTicks(capture.switchProcType55Ticks) +
                    " approachEntries=" + capture.forwardMeleeEntries +
                    " approachTicks=" + capture.forwardMeleeTicks +
                    " approachEntry=" + capture.forwardMeleeEntryTick +
                    " approachValid=" + capture.forwardMeleeSemanticsValid +
                    " moveTicks=" + capture.forwardMeleeMovementTicks +
                    " drainTicks=" + capture.forwardMeleeEnergyDrainTicks +
                    " energy=" + capture.forwardMeleeEnergyConsumed.ToString("R", CultureInfo.InvariantCulture) +
                    " followupEvents=" + capture.forwardMeleeFollowupEvents +
                    " followupTick=" + capture.forwardMeleeFollowupTick +
                    " followupValid=" + capture.forwardMeleeFollowupValid +
                    " followupEntries=" + capture.forwardMeleeFollowupEntries +
                    " followupTicks=" + capture.forwardMeleeFollowupTicks +
                    " type57=" + FormatTicks(capture.forwardMeleeProcType57Ticks) +
                    " approachDistance=" + capture.forwardMeleeApproachDistance.ToString("R", CultureInfo.InvariantCulture) +
                    " followupDistance=" + capture.forwardMeleeFollowupDistance.ToString("R", CultureInfo.InvariantCulture) +
                    " minTargetDistance=" + capture.forwardMeleeMinimumTargetDistance.ToString("R", CultureInfo.InvariantCulture) +
                    " passedTarget=" + capture.forwardMeleePassedTargetPlane +
                    " recoveryTick=" + capture.forwardMeleeRecoveryTick +
                    " recoveryTicks=" + capture.forwardMeleeRecoveryTicks +
                    " idleTick=" + capture.forwardMeleeIdleTick);
            }
            return;
        }

        if (definition.id == "GT-009")
        {
            if (capture.comboMeleeInputTicks != 3 ||
                capture.combo131Entries != 1 ||
                capture.combo131Ticks != 21 ||
                capture.combo132Entries != 1 ||
                capture.combo132Ticks != 16 ||
                capture.combo133Entries != 1 ||
                capture.combo133Ticks != 54 ||
                capture.comboEntryTick != 1 ||
                !capture.comboEntryOnPress ||
                !TicksEqual(capture.comboQueueTicks, 12, 33) ||
                capture.swordCancel132Tick != 21 ||
                capture.swordCancel133Tick != 37 ||
                capture.swordCancel132TransitionTick != 22 ||
                capture.swordCancel133TransitionTick != 38 ||
                capture.swordCancelTransitionEvents != 2 ||
                capture.comboAttackProfileEvents != 3 ||
                capture.comboAttackFlagEvents != 3 ||
                !capture.comboProfileSemanticsValid ||
                !capture.comboSequenceSemanticsValid ||
                !TicksEqual(capture.comboProcType57Ticks, 1, 11, 16, 21, 22, 27, 32, 37, 58, 63) ||
                capture.comboRecoveryTick != 92 ||
                capture.comboRecoveryTicks != 34 ||
                capture.comboIdleTick != 126 ||
                capture.comboMinimumTargetDistance < 1.4999f ||
                capture.comboPassedTargetPlane)
            {
                throw new InvalidOperationException(
                    "GT-009 must enter actions 131, 132, and 133 once for 21, 16, and 54 ticks. " +
                    "C edges at ticks 12 and 33 must queue without an immediate transition; " +
                    "SwordCancel 132/133 must appear at ticks 21/37 and transition at ticks " +
                    "22/38. Each action entry must apply its representative ATTACK and AttackFlag " +
                    "profile, while RunProc2 type 57 is scheduled at the recorded real-ANI ticks. " +
                    "The Unity Adapter must stop applied root movement at the target sphere without " +
                    "changing the real-ANI action sequence. The combo must recover through sword pose " +
                    "56 at tick 92 and idle pose 50 at " +
                    "tick 126. Proc scheduling is checked as RealAniObserved; type-57 hit geometry, " +
                    "multi-hit rules, and hit success are excluded. inputs=" + capture.comboMeleeInputTicks +
                    " action131=" + capture.combo131Entries + "/" + capture.combo131Ticks +
                    " action132=" + capture.combo132Entries + "/" + capture.combo132Ticks +
                    " action133=" + capture.combo133Entries + "/" + capture.combo133Ticks +
                    " entryTick=" + capture.comboEntryTick +
                    " queues=" + FormatTicks(capture.comboQueueTicks) +
                    " cancel132=" + capture.swordCancel132Tick +
                    " cancel133=" + capture.swordCancel133Tick +
                    " transition132=" + capture.swordCancel132TransitionTick +
                    " transition133=" + capture.swordCancel133TransitionTick +
                    " transitionEvents=" + capture.swordCancelTransitionEvents +
                    " profileEvents=" + capture.comboAttackProfileEvents +
                    " flagEvents=" + capture.comboAttackFlagEvents +
                    " profileValid=" + capture.comboProfileSemanticsValid +
                    " sequenceValid=" + capture.comboSequenceSemanticsValid +
                    " type57=" + FormatTicks(capture.comboProcType57Ticks) +
                    " action131Distance=" + capture.combo131Distance.ToString("R", CultureInfo.InvariantCulture) +
                    " action132Distance=" + capture.combo132Distance.ToString("R", CultureInfo.InvariantCulture) +
                    " action133Distance=" + capture.combo133Distance.ToString("R", CultureInfo.InvariantCulture) +
                    " minTargetDistance=" + capture.comboMinimumTargetDistance.ToString("R", CultureInfo.InvariantCulture) +
                    " passedTarget=" + capture.comboPassedTargetPlane +
                    " recoveryTick=" + capture.comboRecoveryTick +
                    " recoveryTicks=" + capture.comboRecoveryTicks +
                    " idleTick=" + capture.comboIdleTick);
            }
            return;
        }

        if (definition.id == "GT-010")
        {
            if (capture.lockInputTicks != 1 ||
                capture.lockAcquiredTick != 1 ||
                !capture.lockStateSemanticsValid ||
                capture.targetRelativeShotEntries != 1 ||
                capture.targetRelativeShotEntryTick != 4 ||
                !capture.targetRelativeShotEntryOnPress ||
                !capture.targetRelativeShotSelectionValid ||
                capture.targetRelativeShotTicks != 49 ||
                capture.targetRelativeShotTurnTicks != 49 ||
                !capture.targetRelativeShotTurnSemanticsValid ||
                !capture.targetRelativeShotNoTurnOutsideAction ||
                capture.targetRelativeShotRecoveryTick != 53 ||
                capture.targetRelativeShotRecoveryTicks != 34 ||
                capture.targetRelativeShotIdleTick != 87)
            {
                throw new InvalidOperationException(
                    "GT-010 must acquire and retain the rear target lock from one S edge at tick 1, " +
                    "select rear-shot action 103 from the grounded base action 100 on the X+left " +
                    "edge at tick 4, execute action 103 as pose/script on channels 0 and 1, and " +
                    "apply its real-ANI ShotTurnAng=20 as exactly -20 degrees of root yaw on each " +
                    "of the 49 action-103 ticks only. The shot must recover through action 6 at " +
                    "tick 53 for 34 ticks and return to idle at tick 87. lockInputs=" + capture.lockInputTicks +
                    " lockTick=" + capture.lockAcquiredTick +
                    " lockValid=" + capture.lockStateSemanticsValid +
                    " entries=" + capture.targetRelativeShotEntries +
                    " entryTick=" + capture.targetRelativeShotEntryTick +
                    " entryOnPress=" + capture.targetRelativeShotEntryOnPress +
                    " selectionValid=" + capture.targetRelativeShotSelectionValid +
                    " actionTicks=" + capture.targetRelativeShotTicks +
                    " turnTicks=" + capture.targetRelativeShotTurnTicks +
                    " turnValid=" + capture.targetRelativeShotTurnSemanticsValid +
                    " noOutsideTurn=" + capture.targetRelativeShotNoTurnOutsideAction +
                    " recoveryTick=" + capture.targetRelativeShotRecoveryTick +
                    " recoveryTicks=" + capture.targetRelativeShotRecoveryTicks +
                    " idleTick=" + capture.targetRelativeShotIdleTick);
            }
        }
    }

    static bool TicksEqual(List<int> actual, params int[] expected)
    {
        if (actual == null || expected == null || actual.Count != expected.Length)
            return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (actual[i] != expected[i])
                return false;
        }
        return true;
    }

    static string FormatTicks(List<int> ticks)
    {
        return ticks == null ? "(null)" : string.Join(",", ticks);
    }

    static bool HasUsableAction(ani2 data, int actionId)
    {
        if (data == null || data.animations == null || actionId < 0 || actionId >= data.animations.Count)
            return false;
        animation candidate = data.animations[actionId];
        return candidate != null &&
               ((candidate.scripts != null && candidate.scripts.Count > 0) ||
                (candidate.frames != null && candidate.frames.Count > 0));
    }

    static bool ContainsCommand(ani2 data, int[] actionIds, string command)
    {
        if (data == null || data.animations == null || actionIds == null ||
            string.IsNullOrWhiteSpace(command))
            return false;
        for (int i = 0; i < actionIds.Length; i++)
        {
            int actionId = actionIds[i];
            if (actionId < 0 || actionId >= data.animations.Count)
                continue;
            animation candidate = data.animations[actionId];
            if (candidate == null)
                continue;
            if (candidate.scripts == null)
                continue;
            for (int scriptIndex = 0; scriptIndex < candidate.scripts.Count; scriptIndex++)
            {
                if (!string.IsNullOrEmpty(candidate.scripts[scriptIndex].squirrel) &&
                    candidate.scripts[scriptIndex].squirrel.IndexOf(command, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        return false;
    }

    static string ComputeFileSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
            return builder.ToString();
        }
    }

    static string NormalizeHash(string value)
    {
        string normalized = (value ?? "").Trim().ToLowerInvariant();
        if (normalized.Length != 64)
            throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", nameof(value));
        for (int i = 0; i < normalized.Length; i++)
        {
            char c = normalized[i];
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                throw new ArgumentException("SHA-256 contains a non-hexadecimal character.", nameof(value));
        }
        return normalized;
    }
}
