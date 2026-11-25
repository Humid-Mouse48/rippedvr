using UnityEngine;

[DefaultExecutionOrder(500)]
public sealed class Pickaxe : Grabbable
{
    [SerializeField] private Transform[] hitPoints = new Transform[0];
    [SerializeField] private float hitRadius = 0.05f;
    [SerializeField] private float minHitSpeed = 1.6f;
    [SerializeField] private LayerMask hitMask = ~0;

    private Vector3[] lastPoints;
    private bool hasLast;
    private AudioSource audioSource;
    private AudioClip clinkClip;

    protected override void Awake()
    {
        base.Awake();
        if (hitPoints == null || hitPoints.Length == 0)
        {
            hitPoints = new[] { transform };
        }

        lastPoints = new Vector3[hitPoints.Length];
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
        clinkClip = PhysicsCompat.CreateThud(900f, 0.12f, 0.35f);
    }

    protected override void OnGrabbed(GrabHand hand)
    {
        hasLast = false;
    }

    protected override void OnReleased(GrabHand hand)
    {
        hasLast = false;
    }

    private void LateUpdate()
    {
        if (!IsHeld)
        {
            return;
        }

        if (!hasLast)
        {
            for (int i = 0; i < hitPoints.Length; i++)
            {
                lastPoints[i] = hitPoints[i].position;
            }

            hasLast = true;
            return;
        }

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        bool landed = false;

        for (int i = 0; i < hitPoints.Length && !landed; i++)
        {
            Vector3 current = hitPoints[i].position;
            Vector3 delta = current - lastPoints[i];
            float distance = delta.magnitude;

            if (distance > 0.0001f && distance / dt >= minHitSpeed)
            {
                landed = SweepForRock(lastPoints[i], delta / distance, distance);
            }

            lastPoints[i] = current;
        }

        if (landed)
        {
            audioSource.pitch = Random.Range(0.9f, 1.15f);
            audioSource.PlayOneShot(clinkClip, 0.8f);
            if (Holder != null)
            {
                Holder.Pulse(1f, 0.12f);
            }
        }
    }

    private bool SweepForRock(Vector3 from, Vector3 direction, float distance)
    {
        RaycastHit[] results = Physics.SphereCastAll(from, hitRadius, direction, distance + hitRadius * 0.5f, hitMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < results.Length; i++)
        {
            MineableRock rock = results[i].collider.GetComponentInParent<MineableRock>();
            if (rock != null && rock.TryHit(results[i].point, direction))
            {
                return true;
            }
        }

        return false;
    }
}
