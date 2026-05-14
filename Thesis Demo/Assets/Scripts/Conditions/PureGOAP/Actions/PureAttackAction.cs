using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;
using UnityEngine;

/// <summary>
/// Pure GOAP attack action with a 3-phase melee cycle:
///   1. Approach — steer toward player until within trigger distance
///   2. WindUp  — pause briefly, lock aim at player
///   3. Charge  — burst forward through player at high speed, deal damage on contact
///
/// Replicates the BOIDS melee attack feel (wind-up → charge → cooldown)
/// without FlockManager rate-limiting — each agent attacks independently via GOAP.
///
/// Condition: PlayerVisible >= 1
/// Effect: PureAttackDone Increase
/// Target: PlayerTarget
/// </summary>
public class PureAttackAction : GoapActionBase<PureAttackAction.Data>
{
    private const float TriggerDistance = 6f;
    private const float WindUpDuration = 0.4f;
    private const float ChargeDuration = 0.6f;
    private const float ChargeSpeedMultiplier = 3f;
    private const float DamageContactDistance = 2f;

    public enum Phase { Approach, WindUp, Charge }

    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public PureGOAPAgent Agent { get; set; }

        public Phase CurrentPhase { get; set; }
        public float PhaseTimer { get; set; }
        public Vector3 ChargeDirection { get; set; }
        public bool DamageDealt { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.CurrentPhase = Phase.Approach;
        data.PhaseTimer = 0f;
        data.DamageDealt = false;

        if (data.Target is TransformTarget transformTarget)
        {
            data.Agent.targetPlayer = transformTarget.Transform;
        }

        // Lock out other agents immediately — only one attacker per swarm pass
        PureGOAPAgent.SwarmAttackCooldown = data.Agent.SwarmCooldownDuration;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        if (data.Target == null || data.Agent.targetPlayer == null)
            return ActionRunState.Stop;

        Vector3 agentPos = data.Agent.Position;
        Vector3 targetPos = data.Target.Position;
        float distance = Vector3.Distance(agentPos, targetPos);

        switch (data.CurrentPhase)
        {
            case Phase.Approach:
                // Steer toward player
                data.Agent.SteerToward(targetPos);

                // Transition to wind-up when close enough
                if (distance <= TriggerDistance)
                {
                    data.CurrentPhase = Phase.WindUp;
                    data.PhaseTimer = WindUpDuration;
                    // Slow down for the wind-up
                    data.Agent.SetVelocity(data.Agent.velocity * 0.2f);
                }
                break;

            case Phase.WindUp:
                // Hold position, face the player
                data.Agent.SetVelocity(Vector3.zero);
                data.PhaseTimer -= context.DeltaTime;

                if (data.PhaseTimer <= 0f)
                {
                    // Lock charge direction and burst forward
                    data.ChargeDirection = (targetPos - agentPos).normalized;
                    data.CurrentPhase = Phase.Charge;
                    data.PhaseTimer = ChargeDuration;
                }
                break;

            case Phase.Charge:
                // Burst through the player at high speed
                float chargeSpeed = data.Agent.MaxSpeed * ChargeSpeedMultiplier;
                data.Agent.SetVelocity(data.ChargeDirection * chargeSpeed);
                data.PhaseTimer -= context.DeltaTime;

                // Deal damage on contact (once)
                if (!data.DamageDealt && distance <= DamageContactDistance)
                {
                    var playerHealth = data.Agent.targetPlayer.GetComponent<PlayerHealth>();
                    if (playerHealth != null)
                    {
                        playerHealth.TakeDamage(data.Agent.AttackDamage);
                    }
                    data.DamageDealt = true;
                }

                // Charge phase ends after timer
                if (data.PhaseTimer <= 0f)
                {
                    return ActionRunState.Completed;
                }
                break;
        }

        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data)
    {
        data.Agent.cooldownTimer = data.Agent.AttackCooldown;
        // Set swarm-wide cooldown so only one attack per pass
        PureGOAPAgent.SwarmAttackCooldown = data.Agent.SwarmCooldownDuration;
    }

    public override void Stop(IMonoAgent agent, Data data) { }

    public override void End(IMonoAgent agent, Data data)
    {
        data.Agent.targetPlayer = null;
    }
}
