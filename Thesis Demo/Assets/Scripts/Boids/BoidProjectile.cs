using UnityEngine;

public class BoidProjectile : MonoBehaviour
{
    private Vector3 direction;
    private float speed;
    private float damage;
    private float lifetime = 6f;
    private Transform target;
    private float turnSpeed;

    public void Initialize(Vector3 dir, float spd, float dmg, Transform trackTarget = null, float trnSpd = 5f)
    {
        direction = dir.normalized;
        speed = spd;
        damage = dmg;
        target = trackTarget;
        turnSpeed = trnSpd;
    }

    private void Update()
    {
        if (target != null)
        {
            Vector3 toTarget = (target.position - transform.position).normalized;
            direction = Vector3.RotateTowards(direction, toTarget, turnSpeed * Time.deltaTime, 0f);
        }

        transform.position += direction * speed * Time.deltaTime;

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f)
            Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            other.GetComponent<PlayerHealth>()?.TakeDamage(damage);
            BehavioralMetricsCollector.Instance?.LogEvent(
                "ProjectileHit", gameObject.name, -1, $"Dmg={damage:F1}");
            Destroy(gameObject);
        }
    }
}
