using System.Numerics;
using Content.Server._WF.Shuttles.Components;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Chat.Managers;
using Content.Shared.Popups;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Server.Player;
using Robust.Shared.Player;

namespace Content.Server._WF.Shuttles.Systems;

/// <summary>
/// Handles automatic navigation of shuttles to target destinations.
/// </summary>
public sealed class AutopilotSystem : EntitySystem
{
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ThrusterSystem _thruster = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AutopilotComponent, ComponentShutdown>(OnAutopilotShutdown);
    }

    private void OnAutopilotShutdown(EntityUid uid, AutopilotComponent component, ComponentShutdown args)
    {
        component.Enabled = false;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AutopilotComponent, ShuttleComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var autopilot, out var shuttle, out var xform, out var physics))
        {
            if (!autopilot.Enabled || autopilot.TargetCoordinates == null)
                continue;

            if (!shuttle.Enabled)
            {
                autopilot.Enabled = false;
                continue;
            }

            // Get target position
            var targetCoords = autopilot.TargetCoordinates.Value;
            if (!targetCoords.IsValid(EntityManager))
            {
                autopilot.Enabled = false;
                continue;
            }

            var targetPos = targetCoords.ToMapPos(EntityManager, _transform);
            var currentPos = _transform.GetMapCoordinates(uid, xform);
            var direction = targetPos - currentPos.Position;
            var distance = direction.Length();

            // Check if we've arrived
            if (distance <= autopilot.ArrivalDistance)
            {
                autopilot.Enabled = false;
                SendShuttleMessage(uid, "Autopilot: Destination reached");
                
                // Apply brakes
                ApplyBraking(uid, shuttle, physics, xform, frameTime);
                
                // Park the shuttle by setting it to Anchor mode (2.5f dampening)
                shuttle.BodyModifier = 2.5f; // AnchorDampingStrength
                if (shuttle.DampingModifier != 0)
                    shuttle.DampingModifier = shuttle.BodyModifier;
                shuttle.EBrakeActive = false;
                
                continue;
            }

            // Normalize direction
            if (distance > 0.01f)
                direction /= distance;
            else
                continue;

            // Check for obstacles and adjust direction
            var obstacleSpeedMultiplier = 1.0f;
            direction = AvoidObstacles(uid, xform, physics, direction, autopilot, out obstacleSpeedMultiplier);

            // Calculate desired velocity based on distance
            var speedMultiplier = autopilot.SpeedMultiplier * obstacleSpeedMultiplier;
            if (distance < autopilot.SlowdownDistance)
            {
                // Slow down as we approach the target
                speedMultiplier *= Math.Max(0.3f, distance / autopilot.SlowdownDistance);
            }

            var maxVelocity = shuttle.BaseMaxLinearVelocity * speedMultiplier;
            var desiredVelocity = direction * maxVelocity;

            // Calculate thrust direction relative to shuttle orientation
            var shuttleAngle = xform.LocalRotation;
            var localDirection = (-shuttleAngle).RotateVec(desiredVelocity);

            // Apply thrust
            ApplyAutopilotThrust(uid, shuttle, localDirection, xform, physics, frameTime);

            // Handle rotation to face direction of travel
            RotateTowardsTarget(uid, shuttle, xform, physics, direction, frameTime);
        }
    }

    private Vector2 AvoidObstacles(EntityUid uid, TransformComponent xform, PhysicsComponent physics, Vector2 direction, AutopilotComponent autopilot, out float speedMultiplier)
    {
        speedMultiplier = 1.0f;
        
        // Get all nearby grids
        var pos = _transform.GetMapCoordinates(uid, xform);
        var velocity = physics.LinearVelocity;
        var speed = velocity.Length();
        
        // Look ahead based on current velocity (2-5 seconds ahead depending on speed)
        var lookAheadTime = Math.Clamp(speed * 0.3f, 2f, 5f);
        var lookAheadPos = pos.Position + velocity * lookAheadTime;
        
        var grids = new HashSet<EntityUid>();
        _lookup.GetEntitiesInRange(pos.MapId, pos.Position, autopilot.ScanRange, grids);

        Vector2 avoidanceVector = Vector2.Zero;
        var closestObstacleDistance = float.MaxValue;
        var hasObstacle = false;
        var nearbyGridCount = 0; // Track grid density for additional speed reduction

        foreach (var gridUid in grids)
        {
            if (gridUid == uid) // Don't avoid ourselves
                continue;

            if (!TryComp<MapGridComponent>(gridUid, out var gridComp))
                continue;

            if (!TryComp<PhysicsComponent>(gridUid, out var gridPhysics))
                continue;

            // Get the actual AABB of the grid for better collision detection
            var gridXform = Transform(gridUid);
            var gridPos = _transform.GetMapCoordinates(gridUid, gridXform);
            
            // Calculate closest point on grid to our position
            var toGrid = gridPos.Position - pos.Position;
            var distanceToGrid = toGrid.Length();
            
            // Count grids within the obstacle avoidance distance for density calculation
            if (distanceToGrid < autopilot.ObstacleAvoidanceDistance)
                nearbyGridCount++;
            
            // Check if this grid is in our path using dot product
            if (distanceToGrid > 0.01f)
            {
                var normalizedToGrid = toGrid / distanceToGrid;
                var dot = Vector2.Dot(normalizedToGrid, direction);
                
                // Check if obstacle is ahead of us (more lenient threshold)
                if (dot > 0.3f && distanceToGrid < autopilot.ObstacleAvoidanceDistance)
                {
                    hasObstacle = true;
                    closestObstacleDistance = Math.Min(closestObstacleDistance, distanceToGrid);
                    
                    // Calculate avoidance vector - push away from the obstacle
                    var avoidDirection = -normalizedToGrid;
                    
                    // Also add a perpendicular component to go around the obstacle
                    var perpendicular = new Vector2(-toGrid.Y, toGrid.X);
                    if (perpendicular.LengthSquared() > 0.01f)
                    {
                        perpendicular = Vector2.Normalize(perpendicular);
                        
                        // Choose perpendicular direction that's more aligned with our desired direction
                        if (Vector2.Dot(perpendicular, direction) < 0)
                            perpendicular = -perpendicular;
                        
                        avoidDirection = Vector2.Normalize(avoidDirection + perpendicular * 1.5f);
                    }
                    
                    // Weight avoidance by proximity - closer obstacles have more influence
                    var weight = 1.0f - (distanceToGrid / autopilot.ObstacleAvoidanceDistance);
                    weight = MathF.Pow(weight, 2); // Square the weight for more aggressive close-range avoidance
                    
                    avoidanceVector += avoidDirection * weight;
                }
                // Also check if we're heading towards the obstacle based on lookahead
                else if (dot > 0.2f)
                {
                    var distanceToLookahead = (gridPos.Position - lookAheadPos).Length();
                    if (distanceToLookahead < autopilot.ObstacleAvoidanceDistance * 0.5f)
                    {
                        hasObstacle = true;
                        closestObstacleDistance = Math.Min(closestObstacleDistance, distanceToGrid);
                        
                        // Light avoidance for predicted collisions
                        var perpendicular = new Vector2(-toGrid.Y, toGrid.X);
                        if (perpendicular.LengthSquared() > 0.01f)
                        {
                            perpendicular = Vector2.Normalize(perpendicular);
                            if (Vector2.Dot(perpendicular, direction) < 0)
                                perpendicular = -perpendicular;
                            
                            avoidanceVector += perpendicular * 0.5f;
                        }
                    }
                }
            }
        }

        if (hasObstacle && avoidanceVector.LengthSquared() > 0.01f)
        {
            avoidanceVector = Vector2.Normalize(avoidanceVector);
            
            // Reduce speed based on obstacle proximity
            if (closestObstacleDistance < autopilot.ObstacleAvoidanceDistance)
            {
                var proximityRatio = closestObstacleDistance / autopilot.ObstacleAvoidanceDistance;
                speedMultiplier = Math.Clamp(proximityRatio, 0.2f, 1.0f);
            }
            
            // Apply additional speed reduction based on grid density
            // More grids nearby = slower speed for better maneuverability
            if (nearbyGridCount > 1)
            {
                // Reduce speed by 10% per additional grid, with minimum of 15% speed
                var densityMultiplier = Math.Clamp(1.0f - (nearbyGridCount - 1) * 0.1f, 0.15f, 1.0f);
                speedMultiplier *= densityMultiplier;
            }
            
            // Blend original direction with avoidance - more aggressive when closer
            var avoidanceStrength = 1.0f - (closestObstacleDistance / autopilot.ObstacleAvoidanceDistance);
            avoidanceStrength = Math.Clamp(avoidanceStrength, 0.3f, 0.8f);
            
            direction = Vector2.Normalize(direction * (1.0f - avoidanceStrength) + avoidanceVector * avoidanceStrength);
        }

        return direction;
    }

    private void ApplyAutopilotThrust(EntityUid uid, ShuttleComponent shuttle, Vector2 localDirection, TransformComponent xform, PhysicsComponent physics, float frameTime)
    {
        // Get current velocity in local space
        var currentVelocity = (-xform.LocalRotation).RotateVec(physics.LinearVelocity);
        var maxVelocity = shuttle.BaseMaxLinearVelocity * 0.6f; // Match the 60% speed multiplier
        
        // Calculate which thrusters to fire and apply forces
        var force = Vector2.Zero;
        DirectionFlag directions = DirectionFlag.None;
        
        // X-axis thrust - only apply if we're under max velocity in that direction
        if (localDirection.X > 0.1f && currentVelocity.X < maxVelocity)
        {
            directions |= DirectionFlag.East;
            var index = (int)Math.Log2((int)DirectionFlag.East);
            force.X += shuttle.LinearThrust[index];
        }
        else if (localDirection.X < -0.1f && currentVelocity.X > -maxVelocity)
        {
            directions |= DirectionFlag.West;
            var index = (int)Math.Log2((int)DirectionFlag.West);
            force.X -= shuttle.LinearThrust[index];
        }
        
        // Y-axis thrust - only apply if we're under max velocity in that direction
        if (localDirection.Y > 0.1f && currentVelocity.Y < maxVelocity)
        {
            directions |= DirectionFlag.North;
            var index = (int)Math.Log2((int)DirectionFlag.North);
            force.Y += shuttle.LinearThrust[index];
        }
        else if (localDirection.Y < -0.1f && currentVelocity.Y > -maxVelocity)
        {
            directions |= DirectionFlag.South;
            var index = (int)Math.Log2((int)DirectionFlag.South);
            force.Y -= shuttle.LinearThrust[index];
        }

        // Enable thrusters visually
        if (directions != DirectionFlag.None)
        {
            _thruster.EnableLinearThrustDirection(shuttle, directions);
            
            // Apply the force in world coordinates
            var worldForce = xform.LocalRotation.RotateVec(force);
            _physics.ApplyForce(uid, worldForce, body: physics);
        }
        else
        {
            _thruster.DisableLinearThrusters(shuttle);
        }
    }

    private void RotateTowardsTarget(EntityUid uid, ShuttleComponent shuttle, TransformComponent xform, PhysicsComponent physics, Vector2 targetDirection, float frameTime)
    {
        // Calculate desired angle - subtract PI/2 to point front of ship (north) instead of right side (east)
        var currentAngle = xform.LocalRotation.Theta;
        var desiredAngle = MathF.Atan2(targetDirection.Y, targetDirection.X) - MathF.PI / 2f;
        var angleDiff = (float)Angle.ShortestDistance(currentAngle, desiredAngle).Theta;

        var maxAngularVelocity = ShuttleComponent.MaxAngularVelocity;
        var currentAngularVelocity = physics.AngularVelocity;

        // Apply stronger angular damping to reduce oscillation
        if (MathF.Abs(currentAngularVelocity) > 0.01f)
        {
            var dampingTorque = -currentAngularVelocity * shuttle.AngularThrust * 0.6f;
            _physics.ApplyAngularImpulse(uid, dampingTorque * frameTime, body: physics);
        }

        // Wider dead zone - don't rotate if we're close enough
        if (MathF.Abs(angleDiff) < 0.15f)
        {
            _thruster.SetAngularThrust(shuttle, false);
            return;
        }

        // Calculate proportional torque based on angle difference
        var direction = angleDiff > 0 ? 1f : -1f;
        
        // Proportional control: smaller angle difference = less torque
        // This prevents overshoot by reducing power as we approach the target
        var angleDiffAbs = MathF.Abs(angleDiff);
        var proportionalMultiplier = Math.Clamp(angleDiffAbs / 1.0f, 0.1f, 1.0f); // Scale from 0.1 to 1.0 over 1 radian
        
        // Further reduce torque application - 25% of max with proportional scaling
        var torqueMultiplier = 0.25f * proportionalMultiplier;
        
        // Only apply torque if we're under the max angular velocity in that direction
        var shouldApplyTorque = (direction > 0 && currentAngularVelocity < maxAngularVelocity * 0.7f) ||
                               (direction < 0 && currentAngularVelocity > -maxAngularVelocity * 0.7f);
        
        if (shouldApplyTorque)
        {
            var torque = shuttle.AngularThrust * direction * torqueMultiplier;
            
            // Apply angular force
            _thruster.SetAngularThrust(shuttle, true);
            _physics.ApplyAngularImpulse(uid, torque * frameTime, body: physics);
        }
        else
        {
            _thruster.SetAngularThrust(shuttle, false);
        }
    }

    private void ApplyBraking(EntityUid uid, ShuttleComponent shuttle, PhysicsComponent physics, TransformComponent xform, float frameTime)
    {
        // Apply braking forces
        var velocity = physics.LinearVelocity;
        if (velocity.LengthSquared() > 0.01f)
        {
            var shuttleVelocity = (-xform.LocalRotation).RotateVec(velocity);
            var force = Vector2.Zero;
            DirectionFlag brakeDirections = DirectionFlag.None;
            
            if (shuttleVelocity.X < -0.1f)
            {
                brakeDirections |= DirectionFlag.East;
                var index = (int)Math.Log2((int)DirectionFlag.East);
                force.X += shuttle.LinearThrust[index];
            }
            else if (shuttleVelocity.X > 0.1f)
            {
                brakeDirections |= DirectionFlag.West;
                var index = (int)Math.Log2((int)DirectionFlag.West);
                force.X -= shuttle.LinearThrust[index];
            }
            
            if (shuttleVelocity.Y < -0.1f)
            {
                brakeDirections |= DirectionFlag.North;
                var index = (int)Math.Log2((int)DirectionFlag.North);
                force.Y += shuttle.LinearThrust[index];
            }
            else if (shuttleVelocity.Y > 0.1f)
            {
                brakeDirections |= DirectionFlag.South;
                var index = (int)Math.Log2((int)DirectionFlag.South);
                force.Y -= shuttle.LinearThrust[index];
            }

            if (brakeDirections != DirectionFlag.None)
            {
                _thruster.EnableLinearThrustDirection(shuttle, brakeDirections);
                
                // Apply braking force with coefficient
                var impulse = force * ShuttleComponent.BrakeCoefficient;
                impulse = xform.LocalRotation.RotateVec(impulse);
                var forceMul = frameTime * physics.InvMass;
                var maxVelocity = (-velocity).Length() / forceMul;
                
                // Don't overshoot
                if (impulse.Length() > maxVelocity)
                    impulse = impulse.Normalized() * maxVelocity;
                
                _physics.ApplyForce(uid, impulse, body: physics);
            }
        }
        else
        {
            _thruster.DisableLinearThrusters(shuttle);
        }
        
        // Also brake angular velocity
        if (MathF.Abs(physics.AngularVelocity) > 0.01f)
        {
            var torque = shuttle.AngularThrust * (physics.AngularVelocity > 0f ? -1f : 1f) * ShuttleComponent.BrakeCoefficient;
            _thruster.SetAngularThrust(shuttle, true);
            _physics.ApplyAngularImpulse(uid, torque * frameTime, body: physics);
        }
        else
        {
            _thruster.SetAngularThrust(shuttle, false);
        }
    }

    /// <summary>
    /// Sends a message to all players on the shuttle.
    /// </summary>
    private void SendShuttleMessage(EntityUid shuttleUid, string message)
    {
        var players = new List<ICommonSession>();
        
        // Find all players on this shuttle
        var query = EntityQueryEnumerator<TransformComponent, ActorComponent>();
        while (query.MoveNext(out var uid, out var xform, out var actor))
        {
            // Check if entity is on this shuttle grid
            if (xform.GridUid == shuttleUid)
            {
                players.Add(actor.PlayerSession);
            }
        }
        
        // Send message to all players on the shuttle
        foreach (var player in players)
        {
            _chatManager.DispatchServerMessage(player, message);
        }
    }
    
    /// <summary>
    /// Toggle autopilot on or off.
    /// </summary>
    public bool ToggleAutopilot(EntityUid shuttleUid, EntityCoordinates? target = null)
    {
        var autopilot = EnsureComp<AutopilotComponent>(shuttleUid);
        
        if (autopilot.Enabled)
        {
            // Disable autopilot
            autopilot.Enabled = false;
            autopilot.TargetCoordinates = null;
            SendShuttleMessage(shuttleUid, "Autopilot disabled");
            return false;
        }
        else
        {
            // Enable autopilot
            if (target == null || !target.Value.IsValid(EntityManager))
            {
                SendShuttleMessage(shuttleUid, "Autopilot: No valid target set");
                return false;
            }

            autopilot.Enabled = true;
            autopilot.TargetCoordinates = target.Value;
            SendShuttleMessage(shuttleUid, "Autopilot enabled");
            return true;
        }
    }
}
