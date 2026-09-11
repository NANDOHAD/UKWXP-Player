using System;

public enum TestPlayRuntimeEventType
{
    Command,
    AttackProfileChanged,
    AttackHit,
    BurnerOutput,
    WeaponSpawned,
    EffectSpawned,
    Sound,
    Voice,
    CameraEffect,
    Warning
}

[Serializable]
public class TestPlayAttackProfile
{
    public int power;
    public int down;
    public float force;
    public float forceY;

    public void Reset()
    {
        power = 0;
        down = 0;
        force = 0f;
        forceY = 0f;
    }
}

public struct TestPlayRuntimeEvent
{
    public TestPlayRuntimeEventType type;
    public string command;
    public string symbol;
    public int actionIndex;
    public int scriptIndex;
    public int tick;
    public int intValue;
    public float floatValue;
    public TestPlayScriptValue[] arguments;
}
