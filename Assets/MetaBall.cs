using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class MetaBall : ICloneable
{
    public Vector2 Position;
    public Vector2 Velocity;
    private LineSegment? _targetSegment;
    public LineSegment? TargetSegment
    {
        get { return _targetSegment; }
        set
        {
            _targetSegment = value;
            if (value != null)
            {
                t = Random.value * 2;
            }
        }
    }
    public Vector2 TargetPoint;
    private float _t;
    private float t
    {
        get { return _t; }
        set
        {
            _t = value < 2 ? value : value - 2;
            if (TargetSegment != null)
            {
                LineSegment l = (LineSegment)TargetSegment;
                float correctedT = _t < 1 ? _t : 2 - _t;
                TargetPoint = l.u + correctedT * (l.v - l.u);
            }
        }
    }
    public float Radius;
    public float TargetRadius;

    public MetaBall(Vector2 position, Vector2 velocity, LineSegment? targetSegment, float radius)
    {
        Position = position;
        Velocity = velocity;
        TargetSegment = targetSegment;
        Radius = radius;
        TargetRadius = radius;
    }

    public void Move(int width, int height, float acceleration, float ballTargetMoveSpeed, float drag, float unit, float deltaTime)
    {
        if (TargetSegment != null)
            t += ballTargetMoveSpeed * deltaTime * (1 + Random.value * 0.1f);
        Velocity += (TargetPoint - Position).normalized * acceleration * unit * deltaTime;
        Velocity *= 1 - (drag * deltaTime);
        Position += Velocity * deltaTime;
        if (Position.x < 0)
        {
            Position.x = 0;
            Velocity.x *= -1;
        }
        if (Position.x > width)
        {
            Position.x = width;
            Velocity.x *= -1;
        }
        if (Position.y < 0)
        {
            Position.y = 0;
            Velocity.y *= -1;
        }
        if (Position.y > height)
        {
            Position.y = height;
            Velocity.y *= -1;
        }
    }

    public void Resize(float ballCorrectionRate, float deltaTime)
    {
        if (Radius == TargetRadius) return;

        if (Radius > TargetRadius)
        {
            Radius -= TargetRadius * ballCorrectionRate * deltaTime;
            if (Radius < TargetRadius)
            {
                Radius = TargetRadius;
            }
        }
        else if (Radius < TargetRadius)
        {
            Radius += TargetRadius * ballCorrectionRate * deltaTime;
            if (Radius > TargetRadius)
            {
                Radius = TargetRadius;
            }
        }
    }

    public object Clone()
    {
        return new MetaBall(Position, Velocity, TargetSegment, Radius) { TargetPoint = TargetPoint, TargetRadius = TargetRadius };
    }

    public MetaBallStruct ToStruct()
    {
        return new MetaBallStruct(Position, Radius);
    }
}
