using UnityEngine;

public static class PhysicsCompat
{
    public static Vector3 GetVelocity(Rigidbody body)
    {
#if UNITY_6000_0_OR_NEWER
        return body.linearVelocity;
#else
        return body.velocity;
#endif
    }

    public static void SetVelocity(Rigidbody body, Vector3 value)
    {
#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = value;
#else
        body.velocity = value;
#endif
    }

    public static AudioClip CreateThud(float frequency, float seconds, float noiseAmount)
    {
        int rate = 22050;
        int count = Mathf.CeilToInt(rate * seconds);
        float[] data = new float[count];
        System.Random random = new System.Random(Mathf.RoundToInt(frequency));
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)rate;
            float envelope = Mathf.Exp(-t * 28f);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
            float noise = (float)(random.NextDouble() * 2.0 - 1.0);
            data[i] = (tone * (1f - noiseAmount) + noise * noiseAmount) * envelope * 0.8f;
        }

        AudioClip clip = AudioClip.Create("Thud", count, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
