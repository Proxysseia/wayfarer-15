using Content.Shared.DeviceLinking;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Server.Weapons.Ranged.Components;

/// <summary>
/// Fires the weapon when signal is received.
/// Supports separate ports for grow and shrink modes.
/// </summary>
[RegisterComponent]
public sealed partial class FireOnSignalComponent : Component
{
    [DataField("growPort", customTypeSerializer: typeof(PrototypeIdSerializer<SinkPortPrototype>))]
    public string GrowPort = "GrowTrigger";

    [DataField("shrinkPort", customTypeSerializer: typeof(PrototypeIdSerializer<SinkPortPrototype>))]
    public string ShrinkPort = "ShrinkTrigger";
}
