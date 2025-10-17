using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace App.Runtime.Player
{
    /// <summary>
    /// 空間グリッドで Prey をセル分割し、各 Predator には近傍セルだけを探索して最近傍 Prey を配布するマネージャ。
    /// ・物理APIは触らない（計算のみ並列）
    /// ・Parallel.For による並列最適化
    /// </summary>
    public class EcosystemGridManager : MonoBehaviour
    {
        public static EcosystemGridManager Instance { get; private set; }

        [Header("Grid Settings")] [Tooltip("グリッドのセル一辺（ワールド単位）")]
        public float cellSize = 4f;

        [Tooltip("索敵半径の全体上限（PredatorのsenseRangeとminを取る）")]
        public float globalSenseCap = 50f;

        [Header("Parallel")] [Tooltip("Predatorの並列処理に Parallel.For を使う")]
        public bool useParallelFor = true;

        // 登録中個体
        private readonly List<PredatorAgent> _predators = new();
        private readonly List<PreyAgent> _preys = new();

        // 毎フレーム作る位置バッファ（GC削減）
        private Vector2[] _predPosBuf = Array.Empty<Vector2>();
        private float[] _predSenseBuf = Array.Empty<float>();
        private Vector2[] _preyPosBuf = Array.Empty<Vector2>();

        // グリッド：セルキー -> そのセルの Prey インデックス一覧
        // キーは (x,y) を 64bit 化： ((long)x << 32) ^ (uint)y
        private readonly Dictionary<long, List<int>> _cellToPreys = new(1024);
        private readonly Stack<List<int>> _listPool = new(); // リスト再利用

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

        // ====== 外部登録口 ======
        public void Register(PredatorAgent p)
        {
            if (p && !_predators.Contains(p)) _predators.Add(p);
        }

        public void Unregister(PredatorAgent p)
        {
            if (!p) return;
            _predators.Remove(p);
        }

        public void Register(PreyAgent p)
        {
            if (p && !_preys.Contains(p)) _preys.Add(p);
        }

        public void Unregister(PreyAgent p)
        {
            if (!p) return;
            _preys.Remove(p);
        }

        private void FixedUpdate()
        {
            if (_predators.Count == 0 || _preys.Count == 0) return;

            CompactLists();
            if (_predators.Count == 0 || _preys.Count == 0)
            {
                ClearGrid();
                return;
            }

            EnsureBuffers();

            // 1) 位置 & 索敵半径 バッファ化
            for (var i = 0; i < _predators.Count; i++)
            {
                var p = _predators[i];
                _predPosBuf[i] = p ? (Vector2)p.transform.position : new Vector2(1e9f, 1e9f);
                _predSenseBuf[i] = p ? Mathf.Min(p.genome.senseRange, globalSenseCap) : 0f;
            }

            for (var j = 0; j < _preys.Count; j++)
                _preyPosBuf[j] = _preys[j] ? (Vector2)_preys[j].transform.position : new Vector2(1e9f, 1e9f);

            // 2) グリッド再構築（Prey をセルへ）
            RebuildGrid();

            // 3) Predatorごとに近傍セルだけ探索
            var count = _predators.Count;
            var nearestIdx = new int[count];

            if (useParallelFor && count >= 4)
            {
                Parallel.For(0, count, i => FindNearestForPred(i, nearestIdx));
            }
            else
            {
                for (var i = 0; i < count; i++) FindNearestForPred(i, nearestIdx);
            }

            // 4) 配布
            for (var i = 0; i < count; i++)
            {
                var pred = _predators[i];
                if (!pred) continue;
                var idx = nearestIdx[i];
                var target = (idx >= 0 && idx < _preys.Count) ? _preys[idx] : null;
                pred.ReceiveExternalTarget(target);
            }
        }

        // ====== 近傍探索 ======
        private void FindNearestForPred(int i, int[] nearestIdx)
        {
            var pred = _predators[i];
            if (pred == null)
            {
                nearestIdx[i] = -1;
                return;
            }

            var p = _predPosBuf[i];
            var sense = _predSenseBuf[i];
            var senseSq = (sense <= 0f) ? float.PositiveInfinity : sense * sense;

            // Predator の位置セル
            (var cx, var cy) = WorldToCell(p);

            // 何セル先まで見るか（Chebyshev 近傍）
            var r = Mathf.Max(0, Mathf.CeilToInt(sense / Mathf.Max(0.0001f, cellSize)));

            var bestSq = float.PositiveInfinity;
            var bestIdx = -1;

            // 周囲 (2r+1)^2 セルを走査
            for (var y = cy - r; y <= cy + r; y++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    var key = Key(x, y);
                    if (!_cellToPreys.TryGetValue(key, out var list) || list == null) continue;

                    // このセルの Prey をチェック
                    for (var k = 0; k < list.Count; k++)
                    {
                        var preyIndex = list[k];
                        var q = _preyPosBuf[preyIndex];
                        var d2 = (q - p).sqrMagnitude;
                        if (d2 <= senseSq && d2 < bestSq)
                        {
                            bestSq = d2;
                            bestIdx = preyIndex;
                        }
                    }
                }
            }

            nearestIdx[i] = bestIdx;
        }

        // ====== グリッド再構築 ======
        private void RebuildGrid()
        {
            ClearGrid();

            for (var j = 0; j < _preys.Count; j++)
            {
                var prey = _preys[j];
                if (!prey) continue;

                (var cx, var cy) = WorldToCell(_preyPosBuf[j]);
                var key = Key(cx, cy);

                if (!_cellToPreys.TryGetValue(key, out var list) || list == null)
                {
                    list = (_listPool.Count > 0) ? _listPool.Pop() : new List<int>(16);
                    list.Clear();
                    _cellToPreys[key] = list;
                }

                list.Add(j);
            }
        }

        private void ClearGrid()
        {
            // 使ったリストをプールに戻す
            foreach (var kv in _cellToPreys)
            {
                if (kv.Value != null)
                {
                    kv.Value.Clear();
                    _listPool.Push(kv.Value);
                }
            }

            _cellToPreys.Clear();
        }

        // ====== ユーティリティ ======
        private (int, int) WorldToCell(Vector2 w)
        {
            var inv = 1f / Mathf.Max(0.0001f, cellSize);
            var x = Mathf.FloorToInt(w.x * inv);
            var y = Mathf.FloorToInt(w.y * inv);
            return (x, y);
        }

        private static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;

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
    }
}