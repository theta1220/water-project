using UnityEngine;

namespace App.Runtime.Player
{
    [CreateAssetMenu(menuName = "Evo/Predator Morph Profile")]
    public class PredatorMorphProfile : ScriptableObject
    {
        [Header("Identity")] public string morphId = "base";
        public string displayName = "Base";

        [TextArea] public string description;

        [Header("Prerequisites (all must pass)")]
        public float minTotalNutrition = 0f; // 総栄養のしきい値

        public float minSize = 0f; // 遺伝子サイズ >=
        public float minSpeed = 0f; // 遺伝子スピード >=
        public float minSense = 0f; // 索敵範囲 >=
        public float minEmission = 0f; // 発光度 >=
        public float maxViscosity = 999f; // 粘度 <=
        public string requiredParentMorphId = ""; // 親形態の指定（空なら不要）

        [Header("Visual Overwrites (optional delta)")]
        public float addSize = 0f; // 基本遺伝子に加算（適用時）

        public float addEmission = 0f;
        [Range(-1f, 1f)] public float addHueShift = 0f; // -1..1 で色相回転
        public float bodyScaleMul = 1f; // 見た目スケール倍率
        public Material overrideMaterial; // 任意のマテリアル差し替え

        [Header("Gameplay Tweaks (applied on enter)")]
        public float suctionMul = 1f; // 吸引力倍率

        public float accelMul = 1f; // 推進力倍率
        public float biteDistanceAdd = 0f; // 噛みつき距離加算
        public int maxAttachmentsAdd = 0; // ラッチ上限増加

        [Header("Next branches")] public PredatorMorphProfile[] nextMorphs; // 次に到達可能な候補

        /// <summary>
        /// この形態に進化可能かどうかをチェックします。
        /// </summary>
        /// <param name="g">現在のゲノム</param>
        /// <param name="totalNutrition">総栄養</param>
        /// <param name="currentParentId">現在の親の形態ID</param>
        /// <returns>進化可能であればtrue</returns>
        public bool CheckEligible(Genome g, float totalNutrition, string currentParentId)
        {
            if (!string.IsNullOrEmpty(requiredParentMorphId) &&
                requiredParentMorphId != currentParentId) return false;

            if (totalNutrition < minTotalNutrition) return false;
            if (g.size < minSize) return false;
            if (g.speed < minSpeed) return false;
            if (g.senseRange < minSense) return false;
            if (g.emission < minEmission) return false;
            if (g.viscosity > maxViscosity) return false;
            return true;
        }
    }
}