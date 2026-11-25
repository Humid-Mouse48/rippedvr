using System.Collections;
using UnityEngine;

public sealed class CrystalChunk : Grabbable
{
    [SerializeField] private float idleLifetimeSeconds = 180f;

    private float idleTimer;

    protected override void Awake()
    {
        base.Awake();
        idleTimer = idleLifetimeSeconds;
    }

    private void Update()
    {
        if (idleLifetimeSeconds <= 0f || IsHeld)
        {
            idleTimer = idleLifetimeSeconds;
            return;
        }

        idleTimer -= Time.deltaTime;
        if (idleTimer <= 0f)
        {
            StartCoroutine(ShrinkAndDestroy());
            idleLifetimeSeconds = 0f;
        }
    }

    private IEnumerator ShrinkAndDestroy()
    {
        Vector3 start = transform.localScale;
        float t = 0f;
        while (t < 1f)
        {
            if (IsHeld)
            {
                transform.localScale = start;
                idleLifetimeSeconds = 180f;
                idleTimer = idleLifetimeSeconds;
                yield break;
            }

            t += Time.deltaTime * 2f;
            transform.localScale = Vector3.Lerp(start, Vector3.zero, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}
