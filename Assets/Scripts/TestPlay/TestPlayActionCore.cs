using System;

public enum TestPlayWeaponMode
{
    Gun,
    Sword
}

public struct TestPlayActionSelection
{
    public int requestedActionId;
    public int logicalActionId;
    public int poseActionId;
    public int scriptActionId;
    public TestPlayWeaponMode weaponMode;
    public int primaryChannel;
    public int secondaryChannel;

    public bool UsesDualChannels => secondaryChannel >= 0;
}

/// <summary>
/// Resolves the three action identities used by the original runtime:
/// logical gameplay action, displayed ANI pose, and ANI script source.
/// This class deliberately has no scene or input dependencies.
/// </summary>
public static class TestPlayActionCore
{
    public static TestPlayActionSelection Resolve(
        int requestedActionId,
        TestPlayWeaponMode weaponMode,
        Func<int, bool> hasUsableAction,
        Func<int, bool> hasScript)
    {
        int poseActionId = requestedActionId;
        if (weaponMode == TestPlayWeaponMode.Sword && requestedActionId >= 0 && requestedActionId < 50)
        {
            int swordActionId = requestedActionId + 50;
            if (hasUsableAction != null && hasUsableAction(swordActionId))
                poseActionId = swordActionId;
        }

        int logicalActionId = GetLogicalActionId(poseActionId);
        int scriptActionId = poseActionId;
        if (poseActionId >= 50 && poseActionId < 100 &&
            !HasScript(hasScript, poseActionId) &&
            HasScript(hasScript, logicalActionId))
        {
            scriptActionId = logicalActionId;
        }

        bool dualChannels = logicalActionId >= 0 && logicalActionId < 50;
        return new TestPlayActionSelection
        {
            requestedActionId = requestedActionId,
            logicalActionId = logicalActionId,
            poseActionId = poseActionId,
            scriptActionId = scriptActionId,
            weaponMode = weaponMode,
            primaryChannel = IsMeleeAction(poseActionId) ? 0 : (dualChannels ? 0 : 1),
            secondaryChannel = dualChannels ? 1 : -1
        };
    }

    public static int GetLogicalActionId(int actionId)
    {
        return actionId >= 50 && actionId < 100 ? actionId - 50 : actionId;
    }

    public static bool UsesOriginalDualAniChannels(int actionId)
    {
        int logicalActionId = GetLogicalActionId(actionId);
        return logicalActionId >= 0 && logicalActionId < 50;
    }

    static bool HasScript(Func<int, bool> hasScript, int actionId)
    {
        return hasScript != null && hasScript(actionId);
    }

    static bool IsMeleeAction(int actionId)
    {
        return actionId >= 130 && actionId <= 155;
    }
}
