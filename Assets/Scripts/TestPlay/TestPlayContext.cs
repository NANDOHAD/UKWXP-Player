using UnityEngine;

/// <summary>Explicit, borrowed dependencies for one mech. No scene lookup is permitted.</summary>
public sealed class TestPlayContext
{
    public RoboStructure Robo;
    public SptRuntimeData Spt;
    public TestPlayTargetDummy Target;
    public TestPlayCameraController Camera;
    public bool ShowHud = true;
    /// <summary>Scene-placed HUD canvas for the player mech. Null falls back to runtime generation.</summary>
    public GameObject HudCanvas;
}
