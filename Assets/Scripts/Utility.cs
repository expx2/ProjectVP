using Unity.Mathematics;

public static class Utility
{
    public static float3 MultiplyPoint3x4(this float4x4 m, float3 point)
    {
        float3 float3;
        float3.x = (float) ((double) m.c0.x * (double) point.x + (double) m.c1.x * (double) point.y + (double) m.c2.x * (double) point.z) + m.c3.x;
        float3.y = (float) ((double) m.c0.y * (double) point.x + (double) m.c1.y * (double) point.y + (double) m.c2.y * (double) point.z) + m.c3.y;
        float3.z = (float) ((double) m.c0.z * (double) point.x + (double) m.c1.z * (double) point.y + (double) m.c2.z * (double) point.z) + m.c3.z;
        return float3;
    }
}
