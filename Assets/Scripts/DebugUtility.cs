using Unity.Mathematics;
using UnityEngine;

public static class DebugUtility
{
    public static void DrawCube(Vector3 center, Vector3 size, 
        Color? color = null, float duration = 0f, bool depthTest = true)
    {
        color ??= Color.white;
        
        var halfSize = size / 2f;
        var left = center.x - halfSize.x;
        var right = center.x + halfSize.x;
        var top = center.y + halfSize.y;
        var bottom = center.y - halfSize.y;
        var front = center.z + halfSize.z;
        var back = center.z - halfSize.z;

        var lbb = new Vector3(left, bottom, back);
        var lbf = new Vector3(left, bottom, front);
        var rbf = new Vector3(right, bottom, front);
        var rbb = new Vector3(right, bottom, back);
        var ltb = new Vector3(left, top, back);
        var ltf = new Vector3(left, top, front);
        var rtf = new Vector3(right, top, front);
        var rtb = new Vector3(right, top, back);

        Debug.DrawLine(lbb, lbf, color.Value, duration, depthTest);
        Debug.DrawLine(lbf, rbf, color.Value, duration, depthTest);
        Debug.DrawLine(rbf, rbb, color.Value, duration, depthTest);
        Debug.DrawLine(rbb, lbb, color.Value, duration, depthTest);
        
        Debug.DrawLine(ltb, ltf, color.Value, duration, depthTest);
        Debug.DrawLine(ltf, rtf, color.Value, duration, depthTest);
        Debug.DrawLine(rtf, rtb, color.Value, duration, depthTest);
        Debug.DrawLine(rtb, ltb, color.Value, duration, depthTest);
        
        Debug.DrawLine(lbb, ltb, color.Value, duration, depthTest);
        Debug.DrawLine(lbf, ltf, color.Value, duration, depthTest);
        Debug.DrawLine(rbf, rtf, color.Value, duration, depthTest);
        Debug.DrawLine(rbb, rtb, color.Value, duration, depthTest);
    }
    
    public static void DrawCube(float4x4 matrix, Vector3 center, Vector3 size, 
        Color? color = null, float duration = 0f, bool depthTest = true)
    {
        color ??= Color.white;
        
        var halfSize = size / 2f;
        var left = center.x - halfSize.x;
        var right = center.x + halfSize.x;
        var top = center.y + halfSize.y;
        var bottom = center.y - halfSize.y;
        var front = center.z + halfSize.z;
        var back = center.z - halfSize.z;

        var lbb = new Vector3(left, bottom, back);
        var lbf = new Vector3(left, bottom, front);
        var rbf = new Vector3(right, bottom, front);
        var rbb = new Vector3(right, bottom, back);
        var ltb = new Vector3(left, top, back);
        var ltf = new Vector3(left, top, front);
        var rtf = new Vector3(right, top, front);
        var rtb = new Vector3(right, top, back);

        lbb = matrix.MultiplyPoint3x4(lbb);
        lbf = matrix.MultiplyPoint3x4(lbf);
        rbf = matrix.MultiplyPoint3x4(rbf);
        rbb = matrix.MultiplyPoint3x4(rbb);
        ltb = matrix.MultiplyPoint3x4(ltb);
        ltf = matrix.MultiplyPoint3x4(ltf);
        rtf = matrix.MultiplyPoint3x4(rtf);
        rtb = matrix.MultiplyPoint3x4(rtb);

        Debug.DrawLine(lbb, lbf, color.Value, duration, depthTest);
        Debug.DrawLine(lbf, rbf, color.Value, duration, depthTest);
        Debug.DrawLine(rbf, rbb, color.Value, duration, depthTest);
        Debug.DrawLine(rbb, lbb, color.Value, duration, depthTest);
        
        Debug.DrawLine(ltb, ltf, color.Value, duration, depthTest);
        Debug.DrawLine(ltf, rtf, color.Value, duration, depthTest);
        Debug.DrawLine(rtf, rtb, color.Value, duration, depthTest);
        Debug.DrawLine(rtb, ltb, color.Value, duration, depthTest);
        
        Debug.DrawLine(lbb, ltb, color.Value, duration, depthTest);
        Debug.DrawLine(lbf, ltf, color.Value, duration, depthTest);
        Debug.DrawLine(rbf, rtf, color.Value, duration, depthTest);
        Debug.DrawLine(rbb, rtb, color.Value, duration, depthTest);
    }
}
