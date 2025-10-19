using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_JOBS
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
#endif

namespace App.Runtime.Player
{
    /// <summary>
    /// Predator / Prey を登録管理し、FixedUpdate 毎に「各Predatorに最も近いPrey」を並列計算して配布する。
    /// 物理APIは主スレッド限定なので、並列化は“計算（索敵）”のみに限定。
    /// </summary>
    public class EcosystemManager : MonoBehaviour
    {
        // === Singleton ===
        public static EcosystemManager Instance { get; private set; }

        [Header("Parallel Settings")] [Tooltip("Jobs が利用可能なら Job System を使う")]
        public bool useJobsIfAvailable = true;

        [Tooltip("Jobs を使わない場合に Parallel.For を使う")]
        public bool useParallelForFallback = true;

        [Header("Tick Settings")] [Tooltip("索敵半径の上限（PredatorごとのsenseRangeと比較して min を採用）")]
        public float globalSenseCap = 50f;

        // 登録リスト
        private readonly List<PredatorAgent> _predators = new();
        private readonly List<PreyAgent> _preys = new();

        // バッファ（GC削減）
        private Vector2[] _predPosBuf = Array.Empty<Vector2>();
        private float[] _predSenseBuf = Array.Empty<float>();
        private Vector2[] _preyPosBuf = Array.Empty<Vector2>();

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

        // === 外部登録口 ===
        /// <summary>
        /// Predatorをシステムに登録します。
        /// </summary>
        /// <param name="p">登録するPredatorAgent。</param>
        public void Register(PredatorAgent p)
        {
            if (p && !_predators.Contains(p)) _predators.Add(p);
        }

        /// <summary>
        /// Predatorをシステムから登録解除します。
        /// </summary>
        /// <param name="p">登録解除するPredatorAgent。</param>
        public void Unregister(PredatorAgent p)
        {
            if (!p) return;
            _predators.Remove(p);
        }

        /// <summary>
        /// Preyをシステムに登録します。
        /// </summary>
        /// <param name="p">登録するPreyAgent。</param>
        public void Register(PreyAgent p)
        {
            if (p && !_preys.Contains(p)) _preys.Add(p);
        }

        /// <summary>
        /// Preyをシステムから登録解除します。
        /// </summary>
        /// <param name="p">登録解除するPreyAgent。</param>
        public void Unregister(PreyAgent p)
        {
            if (!p) return;
            _preys.Remove(p);
        }

        private void FixedUpdate()
        {
            if (_predators.Count == 0 || _preys.Count == 0) return;

            // 1) null 掃除
            CompactLists();
            if (_predators.Count == 0 || _preys.Count == 0) return;

            // 2) 位置と索敵半径をバッファ化（主スレッドで安全に参照）
            EnsureBuffers();
            for (var i = 0; i < _predators.Count; i++)
            {
                var p = _predators[i];
                _predPosBuf[i] = p ? (Vector2)p.transform.position : new Vector2(1e9f, 1e9f);
                _predSenseBuf[i] = p ? Mathf.Min(p.genome.senseRange, globalSenseCap) : 0f;
            }

            for (var j = 0; j < _preys.Count; j++)
                _preyPosBuf[j] = _preys[j] ? (Vector2)_preys[j].transform.position : new Vector2(1e9f, 1e9f);

            // 3) 並列で最近傍インデックスを求める
            var nearestIdx = new int[_predators.Count];

#if UNITY_JOBS
        if (useJobsIfAvailable)
        {
            using var predPos = new NativeArray<float2>(_predators.Count, Allocator.TempJob);
            using var predSens = new NativeArray<float>(_predators.Count, Allocator.TempJob);
            using var preyPos = new NativeArray<float2>(_preys.Count, Allocator.TempJob);
            using var outIdx = new NativeArray<int>(_predators.Count, Allocator.TempJob);

            for (int i = 0; i < _predators.Count; i++)
            {
                predPos[i] = _predPosBuf[i];
                predSens[i] = _predSenseBuf[i] <= 0 ? float.MaxValue : _predSenseBuf[i];
            }
            for (int j = 0; j < _preys.Count; j++)
                preyPos[j] = _preyPosBuf[j];

            var job = new ClosestJob
            {
                predators = predPos,
                senses = predSens,
                preys = preyPos,
                outIdx = outIdx
            };
            JobHandle handle = job.Schedule(_predators.Count, 64);
            handle.Complete();

            outIdx.CopyTo(nearestIdx);
        }
        else
#endif
            {
                if (useParallelForFallback)
                {
                    Parallel.For(0, _predators.Count, i =>
                    {
                        var pi = _predPosBuf[i];
                        var sense = _predSenseBuf[i];
                        var senseSq = sense <= 0 ? float.PositiveInfinity : sense * sense;

                        var bestSq = float.PositiveInfinity;
                        var best = -1;
                        for (var j = 0; j < _preys.Count; j++)
                        {
                            var pj = _preyPosBuf[j];
                            var d2 = (pj - pi).sqrMagnitude;
                            if (d2 <= senseSq && d2 < bestSq)
                            {
                                bestSq = d2;
                                best = j;
                            }
                        }

                        nearestIdx[i] = best;
                    });
                }
                else
                {
                    for (var i = 0; i < _predators.Count; i++)
                    {
                        var pi = _predPosBuf[i];
                        var sense = _predSenseBuf[i];
                        var senseSq = sense <= 0 ? float.PositiveInfinity : sense * sense;

                        var bestSq = float.PositiveInfinity;
                        var best = -1;
                        for (var j = 0; j < _preys.Count; j++)
                        {
                            var pj = _preyPosBuf[j];
                            var d2 = (pj - pi).sqrMagnitude;
                            if (d2 <= senseSq && d2 < bestSq)
                            {
                                bestSq = d2;
                                best = j;
                            }
                        }

                        nearestIdx[i] = best;
                    }
                }
            }

            // 4) 結果を各 Predator に配布（主スレッド）
            for (var i = 0; i < _predators.Count; i++)
            {
                var pred = _predators[i];
                if (!pred) continue;

                var idx = nearestIdx[i];
                var target = (idx >= 0 && idx < _preys.Count) ? _preys[idx] : null;
                pred.ReceiveExternalTarget(target);
            }
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

        private void EnsureBuffers()
        {
            if (_predPosBuf.Length != _predators.Count) _predPosBuf = new Vector2[_predators.Count];
            if (_predSenseBuf.Length != _predators.Count) _predSenseBuf = new float[_predators.Count];
            if (_preyPosBuf.Length != _preys.Count) _preyPosBuf = new Vector2[_preys.Count];
        }

#if UNITY_JOBS
    // Job: 各Predatorに最も近いPreyインデックスを outIdx に書く
    struct ClosestJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float2> predators;
        [ReadOnly] public NativeArray<float>  senses;
        [ReadOnly] public NativeArray<float2> preys;
        public NativeArray<int> outIdx;

        public void Execute(int i)
        {
            float2 pi = predators[i];
            float sense = senses[i];
            float senseSq = sense <= 0 ? float.MaxValue : sense * sense;

            float bestSq = float.PositiveInfinity;
            int best = -1;

            for (int j = 0; j < preys.Length; j++)
            {
                float2 d = preys[j] - pi;
                float d2 = math.lengthsq(d);
                if (d2 <= senseSq && d2 < bestSq) { bestSq = d2; best = j; }
            }
            outIdx[i] = best; // -1=該当なし
        }
    }
#endif
    }
}