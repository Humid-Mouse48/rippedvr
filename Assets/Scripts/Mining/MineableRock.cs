using System.Collections;
using UnityEngine;

public sealed class MineableRock : MonoBehaviour
{
    [SerializeField] private int hitsToBreak = 4;
    [SerializeField] private GameObject chunkPrefab;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform[] attachedCrystals = new Transform[0];
    [SerializeField] private int minChunks = 8;
    [SerializeField] private int maxChunks = 12;
    [SerializeField] private float burstSpeed = 2.2f;
    [SerializeField] private float chunkSpawnRadius = 0.35f;
    [SerializeField] private float hitCooldownSeconds = 0.25f;
    [SerializeField, Range(0f, 1f)] private float chunkScaleVariance = 0.25f;

    private int hits;
    private float nextHitTime;
    private Vector3 baseLocalPosition;
    private Vector3 baseLocalScale;
    private AudioSource audioSource;
    private AudioClip hitClip;
    private AudioClip breakClip;
    private bool broken;
    private int crystalsDropped;

    public int Hits => hits;
    public int HitsRemaining => Mathf.Max(0, hitsToBreak - hits);
    public bool IsBroken => broken;

    private void Awake()
    {
        if (visualRoot == null)
        {
            visualRoot = transform;
        }

        baseLocalPosition = visualRoot.localPosition;
        baseLocalScale = visualRoot.localScale;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;
        audioSource.playOnAwake = false;
        audioSource.maxDistance = 25f;
        audioSource.rolloffMode = AudioRolloffMode.Linear;
        hitClip = PhysicsCompat.CreateThud(150f, 0.18f, 0.55f);
        breakClip = PhysicsCompat.CreateThud(80f, 0.5f, 0.75f);
    }

    public bool TryHit(Vector3 point, Vector3 direction)
    {
        if (broken || Time.time < nextHitTime)
        {
            return false;
        }

        nextHitTime = Time.time + hitCooldownSeconds;
        hits++;

        if (hits >= hitsToBreak)
        {
            Break(point);
            return true;
        }

        audioSource.pitch = Random.Range(0.9f, 1.1f);
        audioSource.PlayOneShot(hitClip, 1f);
        DropAttachedCrystal(point, direction);
        StopAllCoroutines();
        StartCoroutine(Shake(direction));
        return true;
    }

    private void DropAttachedCrystal(Vector3 point, Vector3 direction)
    {
        if (chunkPrefab == null || attachedCrystals == null || attachedCrystals.Length == 0)
        {
            return;
        }

        int perHit = Mathf.Max(1, Mathf.CeilToInt(attachedCrystals.Length / (float)hitsToBreak));
        int dropped = 0;
        while (dropped < perHit && crystalsDropped < attachedCrystals.Length)
        {
            Transform crystal = attachedCrystals[crystalsDropped];
            crystalsDropped++;
            if (crystal == null)
            {
                continue;
            }

            SpawnChunk(crystal.position, (crystal.up + direction.normalized * 0.4f + Random.insideUnitSphere * 0.3f).normalized * burstSpeed * 0.5f, crystal.rotation);
            crystal.gameObject.SetActive(false);
            dropped++;
        }
    }

    private void Break(Vector3 point)
    {
        broken = true;
        StopAllCoroutines();

        if (chunkPrefab != null)
        {
            int count = Random.Range(minChunks, maxChunks + 1);
            Vector3 center = transform.position + Vector3.up * 0.25f;

            for (int i = 0; i < count; i++)
            {
                Vector3 offset = Random.insideUnitSphere * chunkSpawnRadius;
                offset.y = Mathf.Abs(offset.y) * 0.6f;
                Vector3 position = center + offset;
                Vector3 direction = (offset + Vector3.up * 0.8f).normalized;
                SpawnChunk(position, direction * Random.Range(burstSpeed * 0.5f, burstSpeed), Random.rotation);
            }
        }

        GameObject soundObject = new GameObject("RockBreakSound");
        soundObject.transform.position = transform.position;
        AudioSource source = soundObject.AddComponent<AudioSource>();
        source.spatialBlend = 1f;
        source.maxDistance = 30f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.PlayOneShot(breakClip, 1f);
        Destroy(soundObject, 1.5f);

        Destroy(gameObject);
    }

    private void SpawnChunk(Vector3 position, Vector3 velocity, Quaternion rotation)
    {
        GameObject chunk = Instantiate(chunkPrefab, position, rotation);
        float scale = 1f + Random.Range(-chunkScaleVariance, chunkScaleVariance);
        chunk.transform.localScale *= scale;
        Rigidbody body = chunk.GetComponent<Rigidbody>();
        if (body != null)
        {
            PhysicsCompat.SetVelocity(body, velocity);
            body.angularVelocity = Random.insideUnitSphere * 6f;
        }
    }

    private IEnumerator Shake(Vector3 direction)
    {
        Vector3 push = Vector3.ProjectOnPlane(direction, Vector3.up).normalized * 0.02f;
        float duration = 0.22f;
        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float falloff = 1f - Mathf.Clamp01(t / duration);
            Vector3 jitter = Random.insideUnitSphere * 0.012f * falloff;
            visualRoot.localPosition = baseLocalPosition + push * falloff + jitter;
            visualRoot.localScale = baseLocalScale * (1f - 0.04f * falloff);
            yield return null;
        }

        visualRoot.localPosition = baseLocalPosition;
        visualRoot.localScale = baseLocalScale;
    }
}
