using Robust.Shared.GameStates;

namespace Content.Shared.Atmos.Rotting;

/// <summary>
/// Entities inside this container will decay slower (hunger, perishable, etc.)
/// Useful for cryostorage units and similar stasis containers.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SlowDecayContainerComponent : Component
{
    /// <summary>
    /// The multiplier for decay rates. 0.15 means 85% slower (15% of normal speed).
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float DecayModifier = 0.15f;
}
