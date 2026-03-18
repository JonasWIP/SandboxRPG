// client/scripts/interaction/IAttackable.cs
using SpacetimeDB.Types;

namespace SandboxRPG;

public interface IAttackable
{
    string AttackHintText { get; }
    bool CanAttack(Player? player);
    void Attack(Player? player);
}
