
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace App.Runtime.Player
{
    public class FlockingGridJobManager : MonoBehaviour
    {
        public static FlockingGridJobManager Instance { get; private set; }

        [Header("Grid Settings")]
        public float cellSize = 5f;
        public float globalFlockingRangeCap = 10f;

        [Header("Capacity")]
        public int initialGridCapacity = 1024;

        private readonly List<PredatorAgent> _predators = new();

        // Native buffers
        private NativeArray<float2> _predatorPositions;
        private NativeArray<float2> _predatorVelocities;
        private NativeArray<float> _predatorFlockingRanges;
        private NativeArray<float2> _separation;
        private NativeArray<float2> _alignment;
        private NativeArray<float2> _cohesion;

        private NativeParallelMultiHashMap<long, int> _grid;
        private bool _gridAllocated;

        private void Awake()
        {
            if (Instance && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            DisposeNative();
        }

        public void Register(PredatorAgent a)
        {
            if (a && !_predators.Contains(a)) _predators.Add(a);
        }

        public void Unregister(PredatorAgent a)
        {
            if (a) _predators.Remove(a);
        }

        private void FixedUpdate()
        {
            if (_predators.Count < 2) return;
            CompactLists();
            if (_predators.Count < 2) return;

            var predCount = _predators.Count;

            AllocateFrameBuffers(predCount);
            CopyAgentData(predCount);
            RebuildGrid(predCount);

            var jobHandle = ScheduleAndRunJobs(predCount);
            jobHandle.Complete();

            DistributeResults(predCount);
            DisposeNative();
        }

        private void AllocateFrameBuffers(int predCount)
        {
            _predatorPositions = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _predatorVelocities = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _predatorFlockingRanges = new NativeArray<float>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _separation = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            _alignment = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            _cohesion = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
        }
        
        private void CopyAgentData(int predCount)
        {
            for (var i = 0; i < predCount; i++)
            {
                var p = _predators[i];
                if (p == null)
                {
                    _predatorPositions[i] = new float2(1e9f, 1e9f);
                    _predatorVelocities[i] = float2.zero;
                    _predatorFlockingRanges[i] = 0f;
                    continue;
                }
                var pos = new float2(p.transform.position.x, p.transform.position.y);
                var vel = p.Rb ? new float2(p.Rb.linearVelocity.x, p.Rb.linearVelocity.y) : float2.zero;
                _predatorPositions[i] = pos;
                _predatorVelocities[i] = vel;
                _predatorFlockingRanges[i] = math.min(p.flockingRange, globalFlockingRangeCap);
            }
        }

        private void RebuildGrid(int predCount)
        {
            if (!_gridAllocated)
            {
                _grid = new NativeParallelMultiHashMap<long, int>(math.max(initialGridCapacity, predCount * 2), Allocator.TempJob);
                _gridAllocated = true;
            }
            else
            {
                _grid.Clear();
                if (_grid.Capacity < predCount * 2) _grid.Capacity = predCount * 2;
            }
        }

        private JobHandle ScheduleAndRunJobs(int predCount)
        {
            var build = new BuildGridJob
            {
                agentPositions = _predatorPositions,
                cellSize = math.max(0.0001f, cellSize),
                gridWriter = _grid.AsParallelWriter()
            };
            var h1 = build.Schedule(predCount, 64);

            var flock = new FlockingJob
            {
                agentPositions = _predatorPositions,
                agentVelocities = _predatorVelocities,
                agentFlockingRanges = _predatorFlockingRanges,
                cellSize = math.max(0.0001f, cellSize),
                grid = _grid,
                separation = _separation,
                alignment = _alignment,
                cohesion = _cohesion
            };
            return flock.Schedule(predCount, 64, h1);
        }

        private void DistributeResults(int predCount)
        {
            for (var i = 0; i < predCount; i++)
            {
                var pred = _predators[i];
                if (!pred) continue;
                pred.ReceiveFlockingVectors(_separation[i], _alignment[i], _cohesion[i]);
            }
        }

        private void DisposeNative()
        {
            if (_grid.IsCreated) _grid.Dispose();
            _gridAllocated = false;

            if (_predatorPositions.IsCreated) _predatorPositions.Dispose();
            if (_predatorVelocities.IsCreated) _predatorVelocities.Dispose();
            if (_predatorFlockingRanges.IsCreated) _predatorFlockingRanges.Dispose();
            if (_separation.IsCreated) _separation.Dispose();
            if (_alignment.IsCreated) _alignment.Dispose();
            if (_cohesion.IsCreated) _cohesion.Dispose();
        }

        private void CompactLists()
        {
            for (var i = _predators.Count - 1; i >= 0; i--)
                if (_predators[i] == null)
                    _predators.RemoveAt(i);
        }

        [BurstCompile]
        private struct BuildGridJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> agentPositions;
            [ReadOnly] public float cellSize;
            public NativeParallelMultiHashMap<long, int>.ParallelWriter gridWriter;

            public void Execute(int index)
            {
                var p = agentPositions[index];
                var cx = (int)math.floor(p.x / cellSize);
                var cy = (int)math.floor(p.y / cellSize);
                var key = MakeKey(cx, cy);
                gridWriter.Add(key, index);
            }
        }

        [BurstCompile]
        private struct FlockingJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> agentPositions;
            [ReadOnly] public NativeArray<float2> agentVelocities;
            [ReadOnly] public NativeArray<float> agentFlockingRanges;
            [ReadOnly] public float cellSize;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> grid;

            public NativeArray<float2> separation;
            public NativeArray<float2> alignment;
            public NativeArray<float2> cohesion;

            public void Execute(int i)
            {
                var p = agentPositions[i];
                var v = agentVelocities[i];
                var flockingRange = agentFlockingRanges[i];
                var senseSq = (flockingRange <= 0f) ? float.MaxValue : flockingRange * flockingRange;

                var cx = (int)math.floor(p.x / cellSize);
                var cy = (int)math.floor(p.y / cellSize);
                var r = math.max(0, (int)math.ceil(flockingRange / cellSize));

                var separationVec = float2.zero;
                var alignmentVec = float2.zero;
                var cohesionVec = float2.zero;
                var count = 0;

                for (var y = cy - r; y <= cy + r; y++)
                {
                    for (var x = cx - r; x <= cx + r; x++)
                    {
                        var key = MakeKey(x, y);
                        if (grid.TryGetFirstValue(key, out int neighborIndex, out var it))
                        {
                            do
                            {
                                if (i == neighborIndex) continue;

                                var q = agentPositions[neighborIndex];
                                var d2 = math.lengthsq(q - p);

                                if (d2 <= senseSq)
                                {
                                    var toNeighbor = q - p;
                                    if (d2 > 0.0001f)
                                    {
                                        separationVec -= (float2)toNeighbor / d2;
                                    }
                                    alignmentVec += agentVelocities[neighborIndex];
                                    cohesionVec += q;
                                    count++;
                                }
                            } while (grid.TryGetNextValue(out neighborIndex, ref it));
                        }
                    }
                }

                if (count > 0)
                {
                    alignmentVec /= count;
                    alignmentVec = math.normalize(alignmentVec - v);

                    cohesionVec /= count;
                    cohesionVec = math.normalize(cohesionVec - p);
                }

                separation[i] = separationVec;
                alignment[i] = alignmentVec;
                cohesion[i] = cohesionVec;
            }
        }

        private static long MakeKey(int x, int y) => ((long)x << 32) ^ (uint)y;
    }
}
