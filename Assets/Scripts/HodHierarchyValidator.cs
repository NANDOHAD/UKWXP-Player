using System.Collections.Generic;

public static class HodHierarchyValidator
{
    const int MaxReportedIssues = 8;

    public static bool TryValidate(IList<hod2v0_Part> parts, out string details)
    {
        List<string> issues = new List<string>();
        int issueCount = 0;

        if (parts == null || parts.Count == 0)
        {
            details = UILocalization.Get("hod.validator.no_parts", "パーツ情報がありません。");
            return false;
        }

        for (int i = 0; i < parts.Count; i++)
        {
            hod2v0_Part part = parts[i];
            string partLabel = GetPartLabel(part, i);

            if (part.treeDepth < 0)
                AddIssue(issues, ref issueCount, UILocalization.Get(
                    "hod.validator.tree_depth_negative",
                    "{0}: treeDepthが負の値です（{1}）。",
                    partLabel, part.treeDepth));

            if (i == 0 && part.treeDepth != 0)
                AddIssue(issues, ref issueCount, UILocalization.Get(
                    "hod.validator.first_tree_depth",
                    "{0}: 先頭パーツのtreeDepthは0である必要があります（{1}）。",
                    partLabel, part.treeDepth));

            if (i > 0)
            {
                int previousDepth = parts[i - 1].treeDepth;
                if (part.treeDepth > previousDepth + 1)
                {
                    AddIssue(issues, ref issueCount,
                        UILocalization.Get(
                            "hod.validator.tree_depth_jump",
                            "{0}: treeDepthが直前のパーツから2段以上深くなっています（{1}→{2}）。",
                            partLabel, previousDepth, part.treeDepth));
                }

                if (part.treeDepth == 0)
                    AddIssue(issues, ref issueCount, UILocalization.Get(
                        "hod.validator.additional_root",
                        "{0}: 2個目以降のルートパーツです。",
                        partLabel));
            }

            if (part.treeDepth > 0 && !HasPreviousParent(parts, i, part.treeDepth - 1))
                AddIssue(issues, ref issueCount, UILocalization.Get(
                    "hod.validator.missing_parent",
                    "{0}: treeDepth={1}の親候補が前方にありません。",
                    partLabel, part.treeDepth - 1));

            int actualChildCount = CountDirectChildren(parts, i);
            if (part.childCount != actualChildCount)
            {
                AddIssue(issues, ref issueCount,
                    UILocalization.Get(
                        "hod.validator.child_count_mismatch",
                        "{0}: childCountが一致しません（記録={1}、深度列から算出={2}）。",
                        partLabel, part.childCount, actualChildCount));
            }
        }

        if (issueCount == 0)
        {
            details = "";
            return true;
        }

        details = string.Join("\n", issues);
        if (issueCount > issues.Count)
            details += UILocalization.Get(
                "hod.validator.more_issues",
                "\nほか{0}件の不整合があります。",
                issueCount - issues.Count);
        return false;
    }

    static bool HasPreviousParent(IList<hod2v0_Part> parts, int index, int parentDepth)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (parts[i].treeDepth == parentDepth)
                return true;
        }
        return false;
    }

    static int CountDirectChildren(IList<hod2v0_Part> parts, int parentIndex)
    {
        int parentDepth = parts[parentIndex].treeDepth;
        int childCount = 0;

        for (int i = parentIndex + 1; i < parts.Count; i++)
        {
            int depth = parts[i].treeDepth;
            if (depth <= parentDepth)
                break;
            if (depth == parentDepth + 1)
                childCount++;
        }

        return childCount;
    }

    static string GetPartLabel(hod2v0_Part part, int index)
    {
        string name = string.IsNullOrEmpty(part.name) ? "<名称なし>" : part.name;
        return UILocalization.Get(
            "hod.part.label",
            "パーツ[{0}]「{1}」",
            index,
            name);
    }

    static void AddIssue(List<string> issues, ref int issueCount, string issue)
    {
        issueCount++;
        if (issues.Count < MaxReportedIssues)
            issues.Add(issue);
    }
}
