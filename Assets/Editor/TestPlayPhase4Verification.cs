using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class TestPlayPhase4Verification
{
    const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

    public static int RunAll()
    {
        int assertions = 0;
        VerifyTypedCommandEvents(ref assertions);
        VerifyBurnerBoundaries(ref assertions);
        VerifyProcAndVisualEvidence(ref assertions);
        VerifyPresentationTrace(ref assertions);
        VerifyControllerAdapter(ref assertions);
        return assertions;
    }

    static void VerifyTypedCommandEvents(ref int assertions)
    {
        List<TestPlayScriptValue> soundArguments = Values(9f);
        TestPlayPresentationEvent sound = TestPlayPresentationCore.CreateSound(
            soundArguments, TestPlayPresentationAdapterKind.AudioClip);
        Require(sound.type == TestPlayPresentationEventType.Sound && sound.command == "Snd",
            "Snd becomes a typed presentation event", ref assertions);
        Require(sound.originalId == 9 && sound.symbol == "9",
            "Snd retains the original fixed ID", ref assertions);
        Require(sound.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed &&
                sound.adapter == TestPlayPresentationAdapterKind.AudioClip,
            "Snd evidence and Unity adapter are independent fields", ref assertions);
        soundArguments[0] = TestPlayScriptValue.Number(3f);
        Require(sound.arguments.Length == 1 && sound.arguments[0].AsInt() == 9,
            "presentation arguments are immutable snapshots", ref assertions);

        TestPlayPresentationEvent voice = TestPlayPresentationCore.CreateVoice(
            new[] { TestPlayScriptValue.Symbol("Damage") },
            TestPlayPresentationAdapterKind.None);
        Require(voice.type == TestPlayPresentationEventType.Voice && voice.symbol == "Damage",
            "Voice retains the original symbolic key", ref assertions);
        Require(voice.evidence == TestPlayPresentationEvidence.OriginalDataObserved,
            "Voice is not promoted beyond observed original data", ref assertions);

        TestPlayPresentationEvent camera = TestPlayPresentationCore.CreateCameraEffect(
            Values(2f), 2f, TestPlayPresentationAdapterKind.CameraShakeApproximation);
        Require(camera.originalId == 2 && Mathf.Approximately(camera.output, 2f),
            "CamEffect retains its original value", ref assertions);
        Require(camera.adapter == TestPlayPresentationAdapterKind.CameraShakeApproximation &&
                camera.diagnostic == "OriginalPerValueCameraFormulaUnknown",
            "camera shake is explicitly marked as a Unity approximation", ref assertions);
    }

    static void VerifyBurnerBoundaries(ref int assertions)
    {
        TestPlayPresentationEvent burner;
        Require(TestPlayPresentationCore.TryCreateBurner(
                Values(0f, 0.25f), TestPlayPresentationAdapterKind.BurnerCone, out burner),
            "BURNER accepts original ID zero", ref assertions);
        Require(burner.originalId == 0 && Mathf.Approximately(burner.output, 0.25f) &&
                burner.adapter == TestPlayPresentationAdapterKind.BurnerCone,
            "BURNER keeps ID, output, and adapter", ref assertions);
        Require(TestPlayPresentationCore.TryCreateBurner(
                Values(19f, 1f), TestPlayPresentationAdapterKind.ParticleSystem, out burner) &&
                burner.originalId == 19,
            "BURNER accepts original ID nineteen", ref assertions);
        Require(!TestPlayPresentationCore.TryCreateBurner(
                Values(-1f, 1f), TestPlayPresentationAdapterKind.None, out burner) &&
                burner.type == TestPlayPresentationEventType.Diagnostic,
            "BURNER rejects IDs below the original range", ref assertions);
        Require(burner.diagnostic == "BurnerIdOutsideOriginalRange",
            "invalid BURNER ID has a stable diagnostic", ref assertions);
        Require(!TestPlayPresentationCore.TryCreateBurner(
                Values(20f, 1f), TestPlayPresentationAdapterKind.None, out burner),
            "BURNER rejects IDs above the original range", ref assertions);
        Require(!TestPlayPresentationCore.TryCreateBurner(
                new List<TestPlayScriptValue>(), TestPlayPresentationAdapterKind.None, out burner) &&
                burner.diagnostic == "MissingBurnerId",
            "missing BURNER ID remains traceable", ref assertions);
        Require(TestPlayPresentationCore.TryCreateBurner(
                Values(3f), TestPlayPresentationAdapterKind.None, out burner) &&
                Mathf.Approximately(burner.output, 1f),
            "one-argument MOD compatibility keeps output one", ref assertions);
        Require(burner.diagnostic == "UnityCompatibilityDefaultOutput",
            "one-argument BURNER fallback is not labelled original", ref assertions);

        TestPlayPresentationEvent burner2 = TestPlayPresentationCore.CreateUnsupported(
            "BURNER2", Values(1f), "OriginalParserRejectsCommandObject");
        Require(burner2.type == TestPlayPresentationEventType.Diagnostic &&
                burner2.adapter == TestPlayPresentationAdapterKind.None,
            "BURNER2 stays unsupported instead of gaining an effect", ref assertions);
    }

    static void VerifyProcAndVisualEvidence(ref int assertions)
    {
        TestPlayPresentationEvent sword = TestPlayPresentationCore.CreateProc(
            true,
            Values(1f, 55f, 1f, 200f, 12f, 13f, 0f, 0f, 0f, 0f, 0f, 35f),
            TestPlayPresentationAdapterKind.None);
        Require(sword.command == "RunProc2" && sword.originalId == 1 && sword.procType == 55,
            "RunProc2 keeps order and proc type", ref assertions);
        Require(sword.subtype == -1 && sword.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            "type 55 is classified from the executable BB_SwordBeam handler", ref assertions);

        Require(TestPlayPresentationCore.TryCreateOriginalSwordBeamParameters(
                true,
                sword.arguments,
                out TestPlaySwordBeamParameters swordParameters) &&
                swordParameters.weaponPointId == 1 &&
                Mathf.Approximately(swordParameters.targetLength, 2f) &&
                swordParameters.primaryTextureId == 12 && swordParameters.lineTextureId == 13 &&
                Mathf.Approximately(swordParameters.initialLength, 0f) &&
                !swordParameters.replaceManagedBeam && swordParameters.lifetimeTicks == 35,
            "type 55 preserves the confirmed WEAPONPOINT, two textures, lengths, slot flag, and lifetime",
            ref assertions);

        float swordLength = swordParameters.initialLength;
        int swordLifetime = swordParameters.lifetimeTicks;
        Require(TestPlayPresentationCore.AdvanceOriginalSwordBeam(
                    ref swordLength, swordParameters.targetLength, ref swordLifetime) &&
                Mathf.Approximately(swordLength, 0.2f) && swordLifetime == 34,
            "BB_SwordBeam grows by 0.2 and decrements its finite lifetime once per original tick",
            ref assertions);

        TestPlayPresentationCore.TryCreateOriginalSwordBeamParameters(
            true,
            Values(1f, 55f, 1f, 200f, -1f, 30f, 0f, 0f, 0f, 0f, 0f, 0f),
            out TestPlaySwordBeamParameters persistentSword);
        float persistentLength = persistentSword.initialLength;
        int persistentLifetime = persistentSword.lifetimeTicks;
        Require(TestPlayPresentationCore.AdvanceOriginalSwordBeam(
                    ref persistentLength, persistentSword.targetLength, ref persistentLifetime) &&
                persistentLifetime == TestPlayPresentationCore.OriginalSwordBeamInfiniteLifetime,
            "a zero type 55 lifetime uses the original 999999999 non-decrementing sentinel",
            ref assertions);

        TestPlayPresentationEvent melee = TestPlayPresentationCore.CreateProc(
            true, Values(1f, 57f, 1f), TestPlayPresentationAdapterKind.CombatOnly);
        Require(melee.procType == 57 && melee.adapter == TestPlayPresentationAdapterKind.CombatOnly,
            "type 57 remains on the Combat Core boundary", ref assertions);

        TestPlayPresentationEvent special = TestPlayPresentationCore.CreateProc(
            true, Values(0f, 62f, 28f, 7f), TestPlayPresentationAdapterKind.None);
        Require(special.procType == 62 && special.subtype == 7,
            "type 62 retains its subtype", ref assertions);
        Require(special.evidence == TestPlayPresentationEvidence.OriginalExecutableConfirmed,
            "type 62 subtype 0..11 dispatch is classified from FUN_004fa830 without inferring argument meanings",
            ref assertions);
        TestPlayPresentationEvent unknownSpecial = TestPlayPresentationCore.CreateProc(
            true, Values(0f, 62f, 28f, 12f), TestPlayPresentationAdapterKind.None);
        Require(unknownSpecial.evidence == TestPlayPresentationEvidence.IncompleteInference,
            "type 62 values outside the confirmed subtype table remain inferred", ref assertions);

        Require(TestPlayPresentationCore.TryCreateOriginalHinokoParameters(
                true,
                Values(0f, 62f, 20f, 6f, 5f, 300f, 15f, 15f, 15f, 0f, 0f, 0f),
                out TestPlayHinokoParameters hinoko) &&
                hinoko.weaponPointId == 20 && Mathf.Approximately(hinoko.size, 0.05f) &&
                Mathf.Approximately(hinoko.signedForwardInput, 0.03f) &&
                Mathf.Approximately(hinoko.scatterX, 0.15f) &&
                Mathf.Approximately(hinoko.scatterY, 0.15f) &&
                Mathf.Approximately(hinoko.scatterZ, 0.15f) &&
                hinoko.unusedP9 == 0 && hinoko.unusedP10 == 0 && hinoko.unusedP11 == 0,
            "type 62 subtype 6 maps the real TR-1 BB_Hinoko size, signed direction, scatter, and unread tail",
            ref assertions);
        Require(TestPlayPresentationCore.TryCreateOriginalHinokoParameters(
                true,
                Values(0f, 62f, 13f, 6f, 5f, -300f, 15f, 15f, 15f, 0f, 0f, 0f),
                out TestPlayHinokoParameters reverseHinoko) &&
                reverseHinoko.weaponPointId == 13 &&
                Mathf.Approximately(reverseHinoko.signedForwardInput, -0.03f),
            "type 62 subtype 6 preserves the real ELS_QT negative p5 direction",
            ref assertions);
        Require(!TestPlayPresentationCore.TryCreateOriginalHinokoParameters(
                false,
                Values(0f, 62f, 20f, 6f, 5f, 300f, 15f, 15f, 15f, 0f, 0f, 0f),
                out hinoko) &&
                !TestPlayPresentationCore.TryCreateOriginalHinokoParameters(
                    true,
                    Values(0f, 62f, 20f, 6f),
                    out hinoko),
            "subtype 6 requires RunProc2 and the complete original argument record", ref assertions);

        TestPlayHinokoState hinokoState = TestPlayPresentationCore.CreateOriginalHinokoState();
        bool hinokoAlive = true;
        for (int i = 0; i < 30; i++)
            hinokoAlive &= TestPlayPresentationCore.AdvanceOriginalHinoko(ref hinokoState);
        Require(hinokoAlive && hinokoState.elapsedTicks == 30 && hinokoState.alphaByte == 255 &&
                Mathf.Approximately(hinokoState.accumulatedDrawDegrees, 150f),
            "BB_Hinoko keeps alpha 255 through update 30 while accumulating five draw degrees per update",
            ref assertions);
        Require(TestPlayPresentationCore.AdvanceOriginalHinoko(ref hinokoState) &&
                hinokoState.elapsedTicks == 31 && hinokoState.alphaByte == 251 &&
                Mathf.Approximately(hinokoState.accumulatedDrawDegrees, 155f),
            "BB_Hinoko starts its four-alpha fade on update 31", ref assertions);
        for (int tick = 32; tick <= 93; tick++)
            hinokoAlive &= TestPlayPresentationCore.AdvanceOriginalHinoko(ref hinokoState);
        Require(hinokoAlive && hinokoState.elapsedTicks == 93 && hinokoState.alphaByte == 3,
            "BB_Hinoko remains alive with alpha three after update 93", ref assertions);
        Require(!TestPlayPresentationCore.AdvanceOriginalHinoko(ref hinokoState) &&
                hinokoState.elapsedTicks == 94 && hinokoState.alphaByte == 0,
            "BB_Hinoko clamps alpha to zero and expires on update 94", ref assertions);

        Require(TestPlayPresentationCore.TryCreateOriginalMagicShieldParameters(
                true,
                Values(0f, 62f, 25f, 8f, 1f, 0f, 10f, 5f, 2f, 0f, 2f, 0f),
                out TestPlayMagicShieldParameters magicShield) &&
                magicShield.weaponPointId == 25 && magicShield.modelSlotIndex == 1 &&
                magicShield.releaseGateValue == 0 && magicShield.activeTicks == 10 &&
                magicShield.followWeaponPoint && magicShield.unusedP7 == 5 &&
                magicShield.unusedP8 == 2 && magicShield.unusedP9 == 0 &&
                magicShield.unusedP10 == 2 && magicShield.unusedP11 == 0,
            "type 62 subtype 8 preserves its confirmed WEAPONPOINT, model slot, gate/countdown, and unread tail",
            ref assertions);
        Require(!TestPlayPresentationCore.TryCreateOriginalMagicShieldParameters(
                false,
                Values(0f, 62f, 25f, 8f, 1f, 0f, 10f, 5f, 2f, 0f, 2f, 0f),
                out magicShield) &&
                !TestPlayPresentationCore.TryCreateOriginalMagicShieldParameters(
                    true,
                    Values(0f, 62f, 25f, 8f),
                    out magicShield),
            "subtype 8 requires RunProc2 and the complete original argument record", ref assertions);

        TestPlayPresentationCore.TryCreateOriginalMagicShieldParameters(
            true,
            Values(0f, 62f, 25f, 8f, 1f, 0f, 10f, 5f, 2f, 0f, 2f, 0f),
            out magicShield);
        TestPlayMagicShieldState magicShieldState =
            TestPlayPresentationCore.CreateOriginalMagicShieldState(magicShield);
        Require(magicShieldState.phase == TestPlayMagicShieldPhase.Grow &&
                Mathf.Approximately(magicShieldState.scale, 0.1f) &&
                Mathf.Approximately(magicShieldState.opacity, 0f) &&
                magicShieldState.remainingActiveTicks == 10,
            "LZ_MagicShieldEffect starts at scale 0.1, opacity zero, and the raw p6 countdown",
            ref assertions);
        for (int i = 0; i < 8; i++)
            Require(TestPlayPresentationCore.AdvanceOriginalMagicShield(
                magicShield, ref magicShieldState), "magic shield grow tick " + i, ref assertions);
        Require(magicShieldState.phase == TestPlayMagicShieldPhase.Active &&
                Mathf.Approximately(magicShieldState.scale, 0.9f) &&
                Mathf.Approximately(magicShieldState.opacity, 0.8f),
            "LZ_MagicShieldEffect switches to active at the original 0.9 scale boundary",
            ref assertions);
        Require(TestPlayPresentationCore.AdvanceOriginalMagicShield(
                    magicShield, ref magicShieldState) &&
                magicShieldState.phase == TestPlayMagicShieldPhase.Fade &&
                magicShieldState.remainingActiveTicks == 9 &&
                Mathf.Approximately(magicShieldState.opacity, 0.9f),
            "a non-positive p5 gate decrements p6 once and enters fade on the first active update",
            ref assertions);
        int fadeTicks = 0;
        while (TestPlayPresentationCore.AdvanceOriginalMagicShield(
            magicShield, ref magicShieldState))
            fadeTicks++;
        Require(fadeTicks == 8 && magicShieldState.phase == TestPlayMagicShieldPhase.Expired &&
                magicShieldState.opacity < 0.0001f &&
                magicShieldState.scale > 0.9f,
            "LZ_MagicShieldEffect fades by 0.1 and expands by 0.05 until its removal callback",
            ref assertions);

        TestPlayPresentationEvent texture = TestPlayPresentationCore.CreateTexture(
            "RunProc2:55", 13, "line.png", TestPlayPresentationAdapterKind.OriginalTextureQuad);
        Require(texture.type == TestPlayPresentationEventType.Texture && texture.textureId == 13,
            "texture event retains the script texture ID", ref assertions);
        Require(texture.resourceName == "line.png" &&
                texture.adapter == TestPlayPresentationAdapterKind.OriginalTextureQuad,
            "resource name and Unity quad adapter are separated", ref assertions);

        TestPlayPresentationEvent fallback = TestPlayPresentationCore.CreateVisual(
            "RunProc2:1", 12, TestPlayPresentationAdapterKind.PrimitiveFallback);
        Require(fallback.type == TestPlayPresentationEventType.Visual && fallback.textureId == 12,
            "visual fallback does not discard the requested texture ID", ref assertions);
        Require(fallback.adapter == TestPlayPresentationAdapterKind.PrimitiveFallback,
            "primitive fallback is explicit", ref assertions);
    }

    static void VerifyPresentationTrace(ref int assertions)
    {
        TestPlayPresentationEvent sound = TestPlayPresentationCore.CreateSound(
            new[] { TestPlayScriptValue.Symbol("quote\"line\n") },
            TestPlayPresentationAdapterKind.None);
        sound.tick = 12;
        sound.actionIndex = 100;
        sound.scriptIndex = 2;
        TestPlayPresentationEvent proc = TestPlayPresentationCore.CreateProc(
            true, Values(0f, 62f, 28f, 3f), TestPlayPresentationAdapterKind.None);
        proc.tick = 12;

        TestPlayPresentationSnapshot snapshot = new TestPlayPresentationSnapshot
        {
            events = new[] { sound, proc }
        };
        string phase3 = "{\"tick\":12,\"combat\":{}}";
        string first = TestPlayPhase4TickTrace.Serialize(phase3, snapshot);
        string second = TestPlayPhase4TickTrace.Serialize(phase3, snapshot);
        Require(first == second, "Phase 4 trace serialization is deterministic", ref assertions);
        Require(first.Contains("\"presentation\":{\"events\":["),
            "presentation extends the Phase 3 tick record", ref assertions);
        Require(first.Contains("\"action\":100") && first.Contains("\"script\":2"),
            "trace records action and script context", ref assertions);
        Require(first.Contains("quote\\\"line\\n"),
            "trace escapes symbolic arguments", ref assertions);
        Require(first.Contains("\"kind\":\"Symbol\"") && first.Contains("\"procType\":62"),
            "trace preserves argument kinds and proc identifiers", ref assertions);
        Require(first.Contains("\"evidence\":\"OriginalExecutableConfirmed\"") &&
                first.Contains("\"adapter\":\"None\""),
            "trace carries evidence and adapter independently", ref assertions);
    }

    static void VerifyControllerAdapter(ref int assertions)
    {
        GameObject go = new GameObject("TestPlayPhase4Verification_Controller");
        AudioClip clip = null;
        AudioClip propulsionStartClip = null;
        AudioClip propulsionLoopClip = null;
        try
        {
            TestPlayController controller = go.AddComponent<TestPlayController>();
            controller.logUnhandledCommands = false;
            TestPlayPresentationRuntime presentation = go.AddComponent<TestPlayPresentationRuntime>();
            presentation.enabled = false;
            clip = AudioClip.Create("Phase4Snd", 64, 1, 8000, false);
            propulsionStartClip = AudioClip.Create("Phase4PropulsionStart", 64, 1, 8000, false);
            propulsionLoopClip = AudioClip.Create("Phase4PropulsionLoop", 64, 1, 8000, false);
            presentation.sounds.Add(new TestPlayAudioBinding { key = "9", clip = clip });
            presentation.propulsionStartClip = propulsionStartClip;
            presentation.propulsionLoopClip = propulsionLoopClip;
            controller.presentationRuntime = presentation;

            MethodInfo beginTrace = typeof(TestPlayController).GetMethod("BeginPresentationTraceTick", InstancePrivate);
            MethodInfo handle = typeof(TestPlayController).GetMethod("HandleCommand", InstancePrivate);
            MethodInfo applyBurners = typeof(TestPlayController).GetMethod("ApplyBurners", InstancePrivate);
            MethodInfo stopBurners = typeof(TestPlayController).GetMethod("StopAllBurnerEffects", InstancePrivate);
            Require(beginTrace != null && handle != null && applyBurners != null && stopBurners != null,
                "controller presentation adapter helpers are available", ref assertions);

            List<TestPlayPresentationEvent> raised = new List<TestPlayPresentationEvent>();
            int legacyEvents = 0;
            controller.PresentationEventRaised += value => raised.Add(value);
            controller.RuntimeEventRaised += value => legacyEvents++;
            controller.tick = 42;
            controller.currentAnimationIndex = 100;
            controller.scriptIndex = 3;
            beginTrace.Invoke(controller, null);

            InvokeCommand(handle, controller, "Snd", Values(9f));
            InvokeCommand(handle, controller, "Voice", new List<TestPlayScriptValue>
            {
                TestPlayScriptValue.Symbol("Damage")
            });
            InvokeCommand(handle, controller, "BURNER", Values(3f, 0.75f));
            applyBurners.Invoke(controller, null);
            Require(presentation.PropulsionActiveRequested && presentation.PropulsionLoopRequested &&
                    presentation.propulsionStartSource != null &&
                    presentation.propulsionStartSource.clip == propulsionStartClip &&
                    !presentation.propulsionStartSource.loop &&
                    presentation.propulsionSource != null &&
                    presentation.propulsionSource.clip == propulsionLoopClip &&
                    presentation.propulsionSource.loop,
                "aggregate positive BURNER output configures simultaneous start and loop propulsion audio",
                ref assertions);
            applyBurners.Invoke(controller, null);
            Require(presentation.PropulsionActivationCount == 1,
                "continuous positive BURNER output does not restart propulsion audio every tick",
                ref assertions);
            stopBurners.Invoke(controller, null);
            Require(!presentation.PropulsionActiveRequested && !presentation.PropulsionLoopRequested &&
                    presentation.propulsionSource != null && !presentation.propulsionSource.isPlaying,
                "clearing BURNER output stops only the loop adapter immediately outside Play Mode",
                ref assertions);
            InvokeCommand(handle, controller, "CamEffect", Values(2f));
            InvokeCommand(handle, controller, "RunProc2", Values(0f, 57f, 1f));
            InvokeCommand(handle, controller, "BURNER2", Values(1f));

            Require(raised.Count == 6, "all Phase 4 commands publish typed events", ref assertions);
            Require(raised[0].type == TestPlayPresentationEventType.Sound &&
                    raised[0].adapter == TestPlayPresentationAdapterKind.AudioClip,
                "controller resolves mapped audio before publishing", ref assertions);
            Require(raised[1].type == TestPlayPresentationEventType.Voice &&
                    raised[1].adapter == TestPlayPresentationAdapterKind.None,
                "missing voice mapping remains an event without a fake clip", ref assertions);
            Require(raised[2].type == TestPlayPresentationEventType.Burner &&
                    raised[2].adapter == TestPlayPresentationAdapterKind.None,
                "missing SPT burner mapping is separated from the original request", ref assertions);
            Require(raised[3].type == TestPlayPresentationEventType.CameraEffect &&
                    raised[3].adapter == TestPlayPresentationAdapterKind.None,
                "camera approximation is absent when no camera adapter is assigned", ref assertions);
            Require(raised[4].procType == 57 &&
                    raised[4].adapter == TestPlayPresentationAdapterKind.CombatOnly,
                "controller routes proc type 57 to combat", ref assertions);
            Require(raised[5].type == TestPlayPresentationEventType.Diagnostic &&
                    raised[5].command == "BURNER2",
                "controller traces rejected BURNER2", ref assertions);
            Require(raised[0].tick == 42 && raised[0].actionIndex == 100 && raised[0].scriptIndex == 3,
                "controller stamps presentation context", ref assertions);
            Require(legacyEvents == 11,
                "legacy command and specialized runtime events remain for external subscribers", ref assertions);

            string trace = controller.CapturePhase4TickTrace();
            Require(trace.Contains("\"presentation\":{") && trace.Contains("\"originalId\":9"),
                "controller exposes presentation events in Phase 4 trace", ref assertions);
            Require(trace.Contains("\"adapter\":\"AudioClip\"") &&
                    trace.Contains("\"adapter\":\"CombatOnly\""),
                "controller trace records selected adapter boundaries", ref assertions);
            Require(trace.Contains("OriginalParserRejectsCommandObject"),
                "controller trace retains unsupported-command diagnostics", ref assertions);
        }
        finally
        {
            if (clip != null)
                UnityEngine.Object.DestroyImmediate(clip);
            if (propulsionStartClip != null)
                UnityEngine.Object.DestroyImmediate(propulsionStartClip);
            if (propulsionLoopClip != null)
                UnityEngine.Object.DestroyImmediate(propulsionLoopClip);
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    static void InvokeCommand(
        MethodInfo handle,
        TestPlayController controller,
        string command,
        List<TestPlayScriptValue> arguments)
    {
        handle.Invoke(controller, new object[] { command, arguments, command + "(...);" });
    }

    static List<TestPlayScriptValue> Values(params float[] values)
    {
        List<TestPlayScriptValue> result = new List<TestPlayScriptValue>();
        for (int i = 0; i < values.Length; i++)
            result.Add(TestPlayScriptValue.Number(values[i]));
        return result;
    }

    static void Require(bool condition, string message, ref int assertions)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("[TestPlayPhase4Verification] " + message);
    }
}
