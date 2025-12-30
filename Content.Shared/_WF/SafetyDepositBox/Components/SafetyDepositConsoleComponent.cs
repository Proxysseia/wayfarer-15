using Content.Shared.Containers.ItemSlots;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._WF.SafetyDepositBox.Components;

/// <summary>
/// Console for purchasing, depositing, and withdrawing safety deposit boxes.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SafetyDepositConsoleComponent : Component
{
    /// <summary>
    /// Cost to purchase a small safety deposit box.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int SmallBoxCost = 250000;

    /// <summary>
    /// Cost to purchase a medium safety deposit box.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int MediumBoxCost = 750000;

    /// <summary>
    /// Cost to purchase a large safety deposit box.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public int LargeBoxCost = 1250000;

    /// <summary>
    /// Slot for depositing/withdrawing boxes.
    /// </summary>
    [DataField]
    public ItemSlot BoxSlot = new();

    public static string BoxSlotId = "safety-deposit-console-boxSlot";

    [DataField]
    public SoundSpecifier ErrorSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_sigh.ogg");

    [DataField]
    public SoundSpecifier ConfirmSound =
        new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");
}
