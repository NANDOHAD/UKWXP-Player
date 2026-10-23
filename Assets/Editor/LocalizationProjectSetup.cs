using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Localization;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class LocalizationProjectSetup
{
    static readonly string[] ScenePathCandidates =
    {
        "Assets/Scenes/MechSelection.unity",
        "Assets/MechSelection.unity",
    };

    const string LocalizationRoot = "Assets/Localization";
    const string LocaleRoot = LocalizationRoot + "/Locales";
    const string TableRoot = LocalizationRoot + "/Tables";
    const string SourceRoot = LocalizationRoot + "/Source";
    const string FixedDictionaryPath = SourceRoot + "/FixedTranslations.tsv";
    const string UnmatchedTextsPath = SourceRoot + "/UnmatchedSceneTexts.tsv";

    sealed class Translation
    {
        public readonly string Key;
        public readonly string Japanese;
        public readonly string English;

        public Translation(string key, string japanese, string english)
        {
            Key = key;
            Japanese = japanese;
            English = english;
        }

        public bool Matches(string value)
        {
            string normalized = Normalize(value);
            return normalized == Normalize(Japanese) || normalized == Normalize(English);
        }
    }

    static Translation Fixed(string japanese, string english)
    {
        return new Translation(UILocalization.FixedKey(japanese), japanese, english);
    }

    static Translation Dynamic(string key, string japanese, string english)
    {
        return new Translation(key, japanese, english);
    }

    static readonly Translation[] DynamicTranslations =
    {
        Dynamic("status.loading.complete", "読み込み完了", "Loading complete."),
        Dynamic("status.loading.progress", "読み込み中...{0}%", "Loading...{0}%"),
        Dynamic("dialog.no_mecha_data", "ディレクトリ内に機体データがみつかりません。", "No mecha data was found in the directory."),
        Dynamic("dialog.no_mecha_data_found", "ディレクトリ内に機体データがみつかりませんでした。", "No mecha data was found in the directory."),
        Dynamic("dialog.initial_directory_created", "初期ディレクトリ（Windom_Data\\Robo）を作成しました。", "Created the default directory (Windom_Data\\Robo)."),
        Dynamic("dialog.mecha_load_failed", "機体データの読み込みに失敗しました。\n{0}", "Failed to load mecha data.\n{0}"),
        Dynamic("dialog.ani_save_failed", "ANIファイルの保存に失敗しました。\n{0}", "Failed to save the ANI file.\n{0}"),
        Dynamic("dialog.hod_new_name", "新しいHODファイルの名前を入力してください（拡張子.hodは除く）。", "Enter a name for the new HOD file (without the .hod extension)."),
        Dynamic("dialog.hod_select", "読み込むHODファイルを選択してください", "Select the HOD file to load."),
        Dynamic("dialog.hod_not_found", "フォルダ内にHODファイルが見つかりません: {0}", "No HOD files were found in the folder: {0}"),
        Dynamic("dialog.hod_load_failed", "HODファイルの読み込みに失敗しました: {0}", "Failed to load the HOD file: {0}"),
        Dynamic("dialog.hod_part_count_mismatch", "読み込んだHODファイルのパーツ数({0})が現在のパーツ数({1})と異なります。読み込みを中止します。", "The loaded HOD contains {0} parts, but the current mecha contains {1}. Loading was canceled."),
        Dynamic("dialog.hod_load_exception", "HODファイルの読み込み中にエラーが発生しました: {0}", "An error occurred while loading the HOD file: {0}"),
        Dynamic("dialog.animation_new_name", "新しいアニメーション名を入力してください", "Enter a new animation name."),
        Dynamic("dialog.hod_no_frames", "このアニメーションにHODファイルはありません。", "This animation does not contain any HOD frames."),
        Dynamic("dialog.hod_rename", "HODファイルを改名します。新しい名前を入力してください。", "Enter a new name for the HOD file."),
        Dynamic("dialog.apply_selected_part_all_hods", "選択パーツの値を全てのHODに適用します。", "Apply the selected part values to every HOD."),
        Dynamic("dialog.apply_selected_value_all_hods", "選択値を全てのHODに適用します。", "Apply the selected value to every HOD."),
        Dynamic("dialog.animation_sync_range", "同期させたいアニメーションをカンマで区切って入力してください。2つのアニメーションの間にダッシュを入れると、範囲を指定できます。  例: 1,2, 6-10", "Enter animation numbers separated by commas. Use a dash to specify a range. Example: 1,2, 6-10"),
        Dynamic("dialog.select_part_to_delete", "削除するパーツを選択してください。", "Select a part to delete."),
        Dynamic("dialog.root_part_cannot_delete", "ルートパーツは削除できません。", "The root part cannot be deleted."),
        Dynamic("dialog.part_data_invalid_delete", "パーツ情報の整合性を確認できないため削除できません。", "The part cannot be deleted because its data integrity could not be verified."),
        Dynamic("dialog.part_select_rename", "どのパーツの名前を変更しますか？", "Which part do you want to rename?"),
        Dynamic("dialog.part_missing_use_empty", "指定名のパーツはフォルダに存在しません。空のパーツと入れ替えます。", "The specified part does not exist in the folder. It will be replaced with an empty part."),
        Dynamic("dialog.add_part_under", "{0}の下に新規パーツを追加します。", "Add a new part under {0}."),
        Dynamic("dialog.delete_part_with_children", "子パーツごと削除しますがよろしいですか？", "Delete the selected part and all of its child parts?"),
        Dynamic("dialog.delete_selected_part", "選択パーツを削除します。", "Delete the selected part."),
        Dynamic("default.hod_new_name", "新規HODファイル", "New HOD File"),
        Dynamic("help.application_version", "Ver.{0}", "Version: {0}"),

        Dynamic("hod.part.label", "パーツ[{0}]「{1}」", "Part[{0}] \"{1}\""),
        Dynamic("hod.validator.no_parts", "パーツ情報がありません。", "No part information is available."),
        Dynamic("hod.validator.tree_depth_negative", "{0}: treeDepthが負の値です（{1}）。", "{0}: treeDepth is negative ({1})."),
        Dynamic("hod.validator.first_tree_depth", "{0}: 先頭パーツのtreeDepthは0である必要があります（{1}）。", "{0}: the first part's treeDepth must be 0 ({1})."),
        Dynamic("hod.validator.tree_depth_jump", "{0}: treeDepthが直前のパーツから2段以上深くなっています（{1}→{2}）。", "{0}: treeDepth increases by more than one from the previous part ({1}->{2})."),
        Dynamic("hod.validator.additional_root", "{0}: 2個目以降のルートパーツです。", "{0}: this is a root part after the first part."),
        Dynamic("hod.validator.missing_parent", "{0}: treeDepth={1}の親候補が前方にありません。", "{0}: no preceding parent candidate exists for treeDepth={1}."),
        Dynamic("hod.validator.child_count_mismatch", "{0}: childCountが一致しません（記録={1}、深度列から算出={2}）。", "{0}: childCount does not match (recorded={1}, calculated from depths={2})."),
        Dynamic("hod.validator.more_issues", "\nほか{0}件の不整合があります。", "\nThere are {0} more inconsistencies."),

        Dynamic("hod.repair.preview_change", "パーツ[{0}]「{1}」: treeDepth {2}→{3}, childCount {4}→{5}", "Part[{0}] \"{1}\": treeDepth {2}->{3}, childCount {4}->{5}"),
        Dynamic("hod.repair.preview_more", "\nほか{0}件を修復します。", "\n{0} more changes will be repaired."),
        Dynamic("hod.repair.no_unique_method", "この不整合には一意な自動修復方法がありません。", "There is no unique automatic repair method for this inconsistency."),
        Dynamic("hod.repair.no_structure", "構造HODがありません。", "No structure HOD is available."),
        Dynamic("hod.repair.structure_part_count", "構造HODのパーツ数が修復計画と一致しません（{0}/{1}）。", "The structure HOD part count does not match the repair plan ({0}/{1})."),
        Dynamic("hod.repair.structure_changed", "構造HODのパーツ[{0}]が修復計画の作成後に変更されています。", "Structure HOD part[{0}] changed after the repair plan was created."),
        Dynamic("hod.repair.no_animations", "アニメーション情報がありません。", "No animation information is available."),
        Dynamic("hod.repair.animation_frames_missing", "アニメーション[{0}]のフレーム情報がありません。", "Animation[{0}] has no frame information."),
        Dynamic("hod.repair.frame_parts_missing", "アニメーション[{0}] フレーム[{1}]のパーツ情報がありません。", "Animation[{0}] frame[{1}] has no part information."),
        Dynamic("hod.repair.frame_part_count", "アニメーション[{0}] フレーム[{1}]のパーツ数が構造HODと一致しません（{2}/{3}）。", "Animation[{0}] frame[{1}] part count does not match the structure HOD ({2}/{3})."),
        Dynamic("hod.repair.frame_order_mismatch", "アニメーション[{0}] フレーム[{1}]のパーツ順が構造HODと一致しません（位置{2}: 「{3}」/「{4}」）。", "Animation[{0}] frame[{1}] part order does not match the structure HOD (position {2}: \"{3}\"/\"{4}\")."),
        Dynamic("hod.repair.frame_sync_required", "構造HODとアニメーションフレームのパーツ順または階層列が一致しません。", "The structure HOD and animation frames do not have matching part order or hierarchy columns."),
        Dynamic("hod.repair.tree_depth.no_parts_summary", "パーツ情報がないためtreeDepthを正として修復できません。", "Cannot repair using treeDepth because no part information is available."),
        Dynamic("hod.repair.tree_depth.no_parts_details", "treeDepthから階層を復元できません。", "The hierarchy cannot be reconstructed from treeDepth."),
        Dynamic("hod.repair.tree_depth.unrepairable", "treeDepthを正として修復できません。", "Cannot repair using treeDepth."),
        Dynamic("hod.repair.tree_depth.summary", "treeDepthを正としてchildCountを修復します。", "Repair childCount using treeDepth."),
        Dynamic("hod.repair.tree_depth.details", "treeDepthが表す階層を維持し、全パーツのchildCountを再計算します。", "Keep the hierarchy represented by treeDepth and recalculate childCount for all parts."),
        Dynamic("hod.repair.child_count.no_parts_summary", "パーツ情報がないためchildCountを正として修復できません。", "Cannot repair using childCount because no part information is available."),
        Dynamic("hod.repair.child_count.no_parts_details", "childCountから階層を復元できません。", "The hierarchy cannot be reconstructed from childCount."),
        Dynamic("hod.repair.child_count.unrepairable", "childCountを正として修復できません。", "Cannot repair using childCount."),
        Dynamic("hod.repair.child_count.summary", "childCountを正としてtreeDepthを修復します。", "Repair treeDepth using childCount."),
        Dynamic("hod.repair.child_count.details", "childCountが表す階層を維持し、全パーツのtreeDepthを再計算します。", "Keep the hierarchy represented by childCount and recalculate treeDepth for all parts."),
        Dynamic("hod.repair.no_parts_summary", "パーツ情報がないため修復できません。", "Cannot repair because no part information is available."),
        Dynamic("hod.repair.no_parts_details", "treeDepthとchildCountのどちらからも階層を復元できません。", "The hierarchy cannot be reconstructed from either treeDepth or childCount."),
        Dynamic("hod.repair.no_inconsistency", "パーツ階層に不整合はありません。", "The part hierarchy has no inconsistencies."),
        Dynamic("hod.repair.ambiguous.summary", "treeDepthとchildCountが、それぞれ別の有効な階層を表しています。", "treeDepth and childCount each describe a different valid hierarchy."),
        Dynamic("hod.repair.ambiguous.details", "どちらを正しい値とみなすか一意に決められないため、自動修復は行いません。", "Automatic repair is not performed because the correct representation cannot be determined uniquely."),
        Dynamic("hod.repair.tree_depth.can_repair_summary", "treeDepthを正としてchildCountを修復できます。", "childCount can be repaired using treeDepth."),
        Dynamic("hod.repair.tree_depth.can_repair_details", "treeDepthの並びは有効ですが、childCountからは有効な階層を復元できません。", "The treeDepth sequence is valid, but a valid hierarchy cannot be reconstructed from childCount."),
        Dynamic("hod.repair.child_count.can_repair_summary", "childCountを正としてtreeDepthを修復できます。", "treeDepth can be repaired using childCount."),
        Dynamic("hod.repair.child_count.can_repair_details", "childCountの並びは有効ですが、treeDepthからは有効な階層を復元できません。", "The childCount sequence is valid, but a valid hierarchy cannot be reconstructed from treeDepth."),
        Dynamic("hod.repair.both_invalid.summary", "treeDepthとchildCountの両方に不整合があるため修復できません。", "Cannot repair because both treeDepth and childCount are inconsistent."),
        Dynamic("hod.repair.both_invalid.details", "treeDepth: {0}\nchildCount: {1}", "treeDepth: {0}\nchildCount: {1}"),
        Dynamic("hod.repair.reason.first_tree_depth", "先頭パーツのtreeDepthが0ではありません（{0}）。", "The first part's treeDepth is not 0 ({0})."),
        Dynamic("hod.repair.reason.tree_depth_negative", "パーツ[{0}]のtreeDepthが負の値です（{1}）。", "Part[{0}] has a negative treeDepth ({1})."),
        Dynamic("hod.repair.reason.additional_root", "パーツ[{0}]が2個目以降のルートになっています。", "Part[{0}] is a root after the first part."),
        Dynamic("hod.repair.reason.tree_depth_jump", "パーツ[{0}]のtreeDepthが2段以上増加しています（{1}→{2}）。", "Part[{0}]'s treeDepth increases by more than one ({1}->{2})."),
        Dynamic("hod.repair.reason.child_count_negative", "パーツ[{0}]のchildCountが負の値です（{1}）。", "Part[{0}] has a negative childCount ({1})."),
        Dynamic("hod.repair.reason.no_parent_slot", "パーツ[{0}]を接続できる親のchildCount枠がありません。", "There is no parent childCount slot available for Part[{0}]."),
        Dynamic("hod.repair.reason.missing_children", "childCountが要求する子パーツが{0}個不足しています。", "childCount requires {0} more child parts than are available."),

        Dynamic("hod.prune.preview_range", "パーツ[{0}]「{1}」{2}", "Part[{0}] \"{1}\"{2}"),
        Dynamic("hod.prune.preview_descendants", "（配下{0}個を含む）", " (including {0} descendants)"),
        Dynamic("hod.prune.preview_more_ranges", "ほか{0}範囲", "{0} more ranges"),
        Dynamic("hod.prune.preview_total", "合計{0}パーツを除外します。", "A total of {0} parts will be excluded."),
        Dynamic("hod.prune.no_unique_orphan", "安全に除外できる孤立パーツを一意に特定できません。", "The orphan parts that can be safely excluded cannot be identified uniquely."),
        Dynamic("hod.prune.structure_part_count", "除外後の構造HODパーツ数が修復計画と一致しません。", "The structure HOD part count after exclusion does not match the repair plan."),
        Dynamic("hod.prune.validation_failed", "除外後の階層検証に失敗しました。\n{0}", "Hierarchy validation failed after exclusion.\n{0}"),
        Dynamic("hod.prune.structure_plan_count", "構造HODのパーツ数が除外計画と一致しません（{0}/{1}）。", "The structure HOD part count does not match the exclusion plan ({0}/{1})."),
        Dynamic("hod.prune.structure_changed", "構造HODのパーツ[{0}]が除外計画の作成後に変更されています。", "Structure HOD part[{0}] changed after the exclusion plan was created."),
        Dynamic("hod.prune.frame_hierarchy_mismatch", "アニメーション[{0}] フレーム[{1}]の階層情報が構造HODと一致しません（位置{2}）。", "Animation[{0}] frame[{1}] hierarchy data does not match the structure HOD (position {2})."),
        Dynamic("hod.prune.no_parts", "パーツ情報がないため除外できません。", "Cannot exclude parts because no part information is available."),
        Dynamic("hod.prune.repair_or_ambiguous", "値の修復が可能、または削除対象が曖昧なため、自動除外は行いません。", "Automatic exclusion is not performed because the values can be repaired or the exclusion target is ambiguous."),
        Dynamic("hod.prune.invalid_root_depth", "先頭ルートのtreeDepthが不正なため、安全な除外範囲を決められません。", "The safe exclusion range cannot be determined because the first root treeDepth is invalid."),
        Dynamic("hod.prune.negative_depth_range", "パーツ[{0}]のtreeDepthが負のため、子階層の範囲を決められません。", "The child hierarchy range cannot be determined because Part[{0}] has a negative treeDepth."),
        Dynamic("hod.prune.no_root_after_prune", "除外後に有効なルートパーツが残りません。", "No valid root part would remain after exclusion."),
        Dynamic("hod.prune.depth_rebuild_failed", "除外後のtreeDepthを有効な階層として構築できません。\n{0}", "The remaining treeDepth values cannot form a valid hierarchy after exclusion.\n{0}"),
        Dynamic("hod.prune.summary", "親へ接続できない孤立パーツを{0}個除外できます。", "{0} orphan parts that cannot connect to a parent can be excluded."),
        Dynamic("hod.prune.details", "除外後は残ったtreeDepthを正としてchildCountを再構築します。", "After exclusion, childCount will be rebuilt using the remaining treeDepth values."),
        Dynamic("hod.prune.rebuild_no_root", "先頭パーツがルートではありません。", "The first part is not a root."),
        Dynamic("hod.prune.rebuild_unconnectable", "パーツ[{0}]を親階層へ接続できません（treeDepth={1}）。", "Part[{0}] cannot be connected to the parent hierarchy (treeDepth={1})."),
        Dynamic("hod.prune.rebuild_no_parent", "パーツ[{0}]の親候補がありません。", "Part[{0}] has no parent candidate."),
        Dynamic("hod.prune.unavailable_summary", "安全に除外できる不整合パーツはありません。", "There are no inconsistent parts that can be safely excluded."),

        Dynamic("hod.manual.no_structure_parts", "構造HODのパーツ情報がありません。", "The structure HOD has no part information."),
        Dynamic("hod.manual.part_label", "[{0}] {1}", "[{0}] {1}"),
        Dynamic("hod.manual.root_assignment", "{0}（ルート固定）", "{0} (root fixed)"),
        Dynamic("hod.manual.unconnected_assignment", "{0} → 未接続", "{0} -> Unconnected"),
        Dynamic("hod.manual.parent_assignment", "{0} → {1}", "{0} -> {1}"),
        Dynamic("hod.manual.root_immutable", "ルートパーツは固定されているため、親を変更できません。", "The root part is fixed and its parent cannot be changed."),
        Dynamic("hod.manual.parent_must_precede", "親には、このパーツより前に並んでいるパーツだけを指定できます。", "Only a part that appears before this part can be selected as its parent."),
        Dynamic("hod.manual.unassigned_parts", "親が未設定のパーツが{0}個あります。", "{0} parts have no parent assigned."),
        Dynamic("hod.manual.invalid_parent", "パーツ[{0}]の親指定が不正です。", "Part[{0}] has an invalid parent assignment."),
        Dynamic("hod.manual.not_all_connected", "すべてのパーツをルートへ接続できません。", "Not all parts can be connected to the root."),
        Dynamic("hod.manual.validation_failed", "手動指定から有効なパーツ階層を構築できませんでした。\n{0}", "A valid part hierarchy could not be built from the manual assignments.\n{0}"),
        Dynamic("hod.manual.structure_changed", "構造HODが手動修復の開始後に変更されています。", "The structure HOD changed after manual repair began."),
        Dynamic("hod.manual.structure_part_changed", "構造HODのパーツ[{0}]が手動修復の開始後に変更されています。", "Structure HOD part[{0}] changed after manual repair began."),
        Dynamic("hod.manual.frame_count_changed", "フレーム数が手動修復の開始後に変更されています。", "The frame count changed after manual repair began."),
        Dynamic("hod.manual.frame_changed", "アニメーション[{0}] フレーム[{1}]が手動修復の開始後に変更されています。", "Animation[{0}] frame[{1}] changed after manual repair began."),
        Dynamic("hod.manual.frame_part_changed", "アニメーション[{0}] フレーム[{1}]のパーツ[{2}]が手動修復の開始後に変更されています。", "Animation[{0}] frame[{1}] part[{2}] changed after manual repair began."),

        Dynamic("hod.ui.repair_apply_failed", "HOD階層を安全に修復できなかったため、読み込みを中止しました。\n{0}", "Loading was canceled because the HOD hierarchy could not be repaired safely.\n{0}"),
        Dynamic("hod.ui.legacy_unrepaired_warning", "旧ANIのHODパーツ階層は自動修復せず読み込みました。パーツの追加・削除時に、安全な正本を確定できる場合だけ正規化確認を表示します。\n{0}", "The legacy ANI was loaded without automatically repairing its HOD part hierarchy. When adding or removing parts, a normalization confirmation is shown only if a safe authoritative hierarchy can be determined.\n{0}"),
        Dynamic("hod.ui.prune_apply_failed", "不整合パーツを安全に除外できなかったため、読み込みを中止しました。\n{0}", "Loading was canceled because the inconsistent parts could not be excluded safely.\n{0}"),
        Dynamic("hod.ui.inconsistent_header", "HODのパーツ階層に不整合があります。\n\n", "The HOD part hierarchy contains inconsistencies.\n\n"),
        Dynamic("hod.ui.repair_preview", "\n\n修復予定:\n{0}", "\n\nPlanned repair:\n{0}"),
        Dynamic("hod.ui.repair_unavailable", "\n\n自動修復を適用できません:\n{0}", "\n\nAutomatic repair cannot be applied:\n{0}"),
        Dynamic("hod.ui.tree_depth_case", "\n\ntreeDepthを正とする場合:\n{0}", "\n\nIf treeDepth is treated as authoritative:\n{0}"),
        Dynamic("hod.ui.tree_depth_unavailable", "\n\ntreeDepthを正とする修復を適用できません:\n{0}", "\n\nRepair using treeDepth cannot be applied:\n{0}"),
        Dynamic("hod.ui.child_count_case", "\n\nchildCountを正とする場合:\n{0}", "\n\nIf childCount is treated as authoritative:\n{0}"),
        Dynamic("hod.ui.child_count_unavailable", "\n\nchildCountを正とする修復を適用できません:\n{0}", "\n\nRepair using childCount cannot be applied:\n{0}"),
        Dynamic("hod.ui.manual_available", "\n\n手動修復では各パーツの親を指定し、構造HODと全アニメーションフレームを同じ順序へ再構築します。", "\n\nManual repair lets you assign each part's parent and rebuild the structure HOD and all animation frames in the same order."),
        Dynamic("hod.ui.manual_unavailable", "\n\n手動修復を開始できません:\n{0}", "\n\nManual repair cannot be started:\n{0}"),
        Dynamic("hod.ui.prune_candidate", "\n\n除外候補:\n{0}", "\n\nExclusion candidate:\n{0}"),
        Dynamic("hod.ui.prune_unavailable", "\n\n不整合パーツを除外できません:\n{0}", "\n\nInconsistent parts cannot be excluded:\n{0}"),
        Dynamic("hod.ui.memory_only", "\n\n変更はメモリ上だけで行い、元ファイルを自動上書きしません。", "\n\nChanges are made in memory only; the original files will not be overwritten automatically."),
        Dynamic("hod.ui.read_only_continue", "\n\n読取専用なら表示を継続できます。", "\n\nYou can continue in read-only mode to view the data."),
        Dynamic("hod.ui.manual_header", "HODパーツ階層の手動修復\n\n", "Manual HOD Part Hierarchy Repair\n\n"),
        Dynamic("hod.ui.manual_instructions", "修正するパーツを選び、その親パーツを指定してください。", "Select a part to repair and specify its parent."),
        Dynamic("hod.ui.manual_root", "ルートはパーツ[0]に固定されます。\n", "The root is fixed to Part[0].\n"),
        Dynamic("hod.ui.manual_unassigned", "未接続: {0} / {1}\n\n", "Unconnected: {0} / {1}\n\n"),
        Dynamic("hod.ui.current_connections", "現在の接続:", "Current connections:"),
        Dynamic("hod.ui.manual_apply_failed", "手動修復を適用できません。\n\n{0}", "Manual repair cannot be applied.\n\n{0}"),
        Dynamic("hod.ui.parent_selection", "{0} の親パーツを選択してください。\n循環を防ぐため、現在より前に並ぶパーツだけを選択できます。", "Select the parent part for {0}.\nOnly parts listed before it can be selected to prevent cycles."),
        Dynamic("hod.ui.parent_set_failed", "親パーツを設定できません。\n\n{0}", "The parent part cannot be set.\n\n{0}"),

        Dynamic("hod.legacy_edit.not_legacy", "旧ANIとして読み込まれたデータではありません。", "The data was not loaded as a legacy ANI."),
        Dynamic("hod.legacy_edit.authority_unavailable", "選択した階層情報を正として旧ANIを安全に編集できません。", "The legacy ANI cannot be edited safely using the selected hierarchy representation."),
        Dynamic("hod.legacy_edit.apply_failed", "旧ANIの階層を構造編集用に準備できませんでした。\n{0}", "The legacy ANI hierarchy could not be prepared for structural editing.\n{0}"),
        Dynamic("hod.legacy_edit.frame_index_mismatch", "旧ANIのアニメーション[{0}] フレーム[{1}]は、位置{2}の階層列が構造HODと一致しないため安全に構造編集できません。", "Legacy ANI animation[{0}] frame[{1}] cannot be edited safely because its hierarchy columns at index {2} do not match the structure HOD."),
        Dynamic("hod.legacy_edit.no_plan", "旧ANIの構造編集準備がありません。", "No legacy ANI structural-edit preparation is available."),
        Dynamic("hod.legacy_edit.choose_authority", "旧ANIのtreeDepthとchildCountが別の有効な階層を表しています。パーツの追加・削除を行うには、構造HODと全フレームへ適用する正本を選択してください。元ファイルは保存するまで変更されません。", "The legacy ANI treeDepth and childCount values describe different valid hierarchies. To add or remove parts, select which representation should be authoritative for the structure HOD and every frame. The source file is unchanged until you save."),
        Dynamic("hod.legacy_edit.confirm_normalize", "旧ANIは読込時の階層値を維持しています。パーツの追加・削除を行うには、構造HODと全フレームの階層列を次の内容で正規化します。元ファイルは保存するまで変更されません。正規化して構造編集を続けますか？", "The legacy ANI retains its hierarchy values as loaded. To add or remove parts, the hierarchy columns in the structure HOD and every frame will be normalized as shown below. The source file is unchanged until you save. Normalize and continue structural editing?"),
        Dynamic("hod.legacy_edit.choice_previews", "\n\n[treeDepth]\n{0}\n\n[childCount]\n{1}", "\n\n[treeDepth]\n{0}\n\n[childCount]\n{1}"),
        Dynamic("ani.legacy_conversion.prompt", "読み込もうとしているファイルは旧ANI形式です。AN2へ変換して読み込みますか？\n\nOK: 元の旧ANIを変更せず「{0}」へAN2変換コピーを作成して読み込みます。原作で使用するのは先頭200枠までです。追加枠は本エディタ用として末尾に保持します。問題のあるコメント文字は実行を妨げない形に置き換えます。旧ANI固有の未解析末尾データはAN2コピーには含まれません。\nキャンセル: 旧ANIのまま読み込みます。", "The selected file uses the legacy ANI format. Convert it to AN2 before loading?\n\nOK: Create and load an AN2 copy named \"{0}\" without changing the original legacy ANI. The original game uses up to the first 200 slots; extra slots are retained at the end for this editor. Problematic comment characters are made safe for execution. Unparsed legacy-only trailing data is not included in the AN2 copy.\nCancel: Load the legacy ANI unchanged."),
        Dynamic("ani.legacy_conversion.failed", "旧ANIをAN2へ変換できなかったため、読み込みを中止しました。元の旧ANIは変更されていません。\n{0}", "The legacy ANI could not be converted to AN2, so loading was stopped. The original legacy ANI was not changed.\n{0}"),
        Dynamic("ani.legacy_conversion.completed", "旧ANIをAN2へ変換し、変換後ファイルを読み込みました。\n保存先: {0}\n元の旧ANIは変更されていません。\n原作で使用する枠数: {1}（最大200）\n追加保持枠数: {2}（原作では使用しません）", "The legacy ANI was converted to AN2 and the converted file was loaded.\nSaved to: {0}\nThe original legacy ANI was not changed.\nSlots used by the original game: {1} (maximum 200)\nExtra slots retained for this editor: {2} (not used by the original game)")
    };


    [MenuItem("Tools/WindomXP/Localization/日英Localizationを構築")]
    public static void BuildJapaneseEnglishLocalization()
    {
        EnsureFolders();
        Translation[] fixedTranslations = LoadFixedTranslations();

        LocalizationSettings settings = EnsureLocalizationSettings();
        Locale japanese = EnsureLocale("ja", SystemLanguage.Japanese, "Japanese (ja)");
        Locale english = EnsureLocale("en", SystemLanguage.English, "English (en)");
        LocalizationSettings.ProjectLocale = japanese;
        EditorUtility.SetDirty(settings);

        StringTableCollection collection = LocalizationEditorSettings.GetStringTableCollection(UILocalization.TableName);
        if (collection == null)
        {
            collection = LocalizationEditorSettings.CreateStringTableCollection(
                UILocalization.TableName,
                TableRoot,
                new List<Locale> { japanese, english });
        }

        StringTable japaneseTable = collection.GetTable(japanese.Identifier) as StringTable;
        StringTable englishTable = collection.GetTable(english.Identifier) as StringTable;
        AddOrUpdateEntries(japaneseTable, englishTable, fixedTranslations);
        AddOrUpdateEntries(japaneseTable, englishTable, DynamicTranslations);
        LocalizationEditorSettings.SetPreloadTableFlag(japaneseTable, true);
        LocalizationEditorSettings.SetPreloadTableFlag(englishTable, true);

        Scene scene = OpenTargetScene();
        int localizedCount = AttachFixedTextLocalizers(scene, fixedTranslations);
        CreateLanguageSelector(scene, fixedTranslations);
        int unmatchedCount = WriteUnmatchedSceneTexts(scene, fixedTranslations);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"[LocalizationProjectSetup] 日英Localizationを構築しました。固定UI: {localizedCount}件 / 未登録: {unmatchedCount}件 → {UnmatchedTextsPath}");
    }

    [MenuItem("Tools/WindomXP/Localization/未登録Fixed文言を書き出す")]
    public static void ExportUnmatchedFixedTexts()
    {
        EnsureFolders();
        Translation[] fixedTranslations = LoadFixedTranslations();
        Scene scene = OpenTargetScene();
        int unmatchedCount = WriteUnmatchedSceneTexts(scene, fixedTranslations);
        AssetDatabase.Refresh();
        Debug.Log(
            $"[LocalizationProjectSetup] 未登録Fixed文言を書き出しました: {unmatchedCount}件 → {UnmatchedTextsPath}");
        EditorUtility.RevealInFinder(UnmatchedTextsPath);
    }

    static Translation[] LoadFixedTranslations()
    {
        string absolutePath = ToAbsoluteAssetPath(FixedDictionaryPath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                "Fixed辞書が見つかりません。\n" +
                FixedDictionaryPath + " を作成し、Japanese<TAB>English 形式で追記してください。");
        }

        var translations = new List<Translation>();
        var seenKeys = new HashSet<string>();
        string[] lines = File.ReadAllLines(absolutePath, Encoding.UTF8);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            int tab = line.IndexOf('\t');
            if (tab < 0)
            {
                Debug.LogWarning($"[LocalizationProjectSetup] Fixed辞書 {i + 1}行目にTABがありません: {line}");
                continue;
            }

            string japanese = UnescapeField(line.Substring(0, tab));
            string english = UnescapeField(line.Substring(tab + 1));
            if (string.IsNullOrWhiteSpace(japanese))
            {
                Debug.LogWarning($"[LocalizationProjectSetup] Fixed辞書 {i + 1}行目の日本語が空です。");
                continue;
            }

            Translation translation = Fixed(japanese, english ?? string.Empty);
            if (!seenKeys.Add(translation.Key))
            {
                Debug.LogWarning($"[LocalizationProjectSetup] Fixed辞書でキー重複のため上書きします: {japanese}");
                translations.RemoveAll(entry => entry.Key == translation.Key);
            }

            translations.Add(translation);
        }

        if (translations.Count == 0)
            throw new InvalidOperationException("Fixed辞書に有効な行がありません: " + FixedDictionaryPath);

        return translations.ToArray();
    }

    static int WriteUnmatchedSceneTexts(Scene scene, Translation[] fixedTranslations)
    {
        var unmatched = new SortedDictionary<string, (string Path, string Text)>(StringComparer.Ordinal);
        int scannedCount = 0;
        foreach (SceneTextEntry entry in EnumerateSceneTexts(scene))
        {
            scannedCount++;
            if (IsIgnorableText(entry.Text))
                continue;

            if (FindFixedTranslation(fixedTranslations, entry.Text) != null)
                continue;

            string key = Normalize(entry.Text);
            if (unmatched.ContainsKey(key))
                continue;

            unmatched[key] = (entry.HierarchyPath, entry.Text);
        }

        var lines = new List<string>
        {
            "# Unmatched Text / TMP_Text values in the target scene (not found in FixedTranslations.tsv)",
            "# Format: HierarchyPath<TAB>Japanese<TAB>English",
            "# Fill English, then copy Japanese/English columns into FixedTranslations.tsv",
            "# Scanned components: " + scannedCount,
            "# Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            "",
        };

        foreach (KeyValuePair<string, (string Path, string Text)> pair in unmatched)
        {
            lines.Add(EscapeField(pair.Value.Path) + "\t" + EscapeField(pair.Value.Text) + "\t");
        }

        string absolutePath = ToAbsoluteAssetPath(UnmatchedTextsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
        File.WriteAllText(absolutePath, string.Join("\n", lines) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Debug.Log(
            $"[LocalizationProjectSetup] シーン文言走査: 対象コンポーネント {scannedCount}件 / 未登録 {unmatched.Count}件");
        return unmatched.Count;
    }

    static void AddOrUpdateEntries(StringTable japaneseTable, StringTable englishTable, IEnumerable<Translation> translations)
    {
        foreach (Translation translation in translations)
        {
            StringTableEntry japaneseEntry = japaneseTable.GetEntry(translation.Key) ?? japaneseTable.AddEntry(translation.Key, translation.Japanese);
            StringTableEntry englishEntry = englishTable.GetEntry(translation.Key) ?? englishTable.AddEntry(translation.Key, translation.English);
            japaneseEntry.Value = translation.Japanese;
            englishEntry.Value = translation.English;
        }

        EditorUtility.SetDirty(japaneseTable);
        EditorUtility.SetDirty(englishTable);
        EditorUtility.SetDirty(japaneseTable.SharedData);
    }

    static int AttachFixedTextLocalizers(Scene scene, Translation[] fixedTranslations)
    {
        int count = 0;
        foreach (SceneTextEntry entry in EnumerateSceneTexts(scene))
        {
            if (IsIgnorableText(entry.Text))
                continue;

            Translation translation = FindFixedTranslation(fixedTranslations, entry.Text);
            if (translation == null)
                continue;

            if (entry.LegacyText != null)
            {
                UILocalizedText localizer = entry.LegacyText.GetComponent<UILocalizedText>();
                if (localizer == null)
                    localizer = Undo.AddComponent<UILocalizedText>(entry.LegacyText.gameObject);

                SerializedObject serializedLocalizer = new SerializedObject(localizer);
                serializedLocalizer.FindProperty("entryKey").stringValue = translation.Key;
                serializedLocalizer.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(localizer);
                count++;
                continue;
            }

            if (entry.TmpText != null)
            {
                UILocalizedTmpText localizer = entry.TmpText.GetComponent<UILocalizedTmpText>();
                if (localizer == null)
                    localizer = Undo.AddComponent<UILocalizedTmpText>(entry.TmpText.gameObject);

                SerializedObject serializedLocalizer = new SerializedObject(localizer);
                serializedLocalizer.FindProperty("entryKey").stringValue = translation.Key;
                serializedLocalizer.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(localizer);
                count++;
            }
        }

        return count;
    }

    readonly struct SceneTextEntry
    {
        public readonly string Text;
        public readonly string HierarchyPath;
        public readonly Text LegacyText;
        public readonly TMP_Text TmpText;

        public SceneTextEntry(string text, string hierarchyPath, Text legacyText, TMP_Text tmpText)
        {
            Text = text;
            HierarchyPath = hierarchyPath;
            LegacyText = legacyText;
            TmpText = tmpText;
        }
    }

    static IEnumerable<SceneTextEntry> EnumerateSceneTexts(Scene scene)
    {
        foreach (Text textComponent in Resources.FindObjectsOfTypeAll<Text>())
        {
            if (!IsSceneObject(textComponent, scene))
                continue;

            yield return new SceneTextEntry(
                textComponent.text ?? string.Empty,
                GetHierarchyPath(textComponent.transform),
                textComponent,
                null);
        }

        foreach (TMP_Text tmpText in Resources.FindObjectsOfTypeAll<TMP_Text>())
        {
            if (!IsSceneObject(tmpText, scene))
                continue;

            yield return new SceneTextEntry(
                tmpText.text ?? string.Empty,
                GetHierarchyPath(tmpText.transform),
                null,
                tmpText);
        }
    }

    static bool IsSceneObject(Component component, Scene scene)
    {
        if (component == null)
            return false;

        GameObject gameObject = component.gameObject;
        if (gameObject == null || gameObject.scene != scene)
            return false;

        // Prefab assets and other non-scene objects can appear in FindObjectsOfTypeAll.
        if (EditorUtility.IsPersistent(gameObject))
            return false;

        return true;
    }

    static bool IsIgnorableText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        string trimmed = value.Replace("\u200B", string.Empty).Trim();
        return trimmed.Length == 0;
    }

    static Translation FindFixedTranslation(Translation[] fixedTranslations, string value)
    {
        foreach (Translation translation in fixedTranslations)
        {
            if (translation.Matches(value))
                return translation;
        }

        return null;
    }

    static void CreateLanguageSelector(Scene scene, Translation[] fixedTranslations)
    {
        Transform overlay = FindInScene(scene, "UI_Overlay");
        Transform prefPanel = overlay == null ? null : overlay.Find("PrefPanel");
        if (prefPanel == null && overlay != null)
            prefPanel = overlay.Find("環境設定ウインドウ");
        if (prefPanel == null)
        {
            Debug.LogWarning(
                "[LocalizationProjectSetup] 環境設定ウインドウが見つからないため、言語セレクタの作成をスキップしました。");
            return;
        }

        Transform existingDropdown = prefPanel.Find("LanguageDropdown");
        GameObject dropdownObject;
        if (existingDropdown == null)
        {
            Transform source = overlay.Find("メニューバー/モードセレクター/モード選択メニュー");
            if (source == null)
                throw new InvalidOperationException("複製元Dropdownが見つかりません。");

            dropdownObject = UnityEngine.Object.Instantiate(source.gameObject, prefPanel, false);
            dropdownObject.name = "LanguageDropdown";
            Undo.RegisterCreatedObjectUndo(dropdownObject, "Create language dropdown");
        }
        else
        {
            dropdownObject = existingDropdown.gameObject;
        }

        RectTransform dropdownRect = dropdownObject.GetComponent<RectTransform>();
        dropdownRect.anchorMin = new Vector2(0f, 1f);
        dropdownRect.anchorMax = new Vector2(0f, 1f);
        dropdownRect.pivot = new Vector2(0.5f, 0.5f);
        dropdownRect.anchoredPosition = new Vector2(390f, -160f);
        dropdownRect.sizeDelta = new Vector2(230f, 30f);

        foreach (UILocalizedText inheritedLocalizer in dropdownObject.GetComponentsInChildren<UILocalizedText>(true))
            Undo.DestroyObjectImmediate(inheritedLocalizer);

        Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();
        dropdown.onValueChanged = new Dropdown.DropdownEvent();
        dropdown.ClearOptions();
        dropdown.AddOptions(new List<string> { "日本語", "English" });
        dropdown.SetValueWithoutNotify(0);
        if (dropdownObject.GetComponent<UILanguageSelector>() == null)
            Undo.AddComponent<UILanguageSelector>(dropdownObject);

        Transform existingLabel = prefPanel.Find("LanguageLabel");
        GameObject labelObject;
        if (existingLabel == null)
        {
            Text sourceLabel = null;
            foreach (Text text in prefPanel.GetComponentsInChildren<Text>(true))
            {
                if (Normalize(text.text) == "ROBOフォルダーのディレクトリパス")
                {
                    sourceLabel = text;
                    break;
                }
            }

            if (sourceLabel == null)
                throw new InvalidOperationException("複製元ラベルが見つかりません。");

            labelObject = UnityEngine.Object.Instantiate(sourceLabel.gameObject, prefPanel, false);
            labelObject.name = "LanguageLabel";
            Undo.RegisterCreatedObjectUndo(labelObject, "Create language label");
        }
        else
        {
            labelObject = existingLabel.gameObject;
        }

        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = new Vector2(165f, -160f);
        labelRect.sizeDelta = new Vector2(277f, 26f);

        Text label = labelObject.GetComponent<Text>();
        label.text = "言語";
        Translation translation = FindFixedTranslation(fixedTranslations, "言語");
        if (translation == null)
            throw new InvalidOperationException("Fixed辞書に「言語」がありません: " + FixedDictionaryPath);

        UILocalizedText localizer = labelObject.GetComponent<UILocalizedText>();
        if (localizer == null)
            localizer = Undo.AddComponent<UILocalizedText>(labelObject);

        SerializedObject serializedLocalizer = new SerializedObject(localizer);
        serializedLocalizer.FindProperty("entryKey").stringValue = translation.Key;
        serializedLocalizer.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(localizer);
    }

    static Scene OpenTargetScene()
    {
        string scenePath = ResolveScenePath();
        return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
    }

    static string ResolveScenePath()
    {
        foreach (string candidate in ScenePathCandidates)
        {
            if (File.Exists(ToAbsoluteAssetPath(candidate)))
                return candidate;
        }

        throw new FileNotFoundException(
            "編集シーンが見つかりません。候補: " + string.Join(", ", ScenePathCandidates));
    }

    static Transform FindInScene(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
                return root.transform;
        }

        return null;
    }

    static string GetHierarchyPath(Transform transform)
    {
        var parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    static Locale EnsureLocale(string code, SystemLanguage language, string assetName)
    {
        Locale locale = LocalizationEditorSettings.GetLocale(code);
        if (locale != null)
            return locale;

        locale = Locale.CreateLocale(language);
        AssetDatabase.CreateAsset(locale, LocaleRoot + "/" + assetName + ".asset");
        LocalizationEditorSettings.AddLocale(locale);
        return locale;
    }

    static void EnsureFolders()
    {
        EnsureFolder(LocalizationRoot);
        EnsureFolder(LocaleRoot);
        EnsureFolder(TableRoot);
        EnsureFolder(SourceRoot);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        string name = path.Substring(separator + 1);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    static LocalizationSettings EnsureLocalizationSettings()
    {
        const string settingsPath = LocalizationRoot + "/Localization Settings.asset";
        LocalizationSettings settings = LocalizationEditorSettings.ActiveLocalizationSettings;
        if (settings == null)
        {
            settings = AssetDatabase.LoadAssetAtPath<LocalizationSettings>(settingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<LocalizationSettings>();
                settings.name = "WindomXP Localization Settings";
                AssetDatabase.CreateAsset(settings, settingsPath);
            }

            LocalizationEditorSettings.ActiveLocalizationSettings = settings;
        }

        return settings;
    }

    static string ToAbsoluteAssetPath(string assetPath)
    {
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    static string Normalize(string value)
    {
        return (value ?? string.Empty)
            .Replace("１", "1")
            .Replace("２", "2")
            .TrimEnd('\r', '\n');
    }

    static string EscapeField(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    static string UnescapeField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c != '\\' || i + 1 >= value.Length)
            {
                builder.Append(c);
                continue;
            }

            char next = value[++i];
            switch (next)
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                default:
                    builder.Append(next);
                    break;
            }
        }

        return builder.ToString();
    }
}
