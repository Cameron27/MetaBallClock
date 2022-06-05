using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class Clock : MonoBehaviour
{
    public ComputeShader BallShader;
    public int BallCountPerSegment = 10;
    public int BallCountPerDot = 5;
    public float BallRadius = 10;
    public float BallCorrectionRate = 0.1f;
    public float BallTargetMoveSpeed = 1;
    public float DigitGap = 0.2f;
    public float DigitRound = 0.1f;
    public float DigitThickness = 5;
    public float PixelThreshold = 1f;
    public int BlockRatio = 1;
    public float Acceleration = 10;
    public float Drag = 0.1f;
    public float MergeThreshold = 5;
    public float MergeAcceleration = 1;
    public bool manualMode = false;
    public bool UpdateTime = false;
    public int[] OverrideTime = new int[4];
    private (int, int, int, int, float, float, float) oldSettings;
    private RenderTexture outImage = null;
    private MetaBall[][][] balls = null;
    private ClockSegment[] clockSegments = new ClockSegment[4];
    private float unit;
    private int[] currentTime = new int[] { 0, 7, 4, 8 };
    private List<(MetaBall, MetaBall)> chasingBalls = new List<(MetaBall, MetaBall)>();

    private readonly int[][] segmentIndices = new int[][] {
        new int[] { 6, 5, 2, 0, 1, 4 }, // 0
        new int[] { 5, 2 }, // 1
        new int[] { 6, 5, 3, 1, 0 }, // 2
        new int[] { 6, 5, 3, 2, 0 }, // 3
        new int[] { 4, 3, 5, 2 }, // 4
        new int[] { 6, 4, 3, 2, 0 }, // 5
        new int[] { 6, 4, 3, 1, 2, 0 }, // 6
        new int[] { 6, 5, 2 }, // 7
        new int[] { 6, 5, 4, 3, 2, 1, 0 }, // 8
        new int[] { 6, 4, 5, 3, 2 } }; // 9

    private void Start()
    {
        if (!manualMode)
        {
            currentTime = GetCurrentTimeArray();
            OverrideTime = (int[])currentTime.Clone();
        }
        else
        {
            currentTime = (int[])OverrideTime.Clone();
        }
        UpdateParameters(Screen.width, Screen.height);
    }

    private void FixedUpdate()
    {
        UpdateParameters(Screen.width, Screen.height);
        if (!manualMode && !currentTime.SequenceEqual(GetCurrentTimeArray()))
        {
            UpdateBalls(Screen.width, Screen.height, GetCurrentTimeArray());
        }
        if (UpdateTime)
        {
            UpdateTime = false;
            manualMode = true;
            UpdateBalls(Screen.width, Screen.height, OverrideTime);
        }
        MoveBalls(Screen.width, Screen.height);
    }

    private void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        // Update output image properties if needed
        if (outImage == null || outImage.width != source.width || outImage.height != source.height)
        {
            if (outImage != null) outImage.Release();
            outImage = new RenderTexture(source.width, source.height, 0, source.format) { enableRandomWrite = true };
            outImage.Create();
        }

        int metaBallKernal = BallShader.FindKernel("RenderMetaBall");

        BallShader.SetTexture(metaBallKernal, "Result", outImage);

        // Set metaball data buffer
        (MetaBallStruct[] ballStructs, int[] startIndices, int[] lengths, int blockSize, int numXBlocks, int numYBlocks)
            = GetMetaBallStructs(source.width, source.height);
        ComputeBuffer ballBuffer = new ComputeBuffer(ballStructs.Length, sizeof(float) * 3);
        ComputeBuffer startIndicesBuffer = new ComputeBuffer(startIndices.Length, sizeof(int));
        ComputeBuffer lengthsBuffer = new ComputeBuffer(lengths.Length, sizeof(int));
        ballBuffer.SetData(ballStructs);
        startIndicesBuffer.SetData(startIndices);
        lengthsBuffer.SetData(lengths);
        BallShader.SetBuffer(metaBallKernal, "balls", ballBuffer);
        BallShader.SetBuffer(metaBallKernal, "startIndices", startIndicesBuffer);
        BallShader.SetBuffer(metaBallKernal, "lengths", lengthsBuffer);

        // Set line segment data buffer
        ComputeBuffer lineSegmentBuffer = new ComputeBuffer(4 * 7, sizeof(float) * 4);
        var x = clockSegments.SelectMany(cs => cs.LineSegments).ToArray();
        lineSegmentBuffer.SetData(clockSegments.SelectMany(cs => cs.LineSegments).ToArray());
        BallShader.SetBuffer(metaBallKernal, "lineSegments", lineSegmentBuffer);

        // Set variables
        BallShader.SetInt("blockSize", blockSize);
        BallShader.SetInt("numXBlocks", numXBlocks);
        BallShader.SetInt("numYBlocks", numYBlocks);
        BallShader.SetInt("blockSearchRange", BlockRatio);
        BallShader.SetInt("ballCount", ballStructs.Length);
        BallShader.SetFloat("segmentThickness", DigitThickness);
        BallShader.SetFloat("threshold", PixelThreshold * unit);
        BallShader.SetFloat("threshold", PixelThreshold * unit);

        // Run shader
        BallShader.Dispatch(metaBallKernal, (int)Math.Ceiling(outImage.width / 8.0), (int)Math.Ceiling(outImage.height / 8.0), 1);
        ballBuffer.Dispose();
        startIndicesBuffer.Dispose();
        lengthsBuffer.Dispose();
        lineSegmentBuffer.Dispose();

        Graphics.Blit(outImage, destination);
    }

    private (MetaBallStruct[], int[], int[], int, int, int) GetMetaBallStructs(int width, int height)
    {
        IEnumerable<MetaBallStruct> ballsFlat = balls.SelectMany(x => x.SelectMany(y => y.Select(z => z.ToStruct())));
        IEnumerable<MetaBallStruct> chasingBallsFlat = chasingBalls.Select(x => x.Item1.ToStruct());
        IEnumerable<MetaBallStruct> allBalls = ballsFlat.Concat(chasingBallsFlat);
        int blockSize = (int)Math.Ceiling(PixelThreshold * unit / BlockRatio / 8) * 8;

        int numXBlocks = (int)Math.Ceiling(((double)width) / blockSize);
        int numYBlocks = (int)Math.Ceiling(((double)height) / blockSize);

        List<MetaBallStruct>[] allBallsGrouped = Enumerable.Range(0, numXBlocks * numYBlocks).Select(_ => new List<MetaBallStruct>()).ToArray();

        foreach (MetaBallStruct b in allBalls)
        {
            int x = (int)b.Position.x / blockSize;
            int y = (int)b.Position.y / blockSize;

            allBallsGrouped[y * numXBlocks + x].Add(b);
        }

        int[] lengths = allBallsGrouped.Select(bs => bs.Count).ToArray();
        int[] startIndices = new int[lengths.Length];
        int sum = 0;
        for (int i = 0; i < lengths.Length - 1; i++)
        {
            sum += lengths[i];
            startIndices[i + 1] = sum;
        }

        return (allBallsGrouped.SelectMany(x => x).ToArray(), startIndices, lengths, blockSize, numXBlocks, numYBlocks);
    }

    private void UpdateParameters(int width, int height)
    {
        // If uninitalised or setting have changed, reinitalise
        if (clockSegments == null || balls == null || oldSettings != (width, height, BallCountPerSegment, BallCountPerDot, BallRadius, DigitGap, DigitRound))
        {
            GenerageClockPosition(width, height);
            GenerateBalls(width, height);
            oldSettings = (width, height, BallCountPerSegment, BallCountPerDot, BallRadius, DigitGap, DigitRound);
        }
    }

    private void GenerateBalls(int width, int height)
    {
        LineSegment[][] activeSegments = currentTime.Select((n, i) =>
        {
            List<LineSegment> activeSegments = new List<LineSegment>();
            foreach (int index in segmentIndices[n])
            {
                activeSegments.Add(clockSegments[i].LineSegments[index]);
            }
            return activeSegments.ToArray();
        }).ToArray();

        balls = new MetaBall[5][][];
        for (int i = 0; i < 4; i++)
        {
            balls[i] = new MetaBall[activeSegments[i].Length][];
            for (int j = 0; j < activeSegments[i].Length; j++)
            {
                balls[i][j] = new MetaBall[BallCountPerSegment];
                for (int k = 0; k < BallCountPerSegment; k++)
                {
                    balls[i][j][k] = new MetaBall(new Vector2(Random.Range(0, width), Random.Range(0, height)), Vector2.zero, activeSegments[i][j], BallRadius * unit);
                }
            }
        }

        balls[4] = new MetaBall[][] { new MetaBall[2 * BallCountPerDot] };
        for (int i = 0; i < 2 * BallCountPerDot; i++)
        {
            Vector2 dotCenter = new Vector2(width / 2, height / 2 + unit * 0.5f * (i < BallCountPerDot ? 1 : -1));
            LineSegment target = new LineSegment();
            target.u = dotCenter + Random.insideUnitCircle.normalized * unit * DigitGap * 0.25f;
            target.v = dotCenter + Random.insideUnitCircle.normalized * unit * DigitGap * 0.25f;
            balls[4][0][i] = new MetaBall(new Vector2(Random.Range(0, width), Random.Range(0, height)), Vector2.zero, target, BallRadius * unit);
        }
    }

    private void UpdateBalls(int width, int height, int[] newTime)
    {
        List<int> changingIndices = new List<int>();
        for (int i = 0; i < 4; i++)
            if (currentTime[i] != newTime[i]) changingIndices.Add(i);

        int oldCount = changingIndices.Select(i => balls[i]).Sum(x => x.Length) * BallCountPerSegment;
        int newCount = changingIndices.Select(i => segmentIndices[newTime[i]]).Sum(x => x.Length) * BallCountPerSegment;

        List<MetaBall> changingBalls;

        if (oldCount <= newCount)
        {
            changingBalls = changingIndices.SelectMany(i => balls[i].SelectMany(x => x)).ToList();
            for (int i = 0; i < newCount - oldCount; i++)
            {
                MetaBall ball = changingBalls[Random.Range(0, oldCount)];
                ball.Radius /= 2;
                MetaBall clone = (MetaBall)ball.Clone();
                changingBalls.Add(clone);

                List<(MetaBall, MetaBall)> newChasingBalls = new List<(MetaBall, MetaBall)>();
                foreach ((MetaBall chasing, MetaBall target) in chasingBalls)
                {
                    if (target != ball) continue;

                    chasing.Radius /= 2;
                    MetaBall chasingClone = (MetaBall)chasing.Clone();

                    newChasingBalls.Add((chasingClone, clone));
                }
                chasingBalls.AddRange(newChasingBalls);
            }
        }
        else
        {
            (int doubles, int triples, int quads) = CalculateMerges(oldCount, newCount);

            changingBalls = new List<MetaBall>();
            List<MetaBall> ballsToMerge = changingIndices.SelectMany(i => balls[i].SelectMany(x => x)).ToList();

            for (int i = 0; i < doubles + triples + quads; i++)
            {
                int numberMerging = (i < doubles ? 1 : i < doubles + triples ? 2 : 3);

                MetaBall ballToTarget = ballsToMerge[Random.Range(0, ballsToMerge.Count)];
                ballToTarget.TargetRadius /= numberMerging + 1;
                ballsToMerge.Remove(ballToTarget);
                changingBalls.Add(ballToTarget);

                for (int j = 0; j < numberMerging; j++)
                {
                    MetaBall ballToChase = ballsToMerge[Random.Range(0, ballsToMerge.Count)];
                    ballToChase.TargetRadius /= numberMerging + 1;
                    ballToChase.TargetSegment = null;
                    ballsToMerge.Remove(ballToChase);
                    chasingBalls.Add((ballToChase, ballToTarget));
                }
            }

            changingBalls.AddRange(ballsToMerge);
        }

        Shuffle(changingBalls);

        LineSegment[][] activeSegments = newTime.Select((n, i) =>
        {
            List<LineSegment> activeSegments = new List<LineSegment>();
            foreach (int index in segmentIndices[n])
            {
                activeSegments.Add(clockSegments[i].LineSegments[index]);
            }
            return activeSegments.ToArray();
        }).ToArray();

        foreach (int i in changingIndices)
        {
            balls[i] = new MetaBall[activeSegments[i].Length][];
            for (int j = 0; j < activeSegments[i].Length; j++)
            {
                balls[i][j] = new MetaBall[BallCountPerSegment];
                for (int k = 0; k < BallCountPerSegment; k++)
                {
                    MetaBall ball = changingBalls[changingBalls.Count - 1];
                    changingBalls.RemoveAt(changingBalls.Count - 1);
                    ball.TargetSegment = activeSegments[i][j];
                    balls[i][j][k] = ball;
                }
            }
        }

        currentTime = (int[])newTime.Clone();
    }

    private (int, int, int) CalculateMerges(int oldCount, int newCount)
    {
        if (newCount * 2 >= oldCount)
        {
            return (oldCount - newCount, 0, 0);
        }
        else if (newCount * 3 >= oldCount)
        {
            (int x, int y, int z) = CalculateMerges(oldCount - 3, newCount - 1);
            return (x, y + 1, z);
        }
        else
        {
            (int x, int y, int z) = CalculateMerges(oldCount - 4, newCount - 1);
            return (x, y, z + 1);
        }
    }

    private void GenerageClockPosition(int width, int height)
    {
        unit = Math.Min(width / (4 + DigitGap * 10), height / (2 + DigitGap * 2));

        for (int i = 0; i < 4; i++)
        {
            float centerX = width / 2;
            if (i <= 1)
                centerX -= unit * (1 + DigitGap * 2) * (2 - i) - unit * 0.5f;
            else
                centerX += unit * (1 + DigitGap * 2) * (i - 1) - unit * 0.5f;
            Vector2 center = new Vector2(centerX, height / 2);

            Vector2[] points = Enumerable.Range(0, 6).Select(i =>
            {
                float x;
                if (i % 2 == 0) x = center.x - unit * 0.5f;
                else x = center.x + unit * 0.5f;

                float y = center.y + unit * (i / 2 - 1);

                return new Vector2(x, y);
            }).ToArray();

            Vector2 DigitGapX = new Vector2(DigitRound * unit * DigitThickness, 0);
            Vector2 DigitGapY = new Vector2(0, DigitRound * unit * DigitThickness);

            LineSegment[] lineSegments = new LineSegment[7];
            lineSegments[0] = new LineSegment(points[0] + DigitGapX, points[1] - DigitGapX);
            lineSegments[1] = new LineSegment(points[0] + DigitGapY, points[2] - DigitGapY);
            lineSegments[2] = new LineSegment(points[1] + DigitGapY, points[3] - DigitGapY);
            lineSegments[3] = new LineSegment(points[2] + DigitGapX, points[3] - DigitGapX);
            lineSegments[4] = new LineSegment(points[2] + DigitGapY, points[4] - DigitGapY);
            lineSegments[5] = new LineSegment(points[3] + DigitGapY, points[5] - DigitGapY);
            lineSegments[6] = new LineSegment(points[4] + DigitGapX, points[5] - DigitGapX);

            clockSegments[i] = new ClockSegment(lineSegments);
        }
    }

    private void MoveBalls(int width, int height)
    {
        // Update position and size of main balls
        for (int i = 0; i < balls.Length; i++)
            for (int j = 0; j < balls[i].Length; j++)
                for (int k = 0; k < balls[i][j].Length; k++)
                {
                    balls[i][j][k].Move(width, height, Acceleration, BallTargetMoveSpeed, Drag, unit, Time.deltaTime);
                    balls[i][j][k].Resize(BallCorrectionRate, Time.deltaTime);
                }

        // Update position and size of chasing balls
        List<(MetaBall, MetaBall)> caughtBalls = new List<(MetaBall, MetaBall)>();
        foreach ((MetaBall chaser, MetaBall target) in chasingBalls)
        {
            chaser.TargetPoint = target.Position;
            chaser.Move(width, height, Math.Min(Acceleration, (chaser.Position - target.Position).magnitude * MergeAcceleration), BallTargetMoveSpeed, Drag, unit, Time.deltaTime);
            chaser.Resize(BallCorrectionRate, Time.deltaTime);
            if ((chaser.Position - target.Position).magnitude < unit * BallRadius * MergeThreshold)
            {
                caughtBalls.Add((chaser, target));
                target.Position = (chaser.Position + target.Position) / 2;
                target.Radius += chaser.Radius;
                target.TargetRadius += chaser.TargetRadius;
            }
        }
        // Remove balls that were caught from list
        caughtBalls.ForEach(x => chasingBalls.Remove(x));
    }

    private int[] GetCurrentTimeArray()
    {
        return new int[] { DateTime.Now.Hour / 10, DateTime.Now.Hour % 10, DateTime.Now.Minute / 10, DateTime.Now.Minute % 10 };
    }

    private static void Shuffle<T>(List<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Random.Range(0, n + 1);
            T value = list[k];
            list[k] = list[n];
            list[n] = value;
        }
    }
}
