using UnityEngine;

public struct LineSegment
{
    public Vector2 u;
    public Vector2 v;

    public LineSegment(Vector2 u, Vector2 v)
    {
        this.u = u;
        this.v = v;
    }
}
