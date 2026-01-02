using Content.Shared.Radio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Shared.Radio.Components;

/// <summary>
/// Tracks which radio channels are currently disabled (muted) on a headset or radio device.
/// Disabled channels won't receive messages.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class DisabledRadioChannelsComponent : Component
{
    /// <summary>
    /// Set of channel IDs that are currently disabled.
    /// </summary>
    [DataField("disabledChannels", customTypeSerializer: typeof(PrototypeIdHashSetSerializer<RadioChannelPrototype>))]
    [AutoNetworkedField]
    public HashSet<string> DisabledChannels = new();

    /// <summary>
    /// Time when the last reminder was sent to the player.
    /// </summary>
    [DataField("lastReminderTime")]
    public TimeSpan LastReminderTime = TimeSpan.Zero;

    /// <summary>
    /// How often to remind the player about disabled channels (10 minutes).
    /// </summary>
    [DataField("reminderInterval")]
    public TimeSpan ReminderInterval = TimeSpan.FromMinutes(15);
}
