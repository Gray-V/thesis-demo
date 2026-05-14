public interface IEnemy
{
    void TakeDamage(float amount);
    bool IsDead { get; }
    float HealthPercent { get; }
}
