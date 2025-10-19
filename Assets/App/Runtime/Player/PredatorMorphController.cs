using System;
using System.Reflection;
using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player
{
    /// <summary>
    /// 捕食で蓄積した栄養と現在のGenome値を基に、Predatorを自動変形させるコントローラ。
    /// - rootMorph から開始し、current の nextMorphs を順に評価して最初に条件を満たしたものへ進化
    /// - 変形時に OnMorphed(prev, next) を発火（VFX/サウンド/ビジュアル側のフック用）
    /// - Debug用に ForceMorphById / AddNutrition を用意
    /// </summary>
    [RequireComponent(typeof(PredatorAgent))]
    public class PredatorMorphController : MonoBehaviour
    {
        [Header("Morph Graph")] [Tooltip("最初に適用する形態（ルートノード）")]
        public PredatorMorphProfile rootMorph;

        [Tooltip("検索のために全形態を（任意で）並べておくと高速。未設定なら木を辿って検索します。")]
        public PredatorMorphProfile[] allMorphs;

        [Header("Auto Evolve")] [Tooltip("捕食で条件を満たしたら自動で次形態に進む")]
        public bool autoPickFirstEligible = true;

        [Tooltip("1フレームに複数段進化を許可するか（trueなら一気に駆け上がる）")]
        public bool allowMultipleStepsPerFrame = false;

        [Header("Progress / State (read-only)")] [SerializeField]
        private string _currentMorphId;

        [SerializeField] private float _totalNutritionAbsorbed;

        public string currentMorphId => _currentMorphId;
        public float totalNutritionAbsorbed => _totalNutritionAbsorbed;

        // イベント：変形発生時（prev=null の場合は最初の適用）
        public Action<PredatorMorphProfile, PredatorMorphProfile> OnMorphed;

        // 内部
        private PredatorAgent agent;
        private MetaballBody body;

        private void Awake()
        {
            agent = GetComponent<PredatorAgent>();
            body = GetComponentInChildren<MetaballBody>();
        }

        private void Start()
        {
            // 初回形態適用
            if (rootMorph && string.IsNullOrEmpty(_currentMorphId))
                ApplyMorph(rootMorph);
        }

        // ====== 外部API（PredatorAgent から呼ぶ） ======

        /// <summary>
        /// 捕食によって栄養が得られた際に呼び出されます。これにより、進化の条件が評価されます。
        /// </summary>
        /// <param name="nutritionGained">今回増加した栄養量。</param>
        /// <param name="preySnapshot">吸収前のPreyのゲノムのスナップショット。</param>
        public void NotifyAbsorb(float nutritionGained, Genome preySnapshot)
        {
            _totalNutritionAbsorbed += Mathf.Max(0f, nutritionGained);

            if (!autoPickFirstEligible) return;

            bool advanced;
            var guard = 0;
            do
            {
                advanced = TryAdvance();
                guard++;
            } while (allowMultipleStepsPerFrame && advanced && guard < 8);
        }

        /// <summary>
        /// デバッグ用：指定されたIDの形態に強制的に変形させます。
        /// </summary>
        /// <param name="id">変形先の形態ID。</param>
        public void ForceMorphById(string id)
        {
            var m = FindMorphById(id);
            if (m != null) ApplyMorph(m);
            else Debug.LogWarning($"[Morph] MorphId '{id}' が見つかりません。");
        }

        /// <summary>
        /// デバッグ用：栄養を追加し、即座に進化の判定を行います。
        /// </summary>
        /// <param name="amount">追加する栄養量。</param>
        /// <param name="evaluate">進化の判定を行うかどうか。</param>
        public void AddNutrition(float amount, bool evaluate = true)
        {
            _totalNutritionAbsorbed = Mathf.Max(0f, _totalNutritionAbsorbed + amount);
            if (evaluate) NotifyAbsorb(0f, null);
        }

        // ====== 内部ロジック ======

        private bool TryAdvance()
        {
            var cur = FindMorphById(_currentMorphId);
            if (!cur) cur = rootMorph;
            if (!cur) return false;

            var next = FindFirstEligible(cur);
            if (!next) return false;

            ApplyMorph(next);
            return true;
        }

        private void ApplyMorph(PredatorMorphProfile profile)
        {
            var prev = FindMorphById(_currentMorphId);

            _currentMorphId = profile.morphId;

            // --- 遺伝子の上書き/加算 ---
            var g = agent.genome;
            g.size = Mathf.Max(0.2f, g.size + profile.addSize);
            g.emission = Mathf.Clamp01(g.emission + profile.addEmission);
            if (Mathf.Abs(profile.addHueShift) > 0f)
                g.hue = Mathf.Repeat(g.hue + profile.addHueShift, 1f);

            g.biteDistance = Mathf.Max(0.05f, g.biteDistance + profile.biteDistanceAdd);

            // --- 能力倍率（捕食挙動/移動など） ---
            agent.suctionForce *= Mathf.Max(0.01f, profile.suctionMul == 0f ? 1f : profile.suctionMul);
            agent.accel *= Mathf.Max(0.01f, profile.accelMul == 0f ? 1f : profile.accelMul);
            agent.maxCarries = Mathf.Max(0, agent.maxCarries + profile.maxAttachmentsAdd); // 非Joint持ち替え版

            // --- 見た目反映（メタボール/マテリアル） ---
            if (body) body.ApplyGenome(g);
            if (profile.overrideMaterial && body && body.targetRenderer)
                body.targetRenderer.material = Instantiate(profile.overrideMaterial);

            // 物理パラメータ再適用（PredatorAgentの内部メソッドをリフレクションで呼ぶ）
            var method = typeof(PredatorAgent).GetMethod("ApplyPhenotype",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            method?.Invoke(agent, null);

            // コールバック（VFX/SE/ビジュアルEvolverが拾う想定）
            OnMorphed?.Invoke(prev, profile);
        }

        private PredatorMorphProfile FindFirstEligible(PredatorMorphProfile cur)
        {
            if (cur.nextMorphs == null || cur.nextMorphs.Length == 0) return null;

            foreach (var cand in cur.nextMorphs)
            {
                if (!cand) continue;
                if (cand.CheckEligible(agent.genome, _totalNutritionAbsorbed, cur.morphId))
                    return cand;
            }

            return null;
        }

        private PredatorMorphProfile FindMorphById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            // まず allMorphs から探す
            if (allMorphs != null && allMorphs.Length > 0)
            {
                foreach (var m in allMorphs)
                    if (m && m.morphId == id)
                        return m;
            }

            // 見つからなければ木を探索
            return FindInTree(rootMorph, id);
        }

        private PredatorMorphProfile FindInTree(PredatorMorphProfile node, string id)
        {
            if (!node) return null;
            if (node.morphId == id) return node;
            if (node.nextMorphs != null)
            {
                foreach (var n in node.nextMorphs)
                {
                    var hit = FindInTree(n, id);
                    if (hit) return hit;
                }
            }

            return null;
        }

#if UNITY_EDITOR
        // 見やすいように現在形態と累積栄養をインスペクタに表示
        private void OnValidate()
        {
            if (rootMorph && string.IsNullOrEmpty(_currentMorphId))
                _currentMorphId = rootMorph.morphId;
        }
#endif
    }
}