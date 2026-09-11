using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>A value snapshot, including the planar camera basis used by this tick.</summary>
[Serializable]
public struct TestPlaySessionInput
{
    public TestPlayGoldenInputFrame held;
    public TestPlayGoldenInputFrame pressed;
    public bool hasMovementBasis;
    public Vector3 movementForward;

    public TestPlayGoldenInputFrame Effective => Merge(held, pressed);

    internal static TestPlayGoldenInputFrame Merge(TestPlayGoldenInputFrame a, TestPlayGoldenInputFrame b)
    {
        return new TestPlayGoldenInputFrame {
            direction = a.direction != 0 ? a.direction : b.direction,
            rise = a.rise || b.rise, shot = a.shot || b.shot, melee = a.melee || b.melee,
            guard = a.guard || b.guard, lockTarget = a.lockTarget || b.lockTarget,
            special1 = a.special1 || b.special1, special2 = a.special2 || b.special2,
            special3 = a.special3 || b.special3
        };
    }
}

public interface ITestPlayInputProvider
{
    TestPlaySessionInput Read(int mechId, int tick);
}

/// <summary>Render-frame samples latch short presses until the first simulation tick.</summary>
public sealed class TestPlayBufferedInputProvider : ITestPlayInputProvider
{
    TestPlaySessionInput pending;
    int lastTick;
    readonly int mechId;
    public TestPlayBufferedInputProvider(int mechId) { this.mechId = mechId; }

    public void Submit(TestPlaySessionInput sample)
    {
        sample.pressed = TestPlaySessionInput.Merge(sample.pressed, pending.pressed);
        if (sample.held.direction != 0 && sample.held.direction != pending.held.direction)
            sample.pressed.direction = sample.held.direction;
        pending = sample;
    }

    public TestPlaySessionInput Read(int id, int tick)
    {
        if (id != mechId || tick != lastTick + 1)
            throw new InvalidOperationException("入力の機体ID/tickが一致しません。");
        var result = pending;
        pending.pressed = default;
        lastTick = tick;
        return result;
    }
}

public sealed class TestPlayIdleInputProvider : ITestPlayInputProvider
{
    public TestPlaySessionInput Read(int mechId, int tick) => default;
}

/// <summary>Missing replay ticks are errors; zero input must be recorded explicitly.</summary>
public sealed class TestPlayReplayInputProvider : ITestPlayInputProvider
{
    readonly int mechId;
    readonly TestPlaySessionInput[] frames;
    int lastTick;
    public TestPlayReplayInputProvider(int mechId, IEnumerable<TestPlaySessionInput> frames)
    {
        this.mechId = mechId;
        this.frames = new List<TestPlaySessionInput>(frames).ToArray();
    }
    public TestPlaySessionInput Read(int id, int tick)
    {
        if (id != mechId || tick != lastTick + 1 || tick > frames.Length)
            throw new InvalidOperationException("replay入力が欠落または重複しています: " + id + "/" + tick);
        lastTick = tick;
        return frames[tick - 1];
    }
}

public static class TestPlayKeyboardInput
{
    public static TestPlaySessionInput Sample(TestPlayCameraController camera)
    {
        var value = new TestPlaySessionInput();
        int x = (Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.LeftArrow) ? 1 : 0);
        int y = (Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.DownArrow) ? 1 : 0);
        value.held.direction = x == 0 && y == 0 ? 0 : 5 + x + y * 3;
        value.held.rise = Input.GetKey(KeyCode.Z); value.pressed.rise = Input.GetKeyDown(KeyCode.Z);
        value.held.shot = Input.GetKey(KeyCode.X); value.pressed.shot = Input.GetKeyDown(KeyCode.X);
        value.held.melee = Input.GetKey(KeyCode.C); value.pressed.melee = Input.GetKeyDown(KeyCode.C);
        value.held.guard = Input.GetKey(KeyCode.V); value.pressed.guard = Input.GetKeyDown(KeyCode.V);
        value.held.lockTarget = Input.GetKey(KeyCode.S); value.pressed.lockTarget = Input.GetKeyDown(KeyCode.S);
        value.held.special1 = Input.GetKey(KeyCode.A); value.pressed.special1 = Input.GetKeyDown(KeyCode.A);
        value.held.special2 = Input.GetKey(KeyCode.D); value.pressed.special2 = Input.GetKeyDown(KeyCode.D);
        value.held.special3 = Input.GetKey(KeyCode.F); value.pressed.special3 = Input.GetKeyDown(KeyCode.F);
        if (camera != null && camera.TryGetPlanarMovementBasis(out var forward, out _))
        {
            value.hasMovementBasis = true;
            value.movementForward = forward;
        }
        return value;
    }
}
