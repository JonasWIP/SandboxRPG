// service/StateMachineRunner.cs
namespace SandboxRPG.Service;

public static class StateMachineRunner
{
    public static string? EvaluateTransitions(NpcContext ctx, NpcStateConfig stateConfig)
    {
        foreach (var transition in stateConfig.Transitions)
        {
            var condition = ConditionRegistry.Get(transition.Condition);
            if (condition == null)
            {
                Console.WriteLine($"[StateMachine] Unknown condition: {transition.Condition}");
                continue;
            }
            if (condition.Evaluate(ctx, transition.Param))
                return transition.TargetState;
        }
        return null;
    }

    public static void ExecuteBehaviors(NpcContext ctx, NpcStateConfig stateConfig, float delta)
    {
        foreach (var behaviorName in stateConfig.Behaviors)
        {
            var behavior = BehaviorRegistry.Get(behaviorName);
            if (behavior == null)
            {
                Console.WriteLine($"[StateMachine] Unknown behavior: {behaviorName}");
                continue;
            }
            behavior.Tick(ctx, delta);
        }
    }
}
