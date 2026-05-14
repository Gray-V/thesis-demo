using CrashKonijn.Agent.Core;
using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Core;
using CrashKonijn.Goap.Runtime;

/// <summary>
/// Fallback action for FlockGoal. Clears the "attacking" and "movement override" flags so the
/// boid's normal flocking forces take over. Runs continuously until BoidGoapBrain requests
/// an attack goal.
/// </summary>
public class FlockAction : GoapActionBase<FlockAction.Data>
{
    public class Data : IActionData
    {
        public ITarget Target { get; set; }

        [GetComponent] public BoidAgent Boid { get; set; }
        [GetComponent] public BoidGoapBrain Brain { get; set; }
    }

    public override void Created() { }

    public override void Start(IMonoAgent agent, Data data)
    {
        data.Brain.isGoapAttacking = false;
        data.Brain.isMovementOverridden = false;
    }

    public override IActionRunState Perform(IMonoAgent agent, Data data, IActionContext context)
    {
        data.Brain.isGoapAttacking = false;
        data.Brain.isMovementOverridden = false;
        return ActionRunState.Continue;
    }

    public override void Complete(IMonoAgent agent, Data data) { }
    public override void Stop(IMonoAgent agent, Data data) { }
    public override void End(IMonoAgent agent, Data data) { }
}
