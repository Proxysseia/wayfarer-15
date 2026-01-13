using Content.Server._WF.Shuttles.Components;
using Content.Server._WF.Shuttles.Systems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Content.Shared.Verbs;
using Robust.Shared.Map;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private readonly AutopilotSystem _autopilot = default!;

    private void InitializeAutopilot()
    {
        SubscribeLocalEvent<ShuttleConsoleComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAutopilotVerb);
    }

    private void OnGetAutopilotVerb(EntityUid uid, ShuttleConsoleComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanInteract || !args.CanAccess)
            return;

        // Get the shuttle this console controls
        var getShuttleEv = new ConsoleShuttleEvent
        {
            Console = uid,
        };
        RaiseLocalEvent(uid, ref getShuttleEv);
        var shuttleUid = getShuttleEv.Console;

        if (shuttleUid == null)
            return;

        if (!TryComp<TransformComponent>(shuttleUid, out var shuttleXform))
            return;

        var shuttleGridUid = shuttleXform.GridUid;
        if (shuttleGridUid == null)
            return;

        // Check if autopilot component exists and is enabled
        var hasAutopilot = TryComp<AutopilotComponent>(shuttleGridUid.Value, out var autopilotComp);
        var isEnabled = hasAutopilot && autopilotComp!.Enabled;

        AlternativeVerb verb = new()
        {
            Act = () => ToggleAutopilot(args.User, uid, shuttleGridUid.Value),
            Text = isEnabled ? Loc.GetString("shuttle-console-autopilot-disable") : Loc.GetString("shuttle-console-autopilot-enable"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/refresh.svg.192dpi.png")),
            Priority = 1,
        };

        args.Verbs.Add(verb);
    }

    private void ToggleAutopilot(EntityUid user, EntityUid consoleUid, EntityUid shuttleUid)
    {
        if (!TryComp<ShuttleConsoleComponent>(consoleUid, out var console))
            return;

        if (!TryComp<ShuttleComponent>(shuttleUid, out var shuttle))
            return;

        // Check if autopilot is currently enabled
        var hasAutopilot = TryComp<AutopilotComponent>(shuttleUid, out var autopilotComp);
        var isEnabled = hasAutopilot && autopilotComp!.Enabled;

        if (isEnabled)
        {
            // Disable autopilot
            _autopilot.ToggleAutopilot(shuttleUid, null);
            _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-disabled"), user, user);
        }
        else
        {
            // Try to get the target from the radar console
            if (!TryComp<RadarConsoleComponent>(consoleUid, out var radarConsole))
            {
                _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-no-target"), user, user);
                return;
            }

            EntityCoordinates? targetCoords = null;

            // Check if a target is set - accessing through query to avoid write access error
            var targetQuery = EntityQueryEnumerator<RadarConsoleComponent>();
            EntityUid? targetEntity = null;
            Vector2? manualTarget = null;
            
            while (targetQuery.MoveNext(out var uid, out var radar))
            {
                if (uid == consoleUid)
                {
                    targetEntity = radar.TargetEntity;
                    manualTarget = radar.Target;
                    break;
                }
            }

            // First try to use entity target
            if (targetEntity != null && targetEntity.Value.IsValid())
            {
                if (TryComp<TransformComponent>(targetEntity.Value, out var targetXform))
                {
                    targetCoords = targetXform.Coordinates;
                }
            }
            // Otherwise try to use manual coordinate target
            else if (manualTarget != null && TryComp<TransformComponent>(consoleUid, out var consoleXform))
            {
                // Convert the map position to entity coordinates
                var mapId = consoleXform.MapID;
                targetCoords = new EntityCoordinates(_mapSystem.GetMap(mapId), manualTarget.Value);
            }

            if (targetCoords == null)
            {
                _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-no-target"), user, user);
                return;
            }

            // Enable autopilot with the target
            if (_autopilot.ToggleAutopilot(shuttleUid, targetCoords.Value))
            {
                _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-enabled"), user, user);
            }
            else
            {
                _popup.PopupEntity(Loc.GetString("shuttle-console-autopilot-failed"), user, user);
            }
        }
    }
}
