using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

public static class HodHierarchyPriorityVerification
{
    const string MenuPath = "Tools/WindomXP/HOD/Run Hierarchy Priority Verification";
    static int assertions;
    public static bool isRunning { get; private set; }
    public static string lastResult { get; private set; } = "未実行";

    [MenuItem(MenuPath)]
    public static void Run()
    {
        WindomVerificationRunner.StartVerification(WindomVerificationSelection.Hod);
    }

    internal static async System.Threading.Tasks.Task<int> RunForJobAsync()
    {
        if (isRunning)
            throw new InvalidOperationException("HOD verification is already running.");

        isRunning = true;
        lastResult = "実行中";
        assertions = 0;
        string temporaryPath = Path.Combine(
            Path.GetTempPath(),
            "WindomXP-HierarchyPriority-" + Guid.NewGuid().ToString("N") + ".an2");
        string legacyRoundTripPath = Path.Combine(
            Path.GetTempPath(),
            "WindomXP-ElsQtLegacy-" + Guid.NewGuid().ToString("N") + ".ani");
        string legacyAn2ExportPath = Path.Combine(
            Path.GetTempPath(),
            "WindomXP-ElsQtExport-" + Guid.NewGuid().ToString("N") + ".an2");
        string legacyIkEditPath = Path.Combine(
            Path.GetTempPath(),
            "WindomXP-LegacyIkEdit-" + Guid.NewGuid().ToString("N") + ".ani");

        try
        {
            assertions += await LegacyAniRotationVerification.RunForJobAsync();
            TestOriginalHierarchyPolicy();
            TestConsistentHierarchy();
            TestTreeDepthWinsAmbiguousHierarchy();
            TestChildCountFallback();
            TestLegacyFrameLabelsKeepIndexOrder();
            TestUniqueNamesRepairFrameOrder();
            TestUnresolvedFrameOrderIsAtomic();
            TestBothInvalidStopsLoading();
            TestFrameMismatchStopsWithoutFallback();
            TestNoTextDialogCancelChoice();
            await TestOriginalElsQtHierarchy(temporaryPath);
            await TestAn2RoundTrip(temporaryPath);
            await TestElsQtLegacyAniRoundTrip(legacyRoundTripPath, legacyAn2ExportPath);
            await TestLegacyIkDataStructuralEdits(legacyIkEditPath);
            await TestLegacyStructureEditPreparation(legacyIkEditPath);
            await TestLegacyPreloadAn2Conversion(legacyIkEditPath);
            await TestConversionFailureIsolation(legacyIkEditPath);
            await TestScriptBytePreservation();
            await TestAn2OriginalGameLimit();
            await TestConversionDialogChoices();
            await TestUnknownLegacyIkDataProtection(legacyIkEditPath);
            lastResult = $"{assertions} assertions passed.";
            Debug.Log($"[HodHierarchyPriorityVerification] {lastResult}");
            return assertions;
        }
        catch (Exception ex)
        {
            lastResult = "Failed: " + ex;
            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            if (File.Exists(legacyRoundTripPath))
                File.Delete(legacyRoundTripPath);
            if (File.Exists(legacyAn2ExportPath))
                File.Delete(legacyAn2ExportPath);
            if (File.Exists(legacyIkEditPath))
                File.Delete(legacyIkEditPath);
            isRunning = false;
        }
    }

    static void TestOriginalHierarchyPolicy()
    {
        ani2 source = BuildAni(new[] { 0, 1, 1, 1 }, new[] { 1, 1, 1, 0 }, 2);
        string before = ComputeHierarchySignature(source);
        int[] parents, depths;
        string error;
        Require(HodHierarchyRepair.TryGetDisplayHierarchy(source.structure.parts,
            out parents, out depths, out error), "original child hierarchy resolves");
        Require(parents[0] == -1 && parents[1] == 0 && parents[2] == 1 && parents[3] == 2,
            "childCount produces a chain despite sibling depth labels");
        Require(depths[3] == 3 && ComputeHierarchySignature(source) == before,
            "display hierarchy calculation preserves source columns");
        HodHierarchyRepairPlan plan;
        Require(UI_SelectMech.TryApplyAutomaticHierarchyRepairForLoad(source, out plan, out error),
            "normal load uses original hierarchy");
        RequireValues(source, new[] { 0, 1, 2, 3 }, new[] { 1, 1, 1, 0 });

        ani2 invalidCounts = BuildAni(new[] { 0, 1, 1 }, new[] { 0, 0, 0 }, 1);
        Require(HodHierarchyRepair.TryGetDisplayHierarchy(invalidCounts.structure.parts,
            out parents, out depths, out error) && parents[2] == 0,
            "invalid child counts retain depth-only compatibility fallback");
        Require(HodHierarchyRepair.TryApplyChildCountFirst(invalidCounts, out plan, out error),
            "invalid child counts normalize from valid depth");
        RequireValues(invalidCounts, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });

        ani2 invalidBoth = BuildAni(new[] { 0, 3, 1 }, new[] { 0, 0, 0 }, 1);
        Require(!HodHierarchyRepair.TryGetDisplayHierarchy(invalidBoth.structure.parts,
            out parents, out depths, out error), "unsafe display hierarchy is rejected");
        ani2 mismatch = BuildAni(new[] { 0, 1, 1 }, new[] { 1, 1, 0 }, 2);
        mismatch.animations[0].frames[1].parts.RemoveAt(2);
        before = ComputeHierarchySignature(mismatch);
        Require(!HodHierarchyRepair.TryApplyChildCountFirst(mismatch, out plan, out error),
            "frame mismatch cannot bypass childCount policy via fallback");
        Require(before == ComputeHierarchySignature(mismatch), "failed original repair is atomic");
    }

    static async System.Threading.Tasks.Task TestOriginalElsQtHierarchy(string exportPath)
    {
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
            "Windom_Data", "Robo", "ELS_QT", "Script.ani");
        string hash = ComputeSha256(path);
        var source = new ani2();
        Require(await source.load(path), "ELS_QT loads for original hierarchy verification");
        string before = ComputeHierarchySignature(source);
        int[] parents, depths;
        string error;
        Require(HodHierarchyRepair.TryGetDisplayHierarchy(source.structure.parts,
            out parents, out depths, out error), "ELS_QT childCount hierarchy is valid");
        int[] changedIndices = { 24, 27, 33, 36, 81, 82, 83, 84, 85, 86, 87, 88 };
        int[] expectedParents = { 21, 21, 30, 30, 80, 81, 82, 83, 84, 85, 86, 87 };
        for (int i = 0; i < changedIndices.Length; i++)
            Require(parents[changedIndices[i]] == expectedParents[i],
                "original ELS/goe parent " + changedIndices[i]);

        var owner = new GameObject("OriginalHierarchyVerification");
        var copyOwner = new GameObject("OriginalHierarchyPreviewVerification");
        var seedOwner = new GameObject("OriginalHierarchySeed");
        RoboStructure robo = owner.AddComponent<RoboStructure>();
        RoboStructure preview = copyOwner.AddComponent<RoboStructure>();
        try
        {
            var seed = seedOwner.AddComponent<RoboStructure>();
            robo.buildStructureFromLoaded(seed, source.structure);
            preview.buildStructureFromLoaded(robo, source.structure);
            Require(robo.parts[81].transform.parent == robo.parts[80].transform
                && preview.parts[82].transform.parent == preview.parts[81].transform,
                "main and preview transforms use original parent chain");
            // 構造のEls8はscale=0。falseのSetParentで子のローカルTRSを保つ。
            Require(robo.parts[82].transform.localScale == source.structure.parts[82].scale
                && !float.IsNaN(robo.parts[82].transform.localPosition.x),
                "zero-scale parent does not corrupt child local TRS");
            foreach (int action in new[] { 0, 50, 100, 105, 0, 136, 164 })
            {
                int frame = action == 105 ? 5 : 0;
                robo.setPose(source.animations[action].frames[frame]);
                preview.setPose(source.animations[action].frames[frame]);
                bool hidden = action != 105;
                foreach (int index in new[] { 81, 82 })
                {
                    float size = robo.parts[index].transform.localToWorldMatrix.MultiplyVector(Vector3.right).magnitude;
                    Require(hidden ? size == 0f : size > 0.1f,
                        "parent scale controls part " + index + " action " + action);
                    Require(robo.parts[index].transform.localScale == source.animations[action].frames[frame].parts[index].scale,
                        "local scale remains recorded value " + index + " action " + action);
                    Require(preview.parts[index].transform.localToWorldMatrix == robo.parts[index].transform.localToWorldMatrix,
                        "preview pose matches edit pose " + index + " action " + action);
                }
            }
            // ID105後半で親が0へ戻ると、ローカルsize=1.3の子も隠れる。
            robo.setPose(source.animations[105].frames[10]);
            Require(robo.parts[81].transform.localToWorldMatrix.MultiplyVector(Vector3.right).sqrMagnitude == 0,
                "action105 closing pose hides descendants through parent");
        }
        finally
        {
            if (robo.root != null) UnityEngine.Object.DestroyImmediate(robo.root);
            if (preview.root != null) UnityEngine.Object.DestroyImmediate(preview.root);
            UnityEngine.Object.DestroyImmediate(owner);
            UnityEngine.Object.DestroyImmediate(copyOwner);
            UnityEngine.Object.DestroyImmediate(seedOwner);
        }

        source.saveAsAn2(exportPath);
        Require(ComputeHierarchySignature(source) == before, "AN2 export does not mutate legacy source hierarchy");
        var reloaded = new ani2();
        Require(await reloaded.load(exportPath), "original hierarchy AN2 copy reloads");
        Require(HodHierarchyRepair.TryGetDisplayHierarchy(reloaded.structure.parts,
            out var exportedParents, out var exportedDepths, out error), "AN2 copy hierarchy resolves");
        for (int i = 0; i < parents.Length; i++)
            Require(exportedParents[i] == parents[i] && reloaded.structure.parts[i].treeDepth == depths[i],
                "AN2 export keeps original parent and normalizes depth " + i);
        Require(reloaded.animations[105].frames[5].parts[81].scale == source.animations[105].frames[5].parts[81].scale,
            "AN2 copy preserves action105 local scale");
        Require(ComputeHierarchySignature(source) == before && ComputeSha256(path) == hash,
            "original hierarchy verification preserves source data");

        LegacyAniStructureEditPlan editPlan;
        Require(LegacyAniStructureEditPlan.TryCreate(source, out editPlan, out error)
            && editPlan.RecommendedAuthority == LegacyAniHierarchyAuthority.ChildCount,
            "structural edits default to original hierarchy");
        Require(editPlan.TryApply(source, editPlan.RecommendedAuthority, out error),
            "original hierarchy normalization for edits succeeds");
        Require(source.TryRemovePart(81, out error), "original Els8 subtree can be removed atomically");
        Require(source.structure.parts.Count == 159 && source.animations[105].frames[0].parts.Count == 159
            && source.structure.parts[80].name == "Els9.x" && source.structure.parts[81].name == "Hand_r.x",
            "subtree deletion removes Els8 through Els and retains sibling hand");
    }

    static void TestConsistentHierarchy()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 }, 2);
        HodHierarchyRepairPlan plan;
        string error;
        Require(HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "consistent hierarchy loads");
        Require(plan != null && plan.Kind == HodHierarchyRepairKind.None, "consistent hierarchy needs no repair");
        Require(error == "", "consistent hierarchy has no error");
        RequireValues(ani, new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 });
    }

    static void TestTreeDepthWinsAmbiguousHierarchy()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 2, 1 }, new[] { 1, 2, 0, 0 }, 2);
        HodHierarchyRepairPlan plan;
        string error;
        Require(HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "ambiguous hierarchy loads");
        Require(plan.Kind == HodHierarchyRepairKind.RebuildChildCountsFromTreeDepth, "treeDepth wins an ambiguity");
        RequireValues(ani, new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 });
    }

    static void TestChildCountFallback()
    {
        ani2 ani = BuildAni(new[] { 0, 2, 1 }, new[] { 2, 0, 0 }, 2);
        HodHierarchyRepairPlan plan;
        string error;
        Require(HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "valid childCount repairs invalid treeDepth");
        Require(plan.Kind == HodHierarchyRepairKind.RebuildTreeDepthFromChildCounts, "childCount is the fallback authority");
        RequireValues(ani, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });
    }

    static void TestNoTextDialogCancelChoice()
    {
        GameObject dialogObject = new GameObject(
            "LegacyAniPreloadChoiceDialogVerification",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(UnityEngine.UI.Text));
        try
        {
            UI_InputBox dialog = dialogObject.AddComponent<UI_InputBox>();
            dialog.text = dialogObject.GetComponent<UnityEngine.UI.Text>();
            int selectedChoice = 0;
            dialogObject.SetActive(false);
            dialog.openNoTextBoxDialog(
                "legacy ANI conversion choice",
                ignored => selectedChoice = 1,
                ignored => selectedChoice = 2);
            Require(dialogObject.activeSelf,
                "legacy conversion confirmation opens its dialog");
            dialog.cancel();
            Require(selectedChoice == 2,
                "legacy conversion confirmation routes cancel to keep-legacy choice");
            Require(!dialogObject.activeSelf,
                "legacy conversion confirmation closes before the keep-legacy callback returns");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(dialogObject);
        }
    }

    static void TestLegacyFrameLabelsKeepIndexOrder()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 2, 1 }, new[] { 1, 2, 0, 0 }, 1);
        hod2v1 frame = ani.animations[0].frames[0];
        string[] legacyNames = { "Pure", "Tele", "Tele", "Body" };
        for (int i = 0; i < frame.parts.Count; i++)
        {
            hod2v1_Part part = frame.parts[i];
            part.name = legacyNames[i];
            part.position = new Vector3(10 + i, 20 + i, 30 + i);
            frame.parts[i] = part;
        }

        HodHierarchyRepairPlan plan;
        string error;
        Require(HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "legacy labels load by matching hierarchy order");
        Require(plan.Kind == HodHierarchyRepairKind.RebuildChildCountsFromTreeDepth, "legacy labels still use treeDepth priority");
        for (int i = 0; i < frame.parts.Count; i++)
        {
            Require(frame.parts[i].name == legacyNames[i], "legacy frame name is preserved " + i);
            Require(frame.parts[i].position == new Vector3(10 + i, 20 + i, 30 + i), "legacy frame transform stays at its index " + i);
        }
        RequireValues(ani, new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 });
    }

    static void TestUniqueNamesRepairFrameOrder()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 }, 1);
        hod2v1 frame = ani.animations[0].frames[0];
        for (int i = 0; i < frame.parts.Count; i++)
        {
            hod2v1_Part part = frame.parts[i];
            part.position = new Vector3(100 + i, 0, 0);
            part.unk1 = new Quaternion(i, i + 1, i + 2, i + 3);
            if (i == 2)
                part.childCount = 99;
            frame.parts[i] = part;
        }

        List<hod2v1_Part> source = new List<hod2v1_Part>(frame.parts);
        frame.parts = new List<hod2v1_Part>
        {
            source[2], source[0], source[3], source[1]
        };

        HodHierarchyRepairPlan plan;
        string error;
        Require(HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "unique names repair a genuine frame permutation");
        Require(plan.Kind == HodHierarchyRepairKind.None, "frame-only order repair does not change a valid hierarchy plan");
        for (int i = 0; i < frame.parts.Count; i++)
        {
            Require(frame.parts[i].name == "Part" + i, "frame name is reordered " + i);
            Require(frame.parts[i].position == new Vector3(100 + i, 0, 0), "frame transform follows its named part " + i);
            Require(frame.parts[i].unk1 == new Quaternion(i, i + 1, i + 2, i + 3), "frame unknown rotation follows its named part " + i);
        }
        RequireValues(ani, new[] { 0, 1, 2, 1 }, new[] { 2, 1, 0, 0 });
    }

    static void TestUnresolvedFrameOrderIsAtomic()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 2, 1 }, new[] { 1, 2, 0, 0 }, 2);
        hod2v1 firstFrame = ani.animations[0].frames[0];
        hod2v1 secondFrame = ani.animations[0].frames[1];
        firstFrame.parts = Permute(firstFrame.parts, 2, 0, 3, 1);
        secondFrame.parts = Permute(secondFrame.parts, 2, 0, 3, 1);

        hod2v1_Part duplicate = secondFrame.parts[0];
        duplicate.name = "Duplicate";
        secondFrame.parts[0] = duplicate;
        duplicate = secondFrame.parts[1];
        duplicate.name = "Duplicate";
        secondFrame.parts[1] = duplicate;

        string firstFrameNameBefore = firstFrame.parts[0].name;
        int structureChildCountBefore = ani.structure.parts[0].childCount;
        HodHierarchyRepairPlan plan;
        string error;
        Require(!HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "duplicate names with a different hierarchy order fail");
        Require(!string.IsNullOrEmpty(error), "unresolved frame order reports a reason");
        Require(firstFrame.parts[0].name == firstFrameNameBefore, "earlier reorderable frame is not mutated on a later failure");
        Require(ani.structure.parts[0].childCount == structureChildCountBefore, "unresolved frame order does not mutate the structure");
    }

    static void TestBothInvalidStopsLoading()
    {
        ani2 ani = BuildAni(new[] { 1, 3 }, new[] { 0, 0 }, 1);
        int originalDepth = ani.structure.parts[1].treeDepth;
        HodHierarchyRepairPlan plan;
        string error;
        Require(!HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "both invalid representations fail");
        Require(!string.IsNullOrEmpty(error), "both invalid representations report a reason");
        Require(ani.structure.parts[1].treeDepth == originalDepth, "failed repair does not mutate the structure");
    }

    static void TestFrameMismatchStopsWithoutFallback()
    {
        ani2 ani = BuildAni(new[] { 0, 1, 1 }, new[] { 0, 0, 0 }, 1);
        ani.animations[0].frames[0].parts.RemoveAt(2);
        HodHierarchyRepairPlan plan;
        string error;
        Require(!HodHierarchyRepair.TryApplyTreeDepthFirst(ani, out plan, out error), "frame mismatch fails");
        Require(!string.IsNullOrEmpty(error), "frame mismatch reports a reason");
        Require(ani.structure.parts[0].childCount == 0, "failed frame synchronization is non-mutating");
    }

    static async System.Threading.Tasks.Task TestAn2RoundTrip(string temporaryPath)
    {
        ani2 source = BuildAni(new[] { 0, 2, 1 }, new[] { 2, 0, 0 }, 1);
        source.save(temporaryPath);

        ani2 loaded = new ani2();
        Require(await loaded.load(temporaryPath), "saved AN2 reloads through ani2.load");
        Require(loaded.sourceFormat == AniContainerFormat.An2, "saved AN2 remembers its source format");
        HodHierarchyRepairPlan plan;
        string error;
        Require(UI_SelectMech.TryApplyAutomaticHierarchyRepairForLoad(
            loaded, out plan, out error), "AN2 automatic hierarchy repair succeeds");
        Require(plan.Kind == HodHierarchyRepairKind.None,
            "AN2 export already persisted the childCount hierarchy");
        RequireValues(loaded, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });

        string validation;
        Require(HodHierarchyValidator.TryValidate(loaded.structure.parts, out validation), "reloaded hierarchy validates");
    }

    static async System.Threading.Tasks.Task TestElsQtLegacyAniRoundTrip(
        string temporaryPath,
        string an2ExportPath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string sourcePath = Path.Combine(
            projectRoot, "Windom_Data", "Robo", "ELS_QT", "script.ani");
        Require(File.Exists(sourcePath), "ELS_QT legacy ANI fixture exists");
        string sourceHashBefore = ComputeSha256(sourcePath);

        ani2 source = new ani2();
        Require(await source.load(sourcePath), "ELS_QT legacy ANI loads through ani2.load");
        Require(source.sourceFormat == AniContainerFormat.LegacyAni, "ELS_QT source format is legacy ANI");
        Require(source.structure.parts.Count == 167, "ELS_QT structure has 167 parts");
        Require(source.animations.Count == 219, "ELS_QT exposes 200 base and 19 extension animation slots");
        Require(source.legacyAnimationSlotCount == 219, "ELS_QT remembers all legacy animation slots");
        Require(CountFrames(source) == 385, "ELS_QT includes extension animation frames");
        Require(source.hasLegacyTailData, "ELS_QT preserves trailing data");
        Require(source.hasLegacyIkData, "ELS_QT identifies the IKDATA tail");
        Require(source.legacyIkDataSupportsPartEdits, "ELS_QT IKDATA is recognized as part-indexed flag/unk data");
        Require(source.legacyIkRecordCount == 91, "ELS_QT IKDATA has 91 explicit part records");
        Require(source.structure.parts[1].flag == 1
            && source.structure.parts[1].unk == Vector3.zero,
            "ELS_QT IKDATA applies Gate.x flag/unk to structure part 1");
        Require(source.structure.parts[8].unk == new Vector3(0, 1, 1),
            "ELS_QT IKDATA applies Body_d.x flag/unk to structure part 8");
        Require(source.structure.parts[91].flag == 1
            && source.structure.parts[91].unk == Vector3.one,
            "ELS_QT parts after the explicit IKDATA prefix keep legacy defaults");
        Require(source.animations[200].name == "掴み２", "ELS_QT extension animation 200 is loaded");

        hod2v1_Part firstPartBefore = source.animations[0].frames[0].parts[0];
        Require(firstPartBefore.name == "Pure", "ELS_QT preserves its legacy frame label before load policy");
        string hierarchyBeforeLoadPolicy = ComputeHierarchySignature(source);

        HodHierarchyRepairPlan plan;
        string error;
        Require(UI_SelectMech.TryApplyAutomaticHierarchyRepairForLoad(
            source, out plan, out error), "ELS_QT legacy load policy succeeds without repair");
        Require(plan == null, "ELS_QT legacy load does not create an automatic repair plan");
        Require(error == "", "ELS_QT legacy load bypass has no fatal error");
        Require(ComputeHierarchySignature(source) == hierarchyBeforeLoadPolicy,
            "ELS_QT legacy load leaves every hierarchy column and part order unchanged");
        string validation;
        Require(!HodHierarchyRepair.TryValidateWithoutRepair(source, out validation),
            "ELS_QT keeps its original hierarchy differences for edit blocking");
        Require(!string.IsNullOrEmpty(validation),
            "ELS_QT unrepaired hierarchy reports a structural editing warning");
        LegacyAniStructureEditPlan legacyEditPlan;
        string legacyEditError;
        Require(LegacyAniStructureEditPlan.TryCreate(
                source, out legacyEditPlan, out legacyEditError),
            "ELS_QT creates a safe legacy structure-edit preparation plan");
        Require(!legacyEditPlan.RequiresAuthorityChoice
                && legacyEditPlan.RecommendedAuthority == LegacyAniHierarchyAuthority.ChildCount,
            "ELS_QT defaults to the original childCount authority");
        Require(!string.IsNullOrEmpty(legacyEditPlan.BuildPreview(
                LegacyAniHierarchyAuthority.TreeDepth, source.structure.parts))
            && !string.IsNullOrEmpty(legacyEditPlan.BuildPreview(
                LegacyAniHierarchyAuthority.ChildCount, source.structure.parts)),
            "ELS_QT presents both hierarchy normalization previews");
        Require(ComputeHierarchySignature(source) == hierarchyBeforeLoadPolicy,
            "ELS_QT edit-plan creation remains non-mutating");
        GameObject editGuardObject = new GameObject("LegacyAniHierarchyEditGuardVerification");
        try
        {
            RoboStructure editGuard = editGuardObject.AddComponent<RoboStructure>();
            editGuard.hod = source.structure;
            editGuard.SetStructureEditingBlockReason(validation);
            string editWarning;
            Require(!editGuard.CanEditStructure(out editWarning),
                "ELS_QT unrepaired hierarchy blocks structural editing");
            Require(editWarning == validation,
                "ELS_QT structural editing block preserves the validation reason");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(editGuardObject);
        }
        hod2v1_Part firstPartAfter = source.animations[0].frames[0].parts[0];
        Require(firstPartAfter.name == firstPartBefore.name, "ELS_QT legacy frame label is not reordered");
        Require(firstPartAfter.position == firstPartBefore.position, "ELS_QT frame position is not reassigned");
        Require(firstPartAfter.rotation == firstPartBefore.rotation, "ELS_QT frame rotation is not reassigned");
        Require(firstPartAfter.scale == firstPartBefore.scale, "ELS_QT frame scale is not reassigned");

        string saveValidation;
        Require(source.canSaveAsLegacyAni(out saveValidation), "ELS_QT unmodified hierarchy can be saved as legacy ANI");
        Require(saveValidation == "", "ELS_QT legacy save validation has no error");
        source.save(temporaryPath);
        Require(ReadSignature(temporaryPath, 3) == "ANI", "source-preserving save writes the ANI signature");
        Require(File.ReadAllBytes(sourcePath).Length == File.ReadAllBytes(temporaryPath).Length, "legacy save keeps the container length");
        Require(ComputeSha256(temporaryPath) == sourceHashBefore,
            "unmodified legacy save is byte exact including the original hierarchy");
        RequireLegacyMatricesEqual(sourcePath, temporaryPath, 219, "unchanged legacy matrices remain byte exact");
        RequireTailEqual(sourcePath, temporaryPath, "IKDATA tail remains byte exact");

        ani2 reloaded = new ani2();
        Require(await reloaded.load(temporaryPath), "ELS_QT unchanged legacy ANI reloads through ani2.load");
        Require(reloaded.sourceFormat == AniContainerFormat.LegacyAni, "round-trip format remains legacy ANI");
        Require(reloaded.structure.parts.Count == 167, "ELS_QT round-trip structure part count");
        Require(reloaded.animations.Count == 219, "ELS_QT round-trip animation count");
        Require(CountFrames(reloaded) == 385, "ELS_QT round-trip frame count");
        Require(reloaded.hasLegacyIkData, "ELS_QT round-trip preserves IKDATA");
        string reloadedHierarchyBeforePolicy = ComputeHierarchySignature(reloaded);
        Require(UI_SelectMech.TryApplyAutomaticHierarchyRepairForLoad(
            reloaded, out plan, out error), "round-trip legacy load policy succeeds without repair");
        Require(plan == null, "round-trip legacy load still skips automatic repair");
        Require(ComputeHierarchySignature(reloaded) == reloadedHierarchyBeforePolicy,
            "round-trip legacy load still preserves all hierarchy data");

        animation editedAnimation = reloaded.animations[0];
        editedAnimation.name += "_編集";
        Require(editedAnimation.frames.Count > 0, "ELS_QT edit target has a frame");
        hod2v1_Part editedPart = editedAnimation.frames[0].parts[0];
        editedPart.position += new Vector3(0.25f, -0.5f, 0.75f);
        editedPart.rotation = Quaternion.Euler(5f, 10f, 15f);
        // Leave the legacy-unrepresentable constraint values unchanged. The edited
        // rotation itself must still be written to the legacy HOD matrix.
        editedAnimation.frames[0].parts[0] = editedPart;
        Require(reloaded.canSaveAsLegacyAni(out validation),
            "legacy rotation edit remains saveable when rotation constraints are not representable");
        Require(validation == "", "legacy rotation-only save has no validation error");
        string editedScriptText = null;
        if (editedAnimation.scripts.Count > 0)
        {
            script editedScript = editedAnimation.scripts[0];
            editedScript.squirrel = (editedScript.squirrel ?? "") + "\r\n' legacy-save verification";
            editedAnimation.scripts[0] = editedScript;
            editedScriptText = editedScript.squirrel;
        }

        reloaded.save(temporaryPath);
        ani2 editedReload = new ani2();
        Require(await editedReload.load(temporaryPath), "edited legacy ANI reloads");
        Require(editedReload.animations[0].name == editedAnimation.name, "legacy animation name edit persists");
        Require(editedReload.animations[0].frames[0].parts[0].position == editedPart.position, "legacy position edit persists");
        Require(Quaternion.Angle(
            editedReload.animations[0].frames[0].parts[0].rotation,
            editedPart.rotation) < 0.01f, "legacy rotation edit persists");
        if (editedScriptText != null)
            Require(editedReload.animations[0].scripts[0].squirrel == editedScriptText, "legacy script edit persists");
        RequireTailEqual(sourcePath, temporaryPath, "edited legacy save still preserves IKDATA");

        editedReload.animations[0].squirrelInit = "unsupported";
        Require(!editedReload.canSaveAsLegacyAni(out validation), "legacy initial script is rejected");
        Require(validation.Contains("初期スクリプト"), "legacy initial script rejection explains the field");
        editedReload.animations[0].squirrelInit = "";

        hod2v0_Part renamedStructurePart = editedReload.structure.parts[0];
        string originalStructureName = renamedStructurePart.name;
        string originalFrameLabel = editedReload.animations[0].frames[0].parts[0].name;
        renamedStructurePart.name = originalStructureName + "_renamed";
        editedReload.structure.parts[0] = renamedStructurePart;
        Require(editedReload.canSaveAsLegacyAni(out validation),
            "recognized IKDATA permits a legacy structure part rename");
        editedReload.save(temporaryPath);
        ani2 renamedReload = new ani2();
        Require(await renamedReload.load(temporaryPath), "renamed legacy ANI reloads");
        Require(renamedReload.structure.parts[0].name == renamedStructurePart.name,
            "legacy structure part rename persists");
        Require(renamedReload.animations[0].frames[0].parts[0].name == originalFrameLabel,
            "legacy frame labels remain independent from structure part names");
        RequireTailEqual(sourcePath, temporaryPath,
            "name-only structure edit keeps equivalent IKDATA byte exact");

        LegacyAniAn2ConversionResult conversion =
            await UI_SelectMech.ConvertLegacyAniToAn2Async(renamedReload, an2ExportPath);
        Require(conversion.success, "explicit ELS_QT AN2 conversion succeeds");
        Require(conversion.hierarchyPlan != null
            && conversion.hierarchyPlan.Kind == HodHierarchyRepairKind.RebuildTreeDepthFromChildCounts,
            "explicit ELS_QT AN2 conversion preserves the original childCount hierarchy");
        Require(ReadSignature(an2ExportPath, 3) == "AN2", "explicit AN2 export writes the AN2 signature");
        ani2 an2Reload = conversion.ani;
        Require(an2Reload != null, "explicit AN2 conversion returns its public reload");
        Require(an2Reload.sourceFormat == AniContainerFormat.An2, "explicit export reloads as AN2");
        Require(an2Reload.animations.Count == 219, "explicit AN2 export keeps all animation slots");
        Require(UI_SelectMech.TryApplyAutomaticHierarchyRepairForLoad(
            an2Reload, out plan, out error), "explicit AN2 export keeps automatic hierarchy repair");
        Require(plan != null && plan.Kind == HodHierarchyRepairKind.None,
            "explicit AN2 conversion persists the normalized hierarchy before reload");
        Require(HodHierarchyValidator.TryValidate(an2Reload.structure.parts, out validation),
            "explicit AN2 export hierarchy validates after automatic repair");

        ani2 normalizedLegacy = new ani2();
        Require(await normalizedLegacy.load(sourcePath),
            "ELS_QT reloads for explicit legacy structure-edit normalization");
        Require(LegacyAniStructureEditPlan.TryCreate(
                normalizedLegacy, out legacyEditPlan, out legacyEditError),
            "ELS_QT recreates its selectable legacy edit plan");
        string normalizedFrameLabel = normalizedLegacy.animations[0].frames[0].parts[0].name;
        Vector3 normalizedFramePosition = normalizedLegacy.animations[0].frames[0].parts[0].position;
        Require(legacyEditPlan.TryApply(
                normalizedLegacy,
                LegacyAniHierarchyAuthority.TreeDepth,
                out legacyEditError),
            "ELS_QT applies an explicitly selected treeDepth authority");
        Require(HodHierarchyValidator.TryValidate(
                normalizedLegacy.structure.parts, out validation),
            "ELS_QT normalized legacy hierarchy validates");
        Require(normalizedLegacy.animations[0].frames[0].parts[0].name == normalizedFrameLabel
            && normalizedLegacy.animations[0].frames[0].parts[0].position == normalizedFramePosition,
            "ELS_QT normalization keeps legacy frame labels and transforms at their indices");
        Require(normalizedLegacy.canSaveAsLegacyAni(out validation),
            "normalized ELS_QT remains representable as legacy ANI");
        normalizedLegacy.save(temporaryPath);
        ani2 normalizedReload = new ani2();
        Require(await normalizedReload.load(temporaryPath),
            "normalized ELS_QT legacy ANI reloads");
        Require(normalizedReload.sourceFormat == AniContainerFormat.LegacyAni
            && normalizedReload.structure.parts.Count == 167
            && CountFrames(normalizedReload) == 385,
            "normalized ELS_QT preserves legacy format, part count, and frames");
        Require(normalizedReload.hasLegacyIkData
            && normalizedReload.legacyIkRecordCount == 91,
            "normalized ELS_QT preserves its IKDATA records");
        Require(HodHierarchyRepair.TryValidateWithoutRepair(
                normalizedReload, out validation),
            "normalized ELS_QT reload no longer needs hierarchy repair");

        Require(ComputeSha256(sourcePath) == sourceHashBefore, "ELS_QT source fixture remains unchanged");
    }

    static async System.Threading.Tasks.Task TestLegacyIkDataStructuralEdits(string path)
    {
        WriteSyntheticLegacyAniWithIkData(path);
        ani2 inserted = new ani2();
        Require(await inserted.load(path), "synthetic IKDATA legacy ANI loads");
        Require(inserted.legacyIkDataSupportsPartEdits, "synthetic IKDATA supports part edits");
        Require(inserted.legacyIkRecordCount == 3, "synthetic IKDATA starts with three records");
        Require(inserted.structure.parts[2].unk == new Vector3(0, 1, 1),
            "synthetic IKDATA non-default value maps to PartB.x");

        inserted.addPart("Inserted.x", 1);
        Require(inserted.structure.parts.Count == 4
            && inserted.structure.parts[2].name == "Inserted.x"
            && inserted.structure.parts[3].name == "PartB.x",
            "legacy add inserts a child and shifts the following sibling");
        Require(inserted.structure.parts[2].unk == Vector3.one
            && inserted.structure.parts[3].unk == new Vector3(0, 1, 1),
            "IK attributes stay attached to their parts after insertion");
        string validation;
        Require(inserted.canSaveAsLegacyAni(out validation),
            "recognized IKDATA permits legacy part insertion");
        inserted.save(path);

        ani2 insertedReload = new ani2();
        Require(await insertedReload.load(path), "inserted IKDATA legacy ANI reloads");
        Require(insertedReload.structure.parts.Count == 4,
            "inserted legacy part count persists");
        Require(insertedReload.legacyIkRecordCount == 4,
            "IKDATA is rebuilt for every current part after index-shifting insertion");
        Require(insertedReload.structure.parts[2].unk == Vector3.one
            && insertedReload.structure.parts[3].unk == new Vector3(0, 1, 1),
            "rebuilt IKDATA preserves inserted defaults and shifted non-default data");

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 removed = new ani2();
        Require(await removed.load(path), "synthetic IKDATA reloads for deletion");
        Require(removed.removePart(2), "legacy IKDATA part deletion succeeds");
        Require(removed.canSaveAsLegacyAni(out validation),
            "recognized IKDATA permits legacy part deletion");
        removed.save(path);

        ani2 removedReload = new ani2();
        Require(await removedReload.load(path), "deleted IKDATA legacy ANI reloads");
        Require(removedReload.structure.parts.Count == 2,
            "deleted legacy part count persists");
        Require(removedReload.legacyIkRecordCount == 2,
            "IKDATA drops the deleted part record");
        Require(removedReload.structure.parts[0].name == "Root.x"
            && removedReload.structure.parts[1].name == "ParentA.x",
            "legacy deletion keeps the remaining part order");
    }

    static async System.Threading.Tasks.Task TestLegacyStructureEditPreparation(string path)
    {
        WriteSyntheticLegacyAniWithIkData(path);
        ani2 treeAuthority = new ani2();
        Require(await treeAuthority.load(path), "legacy ANI loads for treeDepth edit preparation");
        SetHierarchyWithMatchingFrame(
            treeAuthority,
            new[] { 0, 1, 1 },
            new[] { 1, 0, 0 });
        string beforePlan = ComputeHierarchySignature(treeAuthority);
        LegacyAniStructureEditPlan plan;
        string error;
        Require(LegacyAniStructureEditPlan.TryCreate(treeAuthority, out plan, out error),
            "treeDepth-only legacy hierarchy produces an edit plan");
        Require(!plan.RequiresAuthorityChoice,
            "treeDepth-only legacy hierarchy does not require an authority choice");
        Require(plan.RecommendedAuthority == LegacyAniHierarchyAuthority.TreeDepth,
            "treeDepth is selected when childCount alone is invalid");
        Require(ComputeHierarchySignature(treeAuthority) == beforePlan,
            "creating a legacy edit plan does not mutate hierarchy data");
        Require(plan.TryApply(treeAuthority, LegacyAniHierarchyAuthority.TreeDepth, out error),
            "treeDepth legacy edit plan applies");
        RequireValues(treeAuthority, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });
        Require(treeAuthority.TryAddPart("Inserted.x", 1, out error),
            "legacy part insertion succeeds after hierarchy preparation");
        Require(treeAuthority.structure.parts.Count == 4
            && treeAuthority.animations[0].frames[0].parts.Count == 4,
            "prepared legacy insertion updates structure and frame together");

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 childAuthority = new ani2();
        Require(await childAuthority.load(path), "legacy ANI loads for childCount edit preparation");
        SetHierarchyWithMatchingFrame(
            childAuthority,
            new[] { 0, 2, 1 },
            new[] { 2, 0, 0 });
        Require(LegacyAniStructureEditPlan.TryCreate(childAuthority, out plan, out error),
            "childCount-only legacy hierarchy produces an edit plan");
        Require(!plan.RequiresAuthorityChoice,
            "childCount-only legacy hierarchy does not require an authority choice");
        Require(plan.RecommendedAuthority == LegacyAniHierarchyAuthority.ChildCount,
            "childCount is selected only when treeDepth is invalid");
        Require(plan.TryApply(childAuthority, LegacyAniHierarchyAuthority.ChildCount, out error),
            "childCount legacy edit plan applies");
        RequireValues(childAuthority, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 ambiguousTree = new ani2();
        Require(await ambiguousTree.load(path), "legacy ANI loads for ambiguous hierarchy choice");
        SetHierarchyWithMatchingFrame(
            ambiguousTree,
            new[] { 0, 1, 1 },
            new[] { 1, 1, 0 });
        beforePlan = ComputeHierarchySignature(ambiguousTree);
        Require(LegacyAniStructureEditPlan.TryCreate(ambiguousTree, out plan, out error),
            "ambiguous legacy hierarchy produces a selectable edit plan");
        Require(!plan.RequiresAuthorityChoice
                && plan.RecommendedAuthority == LegacyAniHierarchyAuthority.ChildCount,
            "ambiguous legacy hierarchy defaults to original childCount authority");
        Require(ComputeHierarchySignature(ambiguousTree) == beforePlan,
            "ambiguous planning remains non-mutating before selection");
        Require(plan.TryApply(ambiguousTree, LegacyAniHierarchyAuthority.TreeDepth, out error),
            "ambiguous hierarchy accepts explicit treeDepth authority");
        RequireValues(ambiguousTree, new[] { 0, 1, 1 }, new[] { 2, 0, 0 });

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 ambiguousChild = new ani2();
        Require(await ambiguousChild.load(path), "legacy ANI reloads for childCount authority choice");
        SetHierarchyWithMatchingFrame(
            ambiguousChild,
            new[] { 0, 1, 1 },
            new[] { 1, 1, 0 });
        Require(LegacyAniStructureEditPlan.TryCreate(ambiguousChild, out plan, out error),
            "ambiguous legacy hierarchy recreates its selectable plan");
        Require(plan.TryApply(ambiguousChild, LegacyAniHierarchyAuthority.ChildCount, out error),
            "ambiguous hierarchy accepts explicit childCount authority");
        RequireValues(ambiguousChild, new[] { 0, 1, 2 }, new[] { 1, 1, 0 });

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 unrepairable = new ani2();
        Require(await unrepairable.load(path), "legacy ANI loads for unrepairable hierarchy check");
        SetHierarchyWithMatchingFrame(
            unrepairable,
            new[] { 0, 2, 1 },
            new[] { 1, 0, 0 });
        beforePlan = ComputeHierarchySignature(unrepairable);
        Require(!LegacyAniStructureEditPlan.TryCreate(unrepairable, out plan, out error),
            "legacy hierarchy is blocked when neither column is valid");
        Require(!string.IsNullOrEmpty(error),
            "unrepairable legacy hierarchy reports a blocking reason");
        Require(ComputeHierarchySignature(unrepairable) == beforePlan,
            "unrepairable legacy planning leaves source data unchanged");

        WriteSyntheticLegacyAniWithIkData(path);
        ani2 frameMismatch = new ani2();
        Require(await frameMismatch.load(path), "legacy ANI loads for frame mismatch check");
        SetHierarchyWithMatchingFrame(
            frameMismatch,
            new[] { 0, 1, 1 },
            new[] { 1, 0, 0 });
        hod2v1_Part mismatchedFramePart = frameMismatch.animations[0].frames[0].parts[1];
        mismatchedFramePart.childCount = 1;
        frameMismatch.animations[0].frames[0].parts[1] = mismatchedFramePart;
        beforePlan = ComputeHierarchySignature(frameMismatch);
        Require(!LegacyAniStructureEditPlan.TryCreate(frameMismatch, out plan, out error),
            "legacy hierarchy preparation blocks an index hierarchy mismatch");
        Require(error.Contains("位置1"),
            "frame hierarchy mismatch identifies the unsafe part index");
        Require(ComputeHierarchySignature(frameMismatch) == beforePlan,
            "frame mismatch rejection does not mutate any hierarchy data");
        Require(!frameMismatch.TryAddPart("Rejected.x", 1, out error),
            "transactional insertion rejects an unprepared hierarchy");
        Require(ComputeHierarchySignature(frameMismatch) == beforePlan,
            "failed insertion does not partially update structure or frames");
        Require(!frameMismatch.TryRemovePart(2, out error),
            "transactional deletion rejects an unprepared hierarchy");
        Require(ComputeHierarchySignature(frameMismatch) == beforePlan,
            "failed deletion does not partially update structure or frames");
    }

    static async System.Threading.Tasks.Task TestLegacyPreloadAn2Conversion(string path)
    {
        WriteSyntheticLegacyAniWithIkData(path);
        string originalHash = ComputeSha256(path);
        AniContainerFormat detectedFormat;
        string error;
        Require(UI_SelectMech.TryDetectContainerFormat(
                path, out detectedFormat, out error),
            "preload signature detection reads a legacy ANI");
        Require(detectedFormat == AniContainerFormat.LegacyAni,
            "preload signature detection identifies legacy ANI before full load");

        string destination = UI_SelectMech.GetAvailableAn2ConversionPath(path);
        Require(!File.Exists(destination)
            && !string.Equals(
                Path.GetFullPath(destination),
                Path.GetFullPath(path),
                StringComparison.OrdinalIgnoreCase),
            "preload conversion chooses a new non-overwriting AN2 path");

        try
        {
            ani2 source = new ani2();
            Require(await source.load(path), "legacy ANI loads after the preload choice");
            // Both columns form valid but different trees: conversion must normalize a copy.
            var root = source.structure.parts[0];
            root.childCount = 1;
            source.structure.parts[0] = root;
            var parent = source.structure.parts[1];
            parent.childCount = 1;
            source.structure.parts[1] = parent;
            string sourceHierarchy = ComputeHierarchySignature(source);
            var frame = new hod2v1("姿勢.hod") { parts = new List<hod2v1_Part>() };
            foreach (var part in source.structure.parts)
                frame.parts.Add(new hod2v1_Part {
                    name = part.name, treeDepth = part.treeDepth, childCount = part.childCount,
                    rotation = Quaternion.Euler(75, 0, 180), scale = Vector3.one * 1e-6f,
                    position = new Vector3(1, 2, 3), unk1 = Quaternion.identity,
                    unk2 = Quaternion.Euler(1, 2, 3), unk3 = Quaternion.Euler(4, 5, 6)
                });
            source.animations[0].frames.Add(frame);
            source.animations[0].scripts.Add(new script { unk = 7, time = 0.25f,
                squirrel = "// 日本語\nMove(1,2,3);\r\n" });
            source.animations[0].squirrelInit = "// 初期\n";
            sourceHierarchy = ComputeHierarchySignature(source);
            LegacyAniAn2ConversionResult conversion =
                await UI_SelectMech.ConvertLegacyAniToAn2Async(source, destination);
            Require(conversion.success, "preload-selected legacy ANI converts to AN2");
            Require(ComputeHierarchySignature(source) == sourceHierarchy,
                "conversion preserves the in-memory legacy hierarchy");
            var actualFrame = conversion.ani.animations[0].frames[0];
            Require(actualFrame.parts[2].treeDepth == 2 && frame.parts[2].treeDepth == 1,
                "converted frames use childCount while original frame columns survive");
            Require(actualFrame.parts[2].rotation.Equals(frame.parts[2].rotation)
                && actualFrame.parts[2].scale.Equals(frame.parts[2].scale)
                && actualFrame.parts[2].unk3.Equals(frame.parts[2].unk3),
                "conversion retains tiny scale, half-turn rotation and constraints");
            Require(conversion.ani.animations[0].scripts[0].squirrel == "// 日本語\r\nMove(1,2,3);\r\n"
                && conversion.ani.animations[0].squirrelInit == "// 初期\n"
                && actualFrame.filename == "姿勢.hod", "conversion retains Japanese text and script semantics");
            Require(conversion.ani._filename == destination
                && source._filename == path && source.sourceFormat == AniContainerFormat.LegacyAni,
                "conversion returns the final save path without changing source identity");
            Require(File.Exists(destination)
                && ReadSignature(destination, 3) == "AN2",
                "preload conversion writes an AN2 copy");
            Require(ComputeSha256(path) == originalHash,
                "preload conversion leaves the original legacy ANI byte exact");
            Require(conversion.ani != null
                && conversion.ani.sourceFormat == AniContainerFormat.An2,
                "preload conversion returns the converted AN2 reload");
            string hierarchyValidation;
            Require(HodHierarchyValidator.TryValidate(
                    conversion.ani.structure.parts, out hierarchyValidation),
                "preload-converted AN2 hierarchy validates");
            Require(conversion.ani.structure.parts.Count == 3
                && conversion.ani.animations.Count == 200,
                "preload-converted AN2 keeps structure and animation counts");
            Require(conversion.ani.structure.parts[2].unk == new Vector3(0, 1, 1),
                "preload-converted AN2 keeps IKDATA-derived part attributes");

            AniContainerFormat convertedFormat;
            Require(UI_SelectMech.TryDetectContainerFormat(
                    destination, out convertedFormat, out error)
                && convertedFormat == AniContainerFormat.An2,
                "preload signature detection does not prompt again for converted AN2");

            string collisionSafePath = UI_SelectMech.GetAvailableAn2ConversionPath(path);
            Require(!string.Equals(
                    collisionSafePath,
                    destination,
                    StringComparison.OrdinalIgnoreCase)
                && !File.Exists(collisionSafePath),
                "a pre-existing AN2 copy selects a different conversion filename");

            ani2 overwriteProbe = new ani2();
            Require(await overwriteProbe.load(path),
                "legacy ANI reloads for overwrite protection");
            string convertedHash = ComputeSha256(destination);
            LegacyAniAn2ConversionResult overwriteResult =
                await UI_SelectMech.ConvertLegacyAniToAn2Async(
                    overwriteProbe, destination);
            Require(!overwriteResult.success
                && overwriteResult.error.Contains("上書き"),
                "preload conversion rejects an existing destination");
            Require(ComputeSha256(destination) == convertedHash
                && ComputeSha256(path) == originalHash,
                "existing destination rejection changes neither file");

            LegacyAniAn2ConversionResult samePathResult =
                await UI_SelectMech.ConvertLegacyAniToAn2Async(
                    overwriteProbe, path);
            Require(!samePathResult.success
                && samePathResult.error.Contains("上書き"),
                "preload conversion refuses to replace the original legacy ANI");
            Require(ComputeSha256(path) == originalHash,
                "same-path rejection leaves the original legacy ANI unchanged");
        }
        finally
        {
            if (File.Exists(destination))
                File.Delete(destination);
        }
    }

    sealed class ConversionProgress : IProgress<int>
    {
        readonly Action<int> action;
        public ConversionProgress(Action<int> action) { this.action = action; }
        public void Report(int value) { action(value); }
    }

    static async System.Threading.Tasks.Task TestConversionFailureIsolation(string path)
    {
        string folder = Path.Combine(Path.GetTempPath(), "WindomXP-Conversion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string destination = Path.Combine(folder, "copy.an2");
        try
        {
            var source = new ani2();
            Require(await source.load(path), "conversion isolation source loads");
            string hash = ComputeSha256(path);
            string hierarchy = ComputeHierarchySignature(source);
            // Synchronous IProgress runs inside the reload, so the collision is deterministic.
            bool collided = false;
            var race = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, destination,
                new ConversionProgress(value => {
                    if (!collided) { File.WriteAllText(destination, "external file"); collided = true; }
                }));
            Require(collided && !race.success && File.ReadAllText(destination) == "external file",
                "late destination collision cannot overwrite or delete the external file");
            Require(Directory.GetFiles(folder).Length == 1, "collision cleans only its own staging file");
            File.Delete(destination);

            var name = source.structure.parts[0];
            var lossy = name;
            lossy.name = "日本語.x";
            source.structure.parts[0] = lossy;
            var rejected = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, destination);
            Require(!rejected.success && rejected.error.Contains("一致"),
                "AN2 ASCII name loss is rejected despite a successful public reload");
            Require(!File.Exists(destination) && Directory.GetFiles(folder).Length == 0,
                "round-trip mismatch never publishes a partial conversion");
            source.structure.parts[0] = name;

            string oldName = source.animations[0].name;
            source.animations[0].name = new string('x', 257);
            rejected = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, destination);
            Require(!rejected.success && Directory.GetFiles(folder).Length == 0,
                "mid-write failure cleans its staging file");
            source.animations[0].name = oldName;
            rejected = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, "bad\0.an2");
            Require(!rejected.success && !string.IsNullOrEmpty(rejected.error),
                "invalid paths are returned as conversion errors");

            Directory.CreateDirectory(destination);
            Require(UI_SelectMech.GetAvailableAn2ConversionPath(Path.Combine(folder, "copy.ani")) != destination,
                "conversion path skips an existing directory");
            rejected = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, destination);
            Require(!rejected.success && Directory.Exists(destination), "directory destination is preserved");
            Directory.Delete(destination);

            // Explicit Save As must validate before replacing any existing target, too.
            File.WriteAllText(destination, "existing target");
            File.WriteAllText(destination + ".tmp", "unrelated temporary file");
            source.structure.parts[0] = lossy;
            bool threw = false;
            try { source.saveAsAn2(destination); }
            catch (InvalidDataException) { threw = true; }
            source.structure.parts[0] = name;
            Require(threw && File.ReadAllText(destination) == "existing target"
                && File.ReadAllText(destination + ".tmp") == "unrelated temporary file",
                "Save As validation preserves the target and unrelated .tmp file");
            Require(Directory.GetFiles(folder).Length == 2, "Save As failure removes its unique staging file");
            Require(ComputeSha256(path) == hash && ComputeHierarchySignature(source) == hierarchy,
                "failure probes preserve the source file and in-memory hierarchy");
            string saved = Path.Combine(folder, "unchanged.ani");
            source.save(saved);
            Require(ComputeSha256(saved) == hash, "legacy save remains byte exact after failed conversions");
        }
        finally { Directory.Delete(folder, true); }
    }

    // Independent record traversal checks bytes on disk rather than USEncoder output.
    static List<byte[]> ReadScriptPayloads(string path, int legacyActionCount = 219)
    {
        var payloads = new List<byte[]>();
        using (var reader = new BinaryReader(File.OpenRead(path)))
        {
            bool legacy = System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3)) == "ANI";
            reader.BaseStream.Seek(256, SeekOrigin.Current);
            SkipHod(reader, legacy);
            int actions = legacy ? legacyActionCount : reader.ReadInt32();
            for (int a = 0; a < actions || (!legacy && reader.BaseStream.Position < reader.BaseStream.Length); a++)
            {
                reader.BaseStream.Seek(256, SeekOrigin.Current);
                if (!legacy) reader.ReadBytes(reader.ReadInt32());
                int frames = reader.ReadInt32();
                for (int f = 0; f < frames; f++)
                {
                    int nameLength = legacy ? 30 : reader.ReadInt16();
                    reader.BaseStream.Seek(nameLength, SeekOrigin.Current);
                    SkipHod(reader, legacy);
                }
                int count = reader.ReadInt32();
                for (int s = 0; s < count; s++)
                {
                    reader.BaseStream.Seek(8, SeekOrigin.Current);
                    payloads.Add(reader.ReadBytes(reader.ReadInt32()));
                }
            }
        }
        return payloads;
    }

    static void SkipHod(BinaryReader reader, bool legacy)
    {
        Require(System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3)) == (legacy ? "HOD" : "HD2"),
            "raw script scanner HOD boundary");
        int version = legacy ? -1 : reader.ReadInt32();
        int parts = reader.ReadInt32();
        Require(version >= -1 && version <= 1 && parts >= 0, "raw script scanner HOD header");
        reader.BaseStream.Seek(parts * (legacy ? 328L : version == 0 ? 399L : 179L), SeekOrigin.Current);
    }

    static async System.Threading.Tasks.Task TestScriptBytePreservation()
    {
        string sourcePath = Path.Combine(Application.dataPath, "..", "Windom_Data", "Robo", "ELS_QT", "script.ani");
        string folder = Path.Combine(Path.GetTempPath(), "WindomXP-ScriptBytes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string sourceHash = ComputeSha256(sourcePath);
            var source = new ani2();
            Require(await source.load(sourcePath), "script byte test loads real ELS_QT ANI");
            byte[] badComment = { 0x27, 0xa1, 0xeb, 0x6f };
            Require(USEncoder.ToEncoding.ToUnicode(badComment)[2] == '\0',
                "real legacy EB reproduces encoder NUL loss");
            string output = Path.Combine(folder, "converted.an2");
            var converted = await UI_SelectMech.ConvertLegacyAniToAn2Async(source, output);
            Require(converted.success, "ELS_QT raw script conversion succeeds");
            List<byte[]> expected = ReadScriptPayloads(sourcePath);
            List<byte[]> actual = ReadScriptPayloads(output);
            Require(expected.Count == 447 && actual.Count == expected.Count, "all 447 script payloads traversed");
            for (int i = 0; i < expected.Count; i++)
                Require(BytesEqual(expected[i], actual[i]), "ELS_QT script bytes preserved " + i);

            string resaved = Path.Combine(folder, "resaved.an2");
            converted.ani.save(resaved);
            Require(ComputeSha256(output) == ComputeSha256(resaved), "AN2 public reload and resave is byte exact");

            // Changing only timing must not discard the original script payload.
            script walking = source.animations[1].scripts[0];
            walking.unk += 1;
            walking.time += 0.01f;
            source.animations[1].scripts[0] = walking;
            string timed = Path.Combine(folder, "timing.ani");
            source.save(timed);
            actual = ReadScriptPayloads(timed);
            for (int i = 0; i < expected.Count; i++)
                Require(BytesEqual(expected[i], actual[i]), "timing edit preserves legacy script bytes " + i);

            // Decoder equality cannot be allowed to hide EB -> 00 byte corruption.
            byte[] bytes = File.ReadAllBytes(output);
            int changed = -1;
            for (int i = 0; i < bytes.Length - badComment.Length; i++)
                if (bytes[i] == 0x27 && bytes[i+1] == 0xa1 && bytes[i+2] == 0xeb && bytes[i+3] == 0x6f)
                { changed = i+2; break; }
            Require(changed >= 0, "locate raw EB comment in converted AN2");
            bytes[changed] = 0;
            string corrupt = Path.Combine(folder, "corrupt.an2");
            File.WriteAllBytes(corrupt, bytes);
            var corruptReload = new ani2();
            Require(await corruptReload.load(corrupt), "corrupted byte remains structurally loadable");
            bool rejected = false;
            try
            {
                typeof(ani2).GetMethod("ValidateAn2Export", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).Invoke(converted.ani, new object[] { corruptReload });
            }
            catch (System.Reflection.TargetInvocationException ex)
            { rejected = ex.InnerException is InvalidDataException && ex.InnerException.Message.Contains("script"); }
            Require(rejected, "byte validation rejects corruption despite identical decoded text");

            walking.squirrel += "\r\n' edited";
            source.animations[1].scripts[0] = walking;
            source.saveAsAn2(resaved);
            var safeComment = new ani2();
            Require(await safeComment.load(resaved) && !safeComment.animations[1].scripts[0].squirrel.Contains("\0")
                && safeComment.animations[1].scripts[0].squirrel.Contains("Move=(0,0,0.1f);"),
                "edited unrepresentable comment is made safe without removing Move");
            walking.squirrel = "' repaired comment\nMove=(0,0,0.2f);\n";
            source.animations[1].scripts[0] = walking;
            source.saveAsAn2(resaved);
            var edited = new ani2();
            Require(await edited.load(resaved) && edited.animations[1].scripts[0].squirrel
                == "' repaired comment\r\nMove=(0,0,0.2f);\r\n", "representable script edits save with CRLF");
            Require(ComputeSha256(sourcePath) == sourceHash, "all script tests preserve original ANI");
            TestInitialScriptBytes();
        }
        finally { Directory.Delete(folder, true); }
    }

    static void TestInitialScriptBytes()
    {
        using (var stream = new MemoryStream())
        {
            var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            writer.Write(new byte[256]);
            byte[] raw = { 0x27, 0xa1, 0xeb, 0x6f, 0x0d, 0x0a };
            writer.Write(raw.Length); writer.Write(raw);
            writer.Write(0); writer.Write(1);
            writer.Write(8); writer.Write(0.035f); writer.Write(raw.Length); writer.Write(raw);
            byte[] original = stream.ToArray();
            stream.Position = 0;
            var reader = new BinaryReader(stream);
            var structure = new hod2v0("test");
            var animation = new animation();
            animation.loadFromAni(ref reader, ref structure);
            using (var saved = new MemoryStream())
            {
                var savedWriter = new BinaryWriter(saved);
                animation.saveToAni(ref savedWriter);
                Require(BytesEqual(original, saved.ToArray()), "initial/timed raw scripts preserve EB and LF");
            }
        }
    }

    static async System.Threading.Tasks.Task TestAn2OriginalGameLimit()
    {
        string folder = Path.Combine(Path.GetTempPath(), "WindomXP-An2Limit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            foreach (int count in new[] { 0, 199, 200, 201, 219 })
            {
                var source = BuildAni(new[] { 0 }, new[] { 0 }, 1);
                var template = source.animations[0];
                source.animations.Clear();
                for (int i = 0; i < count; i++) source.animations.Add(new animation
                {
                    name = "slot" + i, frames = template.frames,
                    scripts = new List<script> { new script { unk = 8, time = 0.035f, squirrel = "Move=(0,0,0.1f);\r\n" } }
                });
                string path = Path.Combine(folder, count + ".an2");
                source.saveAsAn2(path);
                using (var reader = new BinaryReader(File.OpenRead(path)))
                {
                    reader.BaseStream.Position = 3 + 256;
                    SkipHod(reader, false);
                    Require(reader.ReadInt32() == Math.Min(count, 200), "on-disk original game count " + count);
                }
                var loaded = new ani2();
                Require(await loaded.load(path) && loaded.animations.Count == count,
                    "base and extension records reload " + count);
                Require(loaded.an2DeclaredAnimationCount == Math.Min(count, 200)
                    && loaded.editorOnlyAnimationCount == Math.Max(0, count - 200), "public slot counts " + count);
                if (count > 200) Require(loaded.animations[count - 1].name == "slot" + (count - 1)
                    && loaded.animations[count - 1].frames.Count == 1
                    && loaded.animations[count - 1].scripts.Count == 1, "last extra slot is fully retained");
                string resaved = Path.Combine(folder, count + "-resaved.an2");
                loaded.save(resaved);
                Require(ComputeSha256(path) == ComputeSha256(resaved), "count-limited resave is byte exact " + count);
                Require(source.animations.Count == count, "export preserves in-memory slots " + count);
            }

            // Migrate an earlier editor's AN2 declaring 219 without losing any record.
            byte[] expanded = File.ReadAllBytes(Path.Combine(folder, "219.an2"));
            const int countOffset = 3 + 256 + 11 + 399; // one structure part
            Buffer.BlockCopy(BitConverter.GetBytes(219), 0, expanded, countOffset, 4);
            string oldPath = Path.Combine(folder, "old219.an2");
            File.WriteAllBytes(oldPath, expanded);
            var old = new ani2();
            Require(await old.load(oldPath) && old.animations.Count == 219, "old declared-219 AN2 still loads");
            old.save(Path.Combine(folder, "migrated.an2"));
            Require(ComputeSha256(Path.Combine(folder, "219.an2")) == ComputeSha256(Path.Combine(folder, "migrated.an2")),
                "old 219 AN2 migrates to safe header with every record intact");

            // Never partially promote a truncated extension region or discard unknown bytes.
            foreach (bool truncated in new[] { false, true })
            {
                byte[] data = File.ReadAllBytes(Path.Combine(folder, truncated ? "201.an2" : "200.an2"));
                if (truncated) Array.Resize(ref data, data.Length - 1);
                else { int n = data.Length; Array.Resize(ref data, n + 5); data[n] = 0x7f; }
                string path = Path.Combine(folder, "opaque-" + truncated + ".an2");
                File.WriteAllBytes(path, data);
                var opaque = new ani2();
                Require(await opaque.load(path) && opaque.animations.Count == 200, "opaque tail is not promoted " + truncated);
                string saved = Path.Combine(folder, "opaque-save-" + truncated + ".an2");
                opaque.save(saved);
                Require(ComputeSha256(path) == ComputeSha256(saved), "opaque tail round trips exactly " + truncated);
            }
            TestCommentSafety();
        }
        finally { Directory.Delete(folder, true); }
    }

    // Exercise the actual AN2 reader/writer with raw source bytes and edited Unicode.
    static byte[] WriteCommentProbe(byte[] raw, string edited = null)
    {
        using (var input = new MemoryStream())
        {
            var writer = new BinaryWriter(input, System.Text.Encoding.UTF8, true);
            writer.Write(new byte[256]); writer.Write(raw.Length); writer.Write(raw);
            writer.Write(0); writer.Write(0);
            input.Position = 0;
            var reader = new BinaryReader(input);
            var structure = new hod2v0("test");
            var item = new animation();
            item.loadFromAni(ref reader, ref structure);
            if (edited != null) item.squirrelInit = edited;
            using (var output = new MemoryStream())
            {
                var saved = new BinaryWriter(output);
                item.saveToAni(ref saved);
                output.Position = 256;
                var check = new BinaryReader(output);
                return check.ReadBytes(check.ReadInt32());
            }
        }
    }

    static void TestCommentSafety()
    {
        byte[] suffix = System.Text.Encoding.ASCII.GetBytes("\r\nMove=(0,0,0.1f);\r\n");
        byte[] Join(byte[] a, byte[] b) { var list = new List<byte>(a); list.AddRange(b); return list.ToArray(); }
        var safe = Join(new byte[] { 39, 0xa1, 0xeb, 0x6f }, suffix);
        Require(BytesEqual(WriteCommentProbe(safe), safe), "original EB lead/trail comment stays byte exact");
        var japanese = USEncoder.ToEncoding.ToSJIS("'日本語コメント\r\nMove=(0,0,0.1f);\r\n");
        Require(BytesEqual(WriteCommentProbe(japanese), japanese), "representable Japanese comment stays intact");
        Require(BytesEqual(WriteCommentProbe(Join(new byte[] {39,0}, suffix)), Join(new byte[] {39,32}, suffix)),
            "existing AN2 comment NUL becomes space without touching command bytes");
        Require(BytesEqual(WriteCommentProbe(Join(new byte[] {39,0x81}, suffix)), Join(new byte[] {39,63}, suffix)),
            "invalid SJIS lead cannot consume the comment newline");
        Require(BytesEqual(WriteCommentProbe(new byte[] {39,0x81}), new byte[] {39,63}), "dangling comment lead becomes question mark");
        Require(System.Text.Encoding.ASCII.GetString(WriteCommentProbe(System.Text.Encoding.ASCII.GetBytes("'note\nMove=(0,0,1);")))
            == "'note\r\nMove=(0,0,1);", "LF comment boundary becomes original-game CRLF");
        var edited = WriteCommentProbe(Array.Empty<byte>(), "'comment \U0001F600\0\r\nMove=(0,0,1);");
        Require(Array.IndexOf(edited, (byte)0) < 0 && System.Text.Encoding.ASCII.GetString(edited).EndsWith("\r\nMove=(0,0,1);"),
            "edited unsupported Unicode is repaired only inside comment");
        foreach (string text in new[] { "Move=(0,\0,1);", "Name=\"o'\0x\";", "Move=(0,\U0001F600,1);" })
        {
            bool rejected = false;
            try { WriteCommentProbe(Array.Empty<byte>(), text); }
            catch (InvalidDataException) { rejected = true; }
            Require(rejected, "command/quoted-string loss is rejected, not treated as comment");
        }
        var quoted = Join(System.Text.Encoding.ASCII.GetBytes("Name=\"o'file\";'"), Join(new byte[] {0}, suffix));
        var expected = (byte[])quoted.Clone(); expected[15] = 32;
        Require(BytesEqual(WriteCommentProbe(quoted), expected), "apostrophe in filename leaves quote state intact");
        var sjisString = new byte[] { 78,61,34,0x83,0x5c,34,59,39,0,13,10,77,59 };
        var sjisExpected = (byte[])sjisString.Clone(); sjisExpected[8] = 32;
        Require(BytesEqual(WriteCommentProbe(sjisString), sjisExpected), "SJIS trail backslash cannot escape the closing quote");
    }

    static async System.Threading.Tasks.Task TestConversionDialogChoices()
    {
        var owner = new GameObject("ConversionDialogVerification");
        owner.SetActive(false);
        try
        {
            var selector = owner.AddComponent<UI_SelectMech>();
            selector.editAni = owner.AddComponent<UI_EditAni>();
            var dialogObject = new GameObject("Dialog");
            dialogObject.transform.SetParent(owner.transform);
            var dialog = dialogObject.AddComponent<UI_InputBox>();
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            textObject.transform.SetParent(dialogObject.transform);
            dialog.text = textObject.GetComponent<UnityEngine.UI.Text>();
            var inputObject = new GameObject("Input", typeof(RectTransform), typeof(UnityEngine.UI.InputField));
            inputObject.transform.SetParent(dialogObject.transform);
            dialog.input = inputObject.GetComponent<UnityEngine.UI.InputField>();
            selector.editAni.kakuninBox = dialog;
            selector.loadingUI = new GameObject("Loading");
            selector.loadingUI.transform.SetParent(owner.transform);
            var method = typeof(UI_SelectMech).GetMethod("AskLegacyAniConversionAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            foreach (bool convert in new[] { true, false })
            {
                var pending = (System.Threading.Tasks.Task<LegacyAniLoadChoice>)method.Invoke(selector,
                    new object[] { "conversion.an2" });
                Require(!pending.IsCompleted && !selector.loadingUI.activeSelf,
                    "conversion dialog waits with loading hidden");
                Require(dialog.text.text.Contains("conversion.an2") && dialog.text.text.Contains("AN2"),
                    "conversion dialog describes the actual destination");
                if (convert) dialog.Ok(); else dialog.cancel();
                Require(pending.IsCompleted, "both dialog choices resolve the pending load");
                Require(await pending == (convert ? LegacyAniLoadChoice.ConvertToAn2 : LegacyAniLoadChoice.KeepLegacy)
                    && selector.loadingUI.activeSelf, "dialog returns the explicit choice and restores loading");
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }

    static async System.Threading.Tasks.Task TestUnknownLegacyIkDataProtection(string path)
    {
        WriteSyntheticLegacyAniWithIkData(path, true);
        string originalHash = ComputeSha256(path);
        ani2 source = new ani2();
        Require(await source.load(path), "unknown IKDATA legacy ANI still loads");
        Require(source.hasLegacyIkData, "unknown IKDATA signature is retained");
        Require(!source.legacyIkDataSupportsPartEdits,
            "non-13-byte IKDATA is not treated as part-indexed data");
        string structureEditError;
        Require(!source.canChangeLegacyStructure(out structureEditError)
            && structureEditError.Contains("IKDATA"),
            "unknown IKDATA blocks legacy structure changes before mutation");
        LegacyAniStructureEditPlan structureEditPlan;
        Require(!LegacyAniStructureEditPlan.TryCreate(
                source, out structureEditPlan, out structureEditError)
            && structureEditError.Contains("IKDATA"),
            "unknown IKDATA cannot create a legacy hierarchy edit plan");

        string validation;
        Require(source.canSaveAsLegacyAni(out validation),
            "unmodified unknown IKDATA can be preserved as legacy ANI");
        source.save(path);
        Require(ComputeSha256(path) == originalHash,
            "unmodified unknown IKDATA save remains byte exact");

        hod2v0_Part renamed = source.structure.parts[0];
        renamed.name = "RenamedRoot.x";
        source.structure.parts[0] = renamed;
        Require(!source.canSaveAsLegacyAni(out validation)
            && validation.Contains("IKDATA"),
            "unknown IKDATA rejects structure changes with an actionable reason");
    }

    static void WriteSyntheticLegacyAniWithIkData(string path, bool unknownFirstRecord = false)
    {
        BinaryWriter writer = new BinaryWriter(File.Open(
            path, FileMode.Create, FileAccess.Write, FileShare.None));
        try
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("ANI"));
            WriteFixedBytes(writer, USEncoder.ToEncoding.ToSJIS("Structure.hod"), 256);

            hod1 structure = new hod1("Structure.hod");
            structure.parts = new List<hod1_Part>
            {
                new hod1_Part
                {
                    treeDepth = 0,
                    childCount = 2,
                    name = "Root.x",
                    transform = Matrix4x4.identity
                },
                new hod1_Part
                {
                    treeDepth = 1,
                    childCount = 0,
                    name = "ParentA.x",
                    transform = Matrix4x4.identity
                },
                new hod1_Part
                {
                    treeDepth = 1,
                    childCount = 0,
                    name = "PartB.x",
                    transform = Matrix4x4.identity
                }
            };
            structure.saveToBinary(ref writer);

            for (int animationIndex = 0; animationIndex < 200; animationIndex++)
            {
                WriteFixedBytes(
                    writer,
                    USEncoder.ToEncoding.ToSJIS("Animation" + animationIndex),
                    256);
                writer.Write(0);
                writer.Write(0);
            }

            writer.Write(System.Text.Encoding.ASCII.GetBytes("IKDATA"));
            writer.Write(3);
            if (unknownFirstRecord)
            {
                writer.Write(12);
                writer.Write(new byte[12]);
            }
            else
            {
                WriteSyntheticIkRecord(writer, Vector3.one);
            }
            WriteSyntheticIkRecord(writer, Vector3.one);
            WriteSyntheticIkRecord(writer, new Vector3(0, 1, 1));
        }
        finally
        {
            writer.Dispose();
        }
    }

    static void WriteSyntheticIkRecord(BinaryWriter writer, Vector3 unk)
    {
        writer.Write(13);
        writer.Write((byte)1);
        writer.Write(unk.x);
        writer.Write(unk.y);
        writer.Write(unk.z);
    }

    static void WriteFixedBytes(BinaryWriter writer, byte[] value, int byteCount)
    {
        if (value.Length > byteCount)
            throw new InvalidDataException("Synthetic fixed string is too long.");
        writer.Write(value);
        for (int i = value.Length; i < byteCount; i++)
            writer.Write((byte)0);
    }

    static string ReadSignature(string path, int byteCount)
    {
        using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(byteCount));
    }

    static string ComputeSha256(string path)
    {
        using (SHA256 sha = SHA256.Create())
        using (FileStream stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    static string ComputeHierarchySignature(ani2 ani)
    {
        using (MemoryStream stream = new MemoryStream())
        {
            using (BinaryWriter writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(ani.structure.parts.Count);
                for (int partIndex = 0; partIndex < ani.structure.parts.Count; partIndex++)
                {
                    hod2v0_Part part = ani.structure.parts[partIndex];
                    writer.Write(part.name ?? "");
                    writer.Write(part.treeDepth);
                    writer.Write(part.childCount);
                }

                writer.Write(ani.animations.Count);
                for (int animationIndex = 0; animationIndex < ani.animations.Count; animationIndex++)
                {
                    animation animationData = ani.animations[animationIndex];
                    writer.Write(animationData.frames.Count);
                    for (int frameIndex = 0; frameIndex < animationData.frames.Count; frameIndex++)
                    {
                        hod2v1 frame = animationData.frames[frameIndex];
                        writer.Write(frame.parts.Count);
                        for (int partIndex = 0; partIndex < frame.parts.Count; partIndex++)
                        {
                            hod2v1_Part part = frame.parts[partIndex];
                            writer.Write(part.name ?? "");
                            writer.Write(part.treeDepth);
                            writer.Write(part.childCount);
                        }
                    }
                }
            }

            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", "");
        }
    }

    static void RequireLegacyMatricesEqual(
        string expectedPath,
        string actualPath,
        int animationCount,
        string message)
    {
        List<byte[]> expected = ReadLegacyMatrices(expectedPath, animationCount);
        List<byte[]> actual = ReadLegacyMatrices(actualPath, animationCount);
        if (expected.Count != actual.Count)
            throw new InvalidOperationException("Assertion failed: " + message + " matrix count");

        for (int matrixIndex = 0; matrixIndex < expected.Count; matrixIndex++)
        {
            if (!BytesEqual(expected[matrixIndex], actual[matrixIndex]))
            {
                throw new InvalidOperationException(
                    "Assertion failed: " + message + " at matrix " + matrixIndex);
            }
        }
        assertions++;
    }

    static List<byte[]> ReadLegacyMatrices(string path, int animationCount)
    {
        List<byte[]> matrices = new List<byte[]>();
        using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
        {
            if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3)) != "ANI")
                throw new InvalidDataException("Expected legacy ANI: " + path);
            reader.BaseStream.Seek(256, SeekOrigin.Current);
            ReadLegacyHodMatrices(reader, matrices);

            for (int animationIndex = 0; animationIndex < animationCount; animationIndex++)
            {
                reader.BaseStream.Seek(256, SeekOrigin.Current);
                int frameCount = reader.ReadInt32();
                for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
                {
                    reader.BaseStream.Seek(30, SeekOrigin.Current);
                    ReadLegacyHodMatrices(reader, matrices);
                }

                int scriptCount = reader.ReadInt32();
                for (int scriptIndex = 0; scriptIndex < scriptCount; scriptIndex++)
                {
                    reader.BaseStream.Seek(8, SeekOrigin.Current);
                    int textLength = reader.ReadInt32();
                    reader.BaseStream.Seek(textLength, SeekOrigin.Current);
                }
            }
        }
        return matrices;
    }

    static void ReadLegacyHodMatrices(BinaryReader reader, List<byte[]> matrices)
    {
        if (System.Text.Encoding.ASCII.GetString(reader.ReadBytes(3)) != "HOD")
            throw new InvalidDataException("Expected legacy HOD while collecting matrices.");
        int partCount = reader.ReadInt32();
        for (int partIndex = 0; partIndex < partCount; partIndex++)
        {
            reader.BaseStream.Seek(4 + 4 + 256, SeekOrigin.Current);
            byte[] matrix = reader.ReadBytes(64);
            if (matrix.Length != 64)
                throw new EndOfStreamException("Legacy HOD matrix is truncated.");
            matrices.Add(matrix);
        }
    }

    static void RequireTailEqual(string expectedPath, string actualPath, string message)
    {
        byte[] expected = ReadTailFromLastSignature(expectedPath, "IKDATA");
        byte[] actual = ReadTailFromLastSignature(actualPath, "IKDATA");
        Require(BytesEqual(expected, actual), message);
    }

    static byte[] ReadTailFromLastSignature(string path, string signature)
    {
        byte[] data = File.ReadAllBytes(path);
        byte[] pattern = System.Text.Encoding.ASCII.GetBytes(signature);
        int offset = -1;
        for (int i = data.Length - pattern.Length; i >= 0; i--)
        {
            bool matches = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j])
                {
                    matches = false;
                    break;
                }
            }
            if (matches)
            {
                offset = i;
                break;
            }
        }
        if (offset < 0)
            throw new InvalidDataException("Tail signature was not found: " + signature);

        byte[] tail = new byte[data.Length - offset];
        Buffer.BlockCopy(data, offset, tail, 0, tail.Length);
        return tail;
    }

    static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                return false;
        }
        return true;
    }

    static List<hod2v1_Part> Permute(List<hod2v1_Part> parts, params int[] indexes)
    {
        List<hod2v1_Part> result = new List<hod2v1_Part>(indexes.Length);
        for (int i = 0; i < indexes.Length; i++)
            result.Add(parts[indexes[i]]);
        return result;
    }

    static int CountFrames(ani2 ani)
    {
        int count = 0;
        foreach (animation animationData in ani.animations)
            count += animationData.frames.Count;
        return count;
    }

    static void RequireAllFramesMatchStructure(ani2 ani, string message)
    {
        for (int animationIndex = 0; animationIndex < ani.animations.Count; animationIndex++)
        {
            animation animationData = ani.animations[animationIndex];
            for (int frameIndex = 0; frameIndex < animationData.frames.Count; frameIndex++)
            {
                hod2v1 frame = animationData.frames[frameIndex];
                if (frame.parts.Count != ani.structure.parts.Count)
                {
                    throw new InvalidOperationException(
                        "Assertion failed: " + message + " part count at animation "
                        + animationIndex + " frame " + frameIndex);
                }

                for (int partIndex = 0; partIndex < frame.parts.Count; partIndex++)
                {
                    hod2v1_Part framePart = frame.parts[partIndex];
                    hod2v0_Part structurePart = ani.structure.parts[partIndex];
                    if (framePart.treeDepth != structurePart.treeDepth
                        || framePart.childCount != structurePart.childCount)
                    {
                        throw new InvalidOperationException(
                            "Assertion failed: " + message + " at animation "
                            + animationIndex + " frame " + frameIndex
                            + " part " + partIndex);
                    }
                }
            }
        }

        assertions++;
    }

    static ani2 BuildAni(int[] depths, int[] childCounts, int frameCount)
    {
        ani2 ani = new ani2();
        ani.structure = new hod2v0("Structure.hod");
        ani.structure.parts = new List<hod2v0_Part>();

        for (int i = 0; i < depths.Length; i++)
        {
            hod2v0_Part part = new hod2v0_Part();
            part.name = "Part" + i;
            part.treeDepth = depths[i];
            part.childCount = childCounts[i];
            ani.structure.parts.Add(part);
        }

        animation animationData = new animation();
        animationData.name = "Verification";
        animationData.frames = new List<hod2v1>();
        animationData.scripts = new List<script>();
        for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            hod2v1 frame = new hod2v1("Frame" + frameIndex + ".hod");
            frame.parts = new List<hod2v1_Part>();
            for (int i = 0; i < depths.Length; i++)
            {
                hod2v1_Part part = new hod2v1_Part();
                part.name = "Part" + i;
                part.treeDepth = depths[i];
                part.childCount = childCounts[i];
                frame.parts.Add(part);
            }
            animationData.frames.Add(frame);
        }

        ani.animations = new List<animation> { animationData };
        return ani;
    }

    static void SetHierarchyWithMatchingFrame(
        ani2 ani,
        int[] depths,
        int[] childCounts)
    {
        Require(ani.structure.parts.Count == depths.Length
            && depths.Length == childCounts.Length,
            "test hierarchy dimensions match the structure");

        for (int i = 0; i < depths.Length; i++)
        {
            hod2v0_Part structurePart = ani.structure.parts[i];
            structurePart.treeDepth = depths[i];
            structurePart.childCount = childCounts[i];
            ani.structure.parts[i] = structurePart;
        }

        hod2v1 frame = new hod2v1("LegacyEditFrame.hod");
        frame.parts = new List<hod2v1_Part>();
        for (int i = 0; i < depths.Length; i++)
        {
            hod2v1_Part framePart = new hod2v1_Part
            {
                name = ani.structure.parts[i].name,
                treeDepth = depths[i],
                childCount = childCounts[i],
                position = Vector3.zero,
                rotation = Quaternion.identity,
                scale = Vector3.one,
                unk1 = Quaternion.identity,
                unk2 = Quaternion.identity,
                unk3 = Quaternion.identity
            };
            frame.parts.Add(framePart);
        }

        ani.animations[0].frames.Add(frame);
    }

    static void RequireValues(ani2 ani, int[] expectedDepths, int[] expectedCounts)
    {
        for (int i = 0; i < expectedDepths.Length; i++)
        {
            Require(ani.structure.parts[i].treeDepth == expectedDepths[i], "structure treeDepth " + i);
            Require(ani.structure.parts[i].childCount == expectedCounts[i], "structure childCount " + i);
        }

        foreach (animation animationData in ani.animations)
        {
            foreach (hod2v1 frame in animationData.frames)
            {
                for (int i = 0; i < expectedDepths.Length; i++)
                {
                    Require(frame.parts[i].treeDepth == expectedDepths[i], "frame treeDepth " + i);
                    Require(frame.parts[i].childCount == expectedCounts[i], "frame childCount " + i);
                }
            }
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException("Assertion failed: " + message);
        assertions++;
    }
}
