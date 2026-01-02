using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Radio.Components;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Radio.EntitySystems;

/// <summary>
/// System that handles toggling radio channels on/off for headsets and radios.
/// </summary>
public sealed class DisabledRadioChannelsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EncryptionKeyHolderComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<DisabledRadioChannelsComponent, ExaminedEvent>(OnExamined);
    }

    private void OnGetAlternativeVerbs(EntityUid uid, EncryptionKeyHolderComponent keyHolder, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        // Only show verbs if there are channels available
        if (keyHolder.Channels.Count == 0)
            return;

        // Create a verb for each available channel
        foreach (var channelId in keyHolder.Channels)
        {
            if (!_prototypeManager.TryIndex<RadioChannelPrototype>(channelId, out var channel))
                continue;

            var disabled = EnsureComp<DisabledRadioChannelsComponent>(uid);
            var isDisabled = disabled.DisabledChannels.Contains(channelId);

            var verb = new AlternativeVerb
            {
                Text = Loc.GetString("disabled-radio-channels-verb",
                    ("channel", channel.LocalizedName),
                    ("status", Loc.GetString(isDisabled ? "disabled-radio-channels-status-disabled" : "disabled-radio-channels-status-enabled"))),
                Icon = isDisabled ? null : new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/dot.svg.192dpi.png")),
                Priority = -10, // Lower priority so it's grouped together
                Category = VerbCategory.RadioChannels,
                Act = () => ToggleChannel(uid, channelId, channel, disabled)
            };

            args.Verbs.Add(verb);
        }
    }

    private void ToggleChannel(EntityUid uid, string channelId, RadioChannelPrototype channel, DisabledRadioChannelsComponent? disabled = null)
    {
        if (!Resolve(uid, ref disabled))
            return;

        if (disabled.DisabledChannels.Contains(channelId))
        {
            disabled.DisabledChannels.Remove(channelId);
            _popup.PopupEntity(Loc.GetString("disabled-radio-channels-enabled",
                ("channel", channel.LocalizedName)), uid, uid, PopupType.Medium);
        }
        else
        {
            disabled.DisabledChannels.Add(channelId);
            _popup.PopupEntity(Loc.GetString("disabled-radio-channels-disabled",
                ("channel", channel.LocalizedName)), uid, uid, PopupType.Medium);
        }

        Dirty(uid, disabled);
    }

    private void OnExamined(EntityUid uid, DisabledRadioChannelsComponent component, ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || component.DisabledChannels.Count == 0)
            return;

        var channels = new List<string>();
        foreach (var channelId in component.DisabledChannels)
        {
            if (_prototypeManager.TryIndex<RadioChannelPrototype>(channelId, out var channel))
            {
                channels.Add(FormattedMessage.EscapeText(channel.LocalizedName));
            }
        }

        if (channels.Count > 0)
        {
            args.PushMarkup(Loc.GetString("disabled-radio-channels-examine",
                ("channels", string.Join(", ", channels))));
        }
    }

    /// <summary>
    /// Checks if a specific channel is disabled on an entity.
    /// </summary>
    public bool IsChannelDisabled(EntityUid uid, string channelId, DisabledRadioChannelsComponent? component = null)
    {
        if (!Resolve(uid, ref component, false))
            return false;

        return component.DisabledChannels.Contains(channelId);
    }
}
