using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace App.Runtime.Player
{
    /// <summary>
    /// グリッド分割 + Job System で最近傍 Prey を並列探索し、Predator に配布。
    /// ・物理APIは触らない（計算のみ並列）
    /// ・Preyはセルへ NativeParallelMultiHashMap<long,int> 登録
    /// ・Predatorは近傍セルのみ走査
    /// </summary>
    public class EcosystemGridJobManager : MonoBehaviour
    {
        public static EcosystemGridJobManager Instance { get; private set; }

        [Header("Grid Settings")] [Tooltip("セル一辺（World単位）")]
        public float cellSize = 4f;

        [Tooltip("索敵半径の全体上限（Predator.senseRange との min を採用）")]
        public float globalSenseCap = 50f;

        [Header("Capacity")] [Tooltip("Prey収容の初期キャパ（自動拡張されるがリサイズを減らすため大きめ推奨）")]
        public int initialGridCapacity = 1024;

        // 登録されている個体（MonoBehaviour参照）
        private readonly List<PredatorAgent> _predators = new();
        private readonly List<PreyAgent> _preys = new();

        // ネイティブバッファ（フレーム毎にTempJobで確保する分）
        private NativeArray<float2> _predPos; // [countPred]
        private NativeArray<float> _predSense; // [countPred]
        private NativeArray<float2> _preyPos; // [countPrey]
        private NativeArray<int> _nearest; // [countPred]

        // グリッド（ネイティブ / フレーム毎に再構築）
        private NativeParallelMultiHashMap<long, int> _grid; // key: cellKey, val: preyIndex
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

        private void DisposeNative()
        {
            if (_gridAllocated && _grid.IsCreated) _grid.Dispose();
            _gridAllocated = false;

            if (_predPos.IsCreated) _predPos.Dispose();
            if (_predSense.IsCreated) _predSense.Dispose();
            if (_preyPos.IsCreated) _preyPos.Dispose();
            if (_nearest.IsCreated) _nearest.Dispose();
        }

        // ===== 登録口（Predator/Prey 側の OnEnable/OnDisable から呼ぶ） =====
        /// <summary>
        /// Predatorを登録します。
        /// </summary>
        /// <param name="a">登録するPredatorAgent</param>
        public void Register(PredatorAgent a)
        {
            if (a && !_predators.Contains(a)) _predators.Add(a);
        }

        /// <summary>
        /// Predatorを登録解除します。
        /// </summary>
        /// <param name="a">登録解除するPredatorAgent</param>
        public void Unregister(PredatorAgent a)
        {
            if (a) _predators.Remove(a);
        }

        /// <summary>
        /// Preyを登録します。
        /// </summary>
        /// <param name="a">登録するPreyAgent</param>
        public void Register(PreyAgent a)
        {
            if (a && !_preys.Contains(a)) _preys.Add(a);
        }

        /// <summary>
        /// Preyを登録解除します。
        /// </summary>
        /// <param name="a">登録解除するPreyAgent</param>
        public void Unregister(PreyAgent a)
        {
            if (a) _preys.Remove(a);
        }

        private void FixedUpdate()
        {
            if (_predators.Count == 0 || _preys.Count == 0) return;
            CompactLists();
            if (_predators.Count == 0 || _preys.Count == 0) return;

            var predCount = _predators.Count;
            var preyCount = _preys.Count;

            // ネイティブ配列を確保
            AllocateFrameBuffers(predCount, preyCount);

            // 位置と索敵半径をコピー（メインスレッド）
            for (var i = 0; i < predCount; i++)
            {
                var p = _predators[i];
                var pos = p ? new float2(p.transform.position.x, p.transform.position.y) : new float2(1e9f, 1e9f);
                _predPos[i] = pos;
                _predSense[i] = p ? math.min(p.genome.senseRange, globalSenseCap) : 0f;
            }

            for (var j = 0; j < preyCount; j++)
            {
                var q = _preys[j];
                _preyPos[j] = q ? new float2(q.transform.position.x, q.transform.position.y) : new float2(1e9f, 1e9f);
            }

            // グリッド（MultiHashMap）再構築
            if (!_gridAllocated)
            {
                _grid = new NativeParallelMultiHashMap<long, int>(math.max(initialGridCapacity, preyCount * 2),
                    Allocator.TempJob);
                _gridAllocated = true;
            }
            else
            {
                if (_grid.IsCreated) _grid.Clear();
                // 容量が足りなそうなら増やす
                if (_grid.Capacity < preyCount * 2) _grid.Capacity = preyCount * 2;
            }

            // Job 1: Prey をセルへ登録
            var build = new BuildGridJob
            {
                preyPositions = _preyPos,
                cellSize = math.max(0.0001f, cellSize),
                gridWriter = _grid.AsParallelWriter()
            };
            var h1 = build.Schedule(preyCount, 64);

            // Job 2: Predator 毎に近傍セル検索（Job1 完了後）
            var find = new NearestJob
            {
                predatorPositions = _predPos,
                predatorSense = _predSense,
                preyPositions = _preyPos,
                cellSize = math.max(0.0001f, cellSize),
                grid = _grid,
                outNearest = _nearest
            };
            var h2 = find.Schedule(predCount, 64, h1);
            h2.Complete(); // 結果受け取り

            // 結果を各 Predator へ配布（メインスレッド）
            for (var i = 0; i < predCount; i++)
            {
                var pred = _predators[i];
                if (!pred) continue;
                var idx = _nearest[i];
                var target = (idx >= 0 && idx < preyCount) ? _preys[idx] : null;
                pred.ReceiveExternalTarget(target); // ◀ 既存の受け口
            }

            // TempJob メモリはここで破棄（次フレームに再確保）
            if (_grid.IsCreated)
            {
                _grid.Dispose();
                _gridAllocated = false;
            }

            if (_predPos.IsCreated)
            {
                _predPos.Dispose();
            }

            if (_predSense.IsCreated)
            {
                _predSense.Dispose();
            }

            if (_preyPos.IsCreated)
            {
                _preyPos.Dispose();
            }

            if (_nearest.IsCreated)
            {
                _nearest.Dispose();
            }
        }

        private void AllocateFrameBuffers(int predCount, int preyCount)
        {
            // 前フレーム残りを一応破棄
            if (_predPos.IsCreated) _predPos.Dispose();
            if (_predSense.IsCreated) _predSense.Dispose();
            if (_preyPos.IsCreated) _preyPos.Dispose();
            if (_nearest.IsCreated) _nearest.Dispose();

            _predPos = new NativeArray<float2>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _predSense = new NativeArray<float>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _preyPos = new NativeArray<float2>(preyCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            _nearest = new NativeArray<int>(predCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        }

        private void CompactLists()
        {
            for (var i = _predators.Count - 1; i >= 0; i--)
                if (_predators[i] == null)
                    _predators.RemoveAt(i);
            for (var j = _preys.Count - 1; j >= 0; j--)
                if (_preys[j] == null)
                    _preys.RemoveAt(j);
        }

        // ===== ジョブ =====
        [BurstCompile]
        private struct BuildGridJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> preyPositions;
            [ReadOnly] public float cellSize;
            public NativeParallelMultiHashMap<long, int>.ParallelWriter gridWriter;

            public void Execute(int index)
            {
                var p = preyPositions[index];
                var cx = (int)math.floor(p.x / cellSize);
                var cy = (int)math.floor(p.y / cellSize);
                var key = MakeKey(cx, cy);
                gridWriter.Add(key, index);
            }
        }

        [BurstCompile]
        private struct NearestJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float2> predatorPositions;
            [ReadOnly] public NativeArray<float> predatorSense;
            [ReadOnly] public NativeArray<float2> preyPositions;
            [ReadOnly] public float cellSize;
            [ReadOnly] public NativeParallelMultiHashMap<long, int> grid;

            public NativeArray<int> outNearest; // -1 = 該当なし

            public void Execute(int i)
            {
                var p = predatorPositions[i];
                var sense = predatorSense[i];
                var senseSq = (sense <= 0f) ? float.MaxValue : sense * sense;

                var cx = (int)math.floor(p.x / cellSize);
                var cy = (int)math.floor(p.y / cellSize);
                var r = math.max(0, (int)math.ceil(sense / cellSize));

                var best = float.PositiveInfinity;
                var bestIdx = -1;

                NativeParallelMultiHashMapIterator<long> it;
                int preyIndex;

                for (var y = cy - r; y <= cy + r; y++)
                {
                    for (var x = cx - r; x <= cx + r; x++)
                    {
                        var key = MakeKey(x, y);
                        if (grid.TryGetFirstValue(key, out preyIndex, out it))
                        {
                            do
                            {
                                var q = preyPositions[preyIndex];
                                var d2 = math.lengthsq(q - p);
                                if (d2 <= senseSq && d2 < best)
                                {
                                    best = d2;
                                    bestIdx = preyIndex;
                                }
                            } while (grid.TryGetNextValue(out preyIndex, ref it));
                        }
                    }
                }

                outNearest[i] = bestIdx;
            }
        }

        // 64bit キー化（cx,cy）→ long
        private static long MakeKey(int x, int y) => ((long)x << 32) ^ (uint)y;
    }
}