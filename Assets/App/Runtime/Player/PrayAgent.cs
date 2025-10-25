using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player
{
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(Collider2D))]
    public class PreyAgent : MonoBehaviour
    {
        [Tooltip("方向更新の平均間隔（秒）")] public float wanderInterval = 1.2f;
        [Tooltip("最大速度（クランプ）")] public float maxSpeed = 2.0f;
        [Tooltip("境界に近づいたときの軽い戻し（使わないなら0）")] public float boundsSoftPush = 0f;
        public Vector2 boundsCenter = Vector2.zero;
        public Vector2 boundsSize = new(30, 30);

        [Header("Debug")] public bool debugLogs = false;

        // ==== 内部 ====
        private Rigidbody2D rb;
        private Collider2D col;
        private float timer;

        // 捕獲状態バックアップ
        private bool wasKinematic;
        private bool wasColliderEnabled;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<Collider2D>();

            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            col.isTrigger = false;
            col.enabled = true;
        }

        private void OnEnable()
        {
            // グリッドJobマネージャに登録（存在すれば）
            if (EcoSystem.Instance) EcoSystem.Instance.Register(this);
        }

        private void OnDisable()
        {
            if (EcoSystem.Instance) EcoSystem.Instance.Unregister(this);
        }

        private const float WANDER_INTERVAL_MIN_FACTOR = 0.6f;
        private const float WANDER_INTERVAL_MAX_FACTOR = 1.4f;
        private const float MIN_VISCOSITY = 0.5f;
        private const float MIN_MAX_SPEED = 0.2f;

        private void Update()
        {
            UpdateWander();
            ClampVelocity();
            ApplyBoundsPush();
        }

        private void UpdateWander()
        {
            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = wanderInterval * Random.Range(WANDER_INTERVAL_MIN_FACTOR, WANDER_INTERVAL_MAX_FACTOR);
                var dir = Random.insideUnitCircle.normalized;
                rb.AddForce(dir, ForceMode2D.Force);
            }
        }

        private void ClampVelocity()
        {
            var maxV = Mathf.Max(MIN_MAX_SPEED, maxSpeed);
            if (rb.linearVelocity.sqrMagnitude > maxV * maxV)
                rb.linearVelocity = rb.linearVelocity.normalized * maxV;
        }

        private void ApplyBoundsPush()
        {
            if (boundsSoftPush > 0f)
            {
                var min = boundsCenter - boundsSize * 0.5f;
                var max = boundsCenter + boundsSize * 0.5f;
                var p = rb.position;
                var push = Vector2.zero;
                if (p.x < min.x) push.x += 1f;
                if (p.x > max.x) push.x -= 1f;
                if (p.y < min.y) push.y += 1f;
                if (p.y > max.y) push.y -= 1f;
                if (push != Vector2.zero)
                {
                    rb.AddForce(push.normalized * boundsSoftPush, ForceMode2D.Force);
                }
            }
        }

        // ==== 捕食フック ====
        /// <summary>
        /// Jointを使用しない方法で捕獲された際に呼び出され、物理的な挙動を停止させて運搬を容易にします。
        /// </summary>
        public void OnCapturedNonJoint()
        {
            if (debugLogs) Debug.Log($"[Prey] Captured (Non-Joint): {name}");
            if (!rb || !col) return;

            wasKinematic = rb.isKinematic;
            wasColliderEnabled = col.enabled;

            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.isKinematic = true;

            // 口元に保持する間は衝突を切る（めり込み爆発を避ける）
            col.enabled = false;
        }

        /// <summary>
        /// 運搬を中断して解放する必要がある場合に呼び出します。
        /// </summary>
        public void OnReleasedNonJoint()
        {
            if (!rb || !col) return;
            rb.isKinematic = wasKinematic;
            col.enabled = wasColliderEnabled;
        }

        /// <summary>
        /// 完全に捕食された際の処理を実行します。
        /// </summary>
        public void OnEaten()
        {
            if (debugLogs) Debug.Log($"[Prey] Eaten: {name}");
            Destroy(gameObject);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 境界確認用
            if (boundsSoftPush > 0f)
            {
                Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.15f);
                Gizmos.DrawWireCube(boundsCenter, boundsSize);
            }
        }
#endif
    }
}