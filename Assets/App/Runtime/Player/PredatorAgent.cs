using System.Collections.Generic;
using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class PredatorAgent : MonoBehaviour
    {
        [Header("Genome")] public Genome genome = new Genome();
        public float decayFactor = 0.99f;

        [Header("Visual")] public MetaballBody body;

        [Header("Visual Effects")] public ParticleSystem eatParticlePrefab;

        [Header("Hunting")] public LayerMask preyMask;
        public float eatCooldown = 0.25f;
        [Range(0, 1)] public float preyWeight = 0.35f;
        [Range(0, 1)] public float mutationStrength = 0.3f;
        public float eatImpulse = 5f; // 捕食時のインパルス
        public float bitePadding = 0.05f;
        public float suctionForce = 6f;

        [Header("Health")] public float maxHealth = 100f;
        [SerializeField] private float currentHealth;
        public float healthDecayRate = 1f; // 1秒あたりの体力減少量
        public float collisionDamage = 10f; // 衝突1回あたりのダメージ
        public float healthRecoverOnEat = 20f; // 捕食1回あたりの回復量

        [Header("Movement")] 
        public float minSpeed = 2f;
        public float accel = 20f;
        public float dragLinear = 2f;
        public float boostMultiplier = 1.5f;
        public float boostDecayMultiplier = 0.95f;
        public float headWobbleFrequency = 5f; // 揺れの速さ
        public float moveWobbleAmplitude = 0.5f; // 揺れの大きさ（移動）
        private float wobblePhase; // 揺れの位相

        [Header("AI Settings")] public bool aiControlled = true;
        public float wanderInterval = 1.4f;
        public float wanderJitter = 0.6f;
        public float wanderStrength = 1.0f;
        public float targetRefreshInterval = 0.25f;

        [Header("Bounds")] public bool useBoundsAvoidance = false;
        public Vector2 boundsCenter = Vector2.zero;
        public Vector2 boundsSize = new(30, 30);
        public float boundsMargin = 1.0f;

        public enum CaptureMode
        {
            InstantEat,
            CarryWithoutJoint
        }

        [Header("Capture (Non-Joint)")] public CaptureMode captureMode = CaptureMode.CarryWithoutJoint;
        public int maxCarries = 3;
        public float carryOffset = 0.25f;
        public float carrySnap = 8f;
        public float digestPerSecond = 0.25f;
        public float digestNutritionScale = 0.1f;
        public float digestEmissionScale = 0.02f;

        [Header("Sensor")] public CircleCollider2D mouthSensor;
        public bool autoCreateMouthSensor = true;

        [Header("External Target")] [Tooltip("グリッドマネージャなど外部システムからターゲットを受け取る場合に使用")]
        public bool useExternalTarget = true;

        private PreyAgent _externalTarget;

        [Header("Debug")] public bool debugLogs = false;

        [HideInInspector] public Vector2 separation;
        [HideInInspector] public Vector2 alignment;
        [HideInInspector] public Vector2 cohesion;

        public void ReceiveFlockingVectors(Vector2 separation, Vector2 alignment, Vector2 cohesion)
        {
            this.separation = separation;
            this.alignment = alignment;
            this.cohesion = cohesion;
        }

        [Header("Flocking")]
        public float separationWeight = 1.5f;
        public float alignmentWeight = 1.0f;
        public float cohesionWeight = 1.0f;
        public float flockingRange = 5f;
        // === 内部 ===
        private Rigidbody2D rb;
        public Rigidbody2D Rb => rb;
        private CircleCollider2D selfCol;
        private PredatorMorphController morphCtl;

        private float eatTimer;
        private Vector2 aiMoveDir;
        private float wanderTimer;
        private float targetTimer;
        private Collider2D currentTarget;
        private ContactFilter2D filter;
        private readonly List<Collider2D> _hits = new(32);

        // 捕獲状態管理
        private class Carry
        {
            public PreyAgent prey;
            public Genome snapshot;
            public float nutrition;
            public float absorbed01;
            public Vector2 localSlot;
        }

        private readonly List<Carry> _carries = new();

        // ===================== ライフサイクル =====================
        private void OnEnable()
        {
            if (FlockingGridJobManager.Instance != null)
            {
                FlockingGridJobManager.Instance.Register(this);
            }
        }

        private void OnDisable()
        {
            if (FlockingGridJobManager.Instance != null)
            {
                FlockingGridJobManager.Instance.Unregister(this);
            }
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            selfCol = GetComponent<CircleCollider2D>();
            morphCtl = GetComponent<PredatorMorphController>();
            if (!body) body = GetComponentInChildren<MetaballBody>();

            SetupMouthSensor();
            SetupContactFilter();

            currentHealth = maxHealth;
        }

        private void Start()
        {
            ApplyPhenotype();
            ResetWander();
        }

        private void Update()
        {
            if (eatTimer > 0) eatTimer -= Time.deltaTime;
            if (body) body.ApplyGenome(genome);
        }

        private const float ROTATION_SMOOTH_SPEED = 10f;

        private void FixedUpdate()
        {
            if (aiControlled) UpdateAI();
            else HandlePlayerInputFixed();

            UpdateHunting();
            ApplyDamping();
            UpdateCarryAndDigest();
            UpdateRotation();

            // --- ゲノム減衰 --- 
            bool isBoosting = !aiControlled && Input.GetButton("Jump"); // プレイヤー操作時のみJumpキーをチェック
            var currentDecayFactor = isBoosting ? decayFactor * boostDecayMultiplier : decayFactor;
            genome.Decay(currentDecayFactor);

            // 移動による体力減少
            var damage = rb.linearVelocity.magnitude * healthDecayRate * Time.fixedDeltaTime;
            TakeDamage(damage);

            wobblePhase += rb.linearVelocity.magnitude * headWobbleFrequency * Time.fixedDeltaTime;

            if (aiControlled && rb.linearVelocity.magnitude < minSpeed)
            {
                var dir = rb.linearVelocity.normalized;
                if (dir == Vector2.zero)
                {
                    dir = Random.insideUnitCircle.normalized;
                }
                rb.linearVelocity = dir * minSpeed;
            }
        }

        private void UpdateRotation()
        {
            if (rb.linearVelocity.sqrMagnitude > 0.001f)
            {
                var angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Lerp(
                    transform.rotation,
                    Quaternion.Euler(0f, 0f, angle - 90f), // -90° は「上方向」を前方にしたい場合
                    ROTATION_SMOOTH_SPEED * Time.fixedDeltaTime // 補間スピード（値を小さくすると滑らか）
                );
            }
        }

        // ===================== AI / Movement =====================
        private void HandlePlayerInputFixed()
        {
            var h = Input.GetAxisRaw("Horizontal");
            var v = Input.GetAxisRaw("Vertical");
            var thrust = Input.GetButton("Jump") ? boostMultiplier : 1f;
            ApplyDrive(new Vector2(h, v).normalized, thrust);
        }

        private void UpdateAI()
        {
            var dir = Vector2.zero;

            // フロッキングの計算
            var flockingVector = separation * separationWeight +
                                 alignment * alignmentWeight +
                                 cohesion * cohesionWeight;

            // 外部ターゲットを優先
            if (useExternalTarget && _externalTarget)
            {
                currentTarget = _externalTarget ? _externalTarget.GetComponent<Collider2D>() : null;
            }
            else
            {
                targetTimer -= Time.fixedDeltaTime;
                if (targetTimer <= 0)
                {
                    targetTimer = targetRefreshInterval;
                    currentTarget = FindClosestPrey(genome.senseRange);
                }
            }

            if (currentTarget)
            {
                var toPrey = (Vector2)currentTarget.transform.position - rb.position;
                rb.AddForce(toPrey.normalized * suctionForce, ForceMode2D.Force);
                dir = toPrey.normalized + flockingVector * 0.5f; // ターゲットがいる場合はフロッキングの影響を減らす
            }
            else
            {
                wanderTimer -= Time.fixedDeltaTime;
                if (wanderTimer <= 0) ResetWander();
                aiMoveDir = (aiMoveDir + Random.insideUnitCircle * wanderJitter).normalized;
                dir = aiMoveDir + flockingVector;
            }

            if (dir == Vector2.zero) ResetWander();
            ApplyDrive(dir, 1f);
        }

        private void ResetWander()
        {
            wanderTimer = wanderInterval * Random.Range(0.7f, 1.3f);
            aiMoveDir = Random.insideUnitCircle.normalized;
        }

        private void ApplyDrive(Vector2 dir, float thrustMul)
        {
            dir = dir.normalized;

            // 揺らぎの計算
            var wobble = Mathf.Sin(wobblePhase) * moveWobbleAmplitude;
            var perpendicular = new Vector2(-dir.y, dir.x);
            var wobbleDir = dir + perpendicular * wobble;

            var force = accel * genome.speed * thrustMul;
            rb.AddForce(wobbleDir.normalized * force, ForceMode2D.Force);
        }

        private void ApplyDamping()
        {
            var visc = Mathf.Max(0.1f, genome.viscosity);
            var k = Mathf.Clamp01(1f - Time.fixedDeltaTime * dragLinear * visc * 0.4f);
            rb.linearVelocity *= k;
        }

        // ===================== Hunting =====================
        private void UpdateHunting()
        {
            var myR = GetApproxRadius(selfCol);
            var biteR = genome.biteDistance + myR + bitePadding;

            _hits.Clear();
            if (mouthSensor)
            {
                mouthSensor.transform.position = transform.position;
                mouthSensor.radius = biteR;
                mouthSensor.Overlap(filter, _hits);
            }
            else
            {
                var found = Physics2D.OverlapCircleAll(transform.position, biteR, preyMask);
                _hits.AddRange(found);
            }

            if (_hits.Count == 0 || eatTimer > 0) return;

            Collider2D nearest = null;
            var best = float.MaxValue;
            Vector2 pos = transform.position;
            foreach (var h in _hits)
            {
                if (!h) continue;
                var d = ((Vector2)h.transform.position - pos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = h;
                }
            }

            if (nearest && nearest.TryGetComponent(out PreyAgent prey))
            {
                Bite(prey);
                eatTimer = eatCooldown;
            }
        }

        private void Bite(PreyAgent prey)
        {
            if (captureMode == CaptureMode.InstantEat)
            {
                var preyGene = prey.GetGenomeForConsumption();
                genome.Absorb(preyGene, preyWeight, mutationStrength);
                var nutrition = prey.GetNutritionValue();
                genome.size = Mathf.Clamp(genome.size + nutrition * digestNutritionScale, 0.2f, 3);
                genome.emission = Mathf.Clamp01(genome.emission + nutrition * digestEmissionScale);
                rb.AddForce(transform.up * eatImpulse, ForceMode2D.Impulse);
                currentHealth = Mathf.Min(maxHealth, currentHealth + healthRecoverOnEat);
                ApplyPhenotype();
                morphCtl?.NotifyAbsorb(nutrition, preyGene);
                prey.OnEaten();
                Instantiate(eatParticlePrefab, prey.gameObject.transform.position, Quaternion.identity);
                return;
            }

            if (_carries.Count >= maxCarries) return;
            foreach (var c in _carries)
                if (c.prey == prey)
                    return;

            Vector2 slot = Quaternion.Euler(0, 0, (_carries.Count * (360f / Mathf.Max(1, maxCarries)))) *
                           (Vector2.right * carryOffset);
            prey.OnCapturedNonJoint();
            _carries.Add(new Carry
            {
                prey = prey,
                snapshot = prey.GetGenomeForConsumption(),
                nutrition = prey.GetNutritionValue(),
                absorbed01 = 0,
                localSlot = slot
            });
        }

        private void UpdateCarryAndDigest()
        {
            if (_carries.Count == 0) return;

            var dt = Time.fixedDeltaTime;
            var step = Mathf.Clamp01(digestPerSecond * dt);
            Vector2 mouth = transform.position;

            for (var i = _carries.Count - 1; i >= 0; i--)
            {
                var c = _carries[i];
                if (!c.prey)
                {
                    _carries.RemoveAt(i);
                    continue;
                }

                var tr = c.prey.transform;
                if (c.prey.TryGetComponent<Rigidbody2D>(out var preyRb))
                {
                    preyRb.isKinematic = true;
                    preyRb.interpolation = RigidbodyInterpolation2D.None;
                }

                var target = mouth + c.localSlot;
                var dist = Vector2.Distance(tr.position, target);
                if (dist > 0.05f)
                    tr.position = Vector2.Lerp(tr.position, target, 1f - Mathf.Exp(-carrySnap * dt));

                var prev = c.absorbed01;
                c.absorbed01 = Mathf.Clamp01(c.absorbed01 + step);
                var delta = c.absorbed01 - prev;

                if (delta > 0)
                {
                    var partialWeight = preyWeight * delta;
                    genome.Absorb(c.snapshot, partialWeight, mutationStrength * delta);
                    var partNut = c.nutrition * delta;
                    genome.size = Mathf.Clamp(genome.size + partNut * digestNutritionScale, 0.2f, 3);
                    genome.emission = Mathf.Clamp01(genome.emission + partNut * digestEmissionScale);
                    currentHealth = Mathf.Min(maxHealth, currentHealth + healthRecoverOnEat * delta);
                    ApplyPhenotype();
                    morphCtl?.NotifyAbsorb(partNut, c.snapshot);
                }

                if (c.absorbed01 >= 1f)
                {
                    c.prey.OnEaten();
                    _carries.RemoveAt(i);
                }
            }
        }

        // ===================== 外部ターゲットAPI =====================
        /// <summary>
        /// 外部システム（例：グリッドマネージャ）からターゲットを受け取ります。
        /// </summary>
        /// <param name="prey">ターゲットとなるPreyAgent</param>
        public void ReceiveExternalTarget(PreyAgent prey)
        {
            _externalTarget = prey;
        }

        // ===================== Utility =====================
        private Collider2D FindClosestPrey(float radius)
        {
            var list = Physics2D.OverlapCircleAll(transform.position, radius, preyMask);
            if (list == null || list.Length == 0) return null;

            Collider2D nearest = null;
            var best = float.MaxValue;
            Vector2 pos = transform.position;
            foreach (var c in list)
            {
                if (!c) continue;
                var d = ((Vector2)c.transform.position - pos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }

            return nearest;
        }

        private void ApplyPhenotype()
        {
            if (body) body.ApplyGenome(genome);
            if (!selfCol) selfCol = GetComponent<CircleCollider2D>();
            selfCol.radius = 0.25f * genome.size;
            rb.mass = Mathf.Clamp(genome.size, 0.3f, 8f);
            rb.linearDamping = Mathf.Lerp(0.3f, 2f, Mathf.InverseLerp(0.5f, 3f, genome.viscosity));
        }

        private void SetupMouthSensor()
        {
            if (!mouthSensor && autoCreateMouthSensor)
            {
                var go = new GameObject("MouthSensor");
                go.transform.SetParent(transform, false);
                mouthSensor = go.AddComponent<CircleCollider2D>();
                mouthSensor.isTrigger = true;
            }
        }

        private void SetupContactFilter()
        {
            if (preyMask == 0) preyMask = LayerMask.GetMask("Prey");
            filter = new ContactFilter2D { useLayerMask = true, layerMask = preyMask, useTriggers = true };
        }

        private static float GetApproxRadius(Collider2D col)
        {
            if (!col) return 0.25f;
            if (col is CircleCollider2D cc)
                return cc.radius * Mathf.Abs(cc.transform.lossyScale.x);
            var b = col.bounds;
            return Mathf.Max(b.extents.x, b.extents.y);
        }

        /// <summary>
        /// ダメージを受け、体力を減少させます。体力が0以下になると消滅します。
        /// </summary>
        /// <param name="damage">受けるダメージ量。</param>
        public void TakeDamage(float damage)
        {
            currentHealth -= damage;
            if (currentHealth <= 0)
            {
                // 死亡処理
                if (LifeSpawner.Instance != null) LifeSpawner.Instance.NotifyDeath(true); // Predatorなのでtrue
                else Debug.LogWarning("LifeSpawner.Instance is null. Cannot notify death.");
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter2D(Collision2D other)
        {
            if (other.gameObject.TryGetComponent<PredatorAgent>(out var otherPredator))
            {
                // 自分自身との衝突は無視
                if (otherPredator == this) return;

                // ダメージを与える
                TakeDamage(collisionDamage);
            }
        }

        public float GetHealthNormalized()
        {
            return currentHealth / maxHealth;
        }


#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0, 1, 0.3f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, genome != null ? genome.senseRange : 3f);
            var myR = selfCol ? GetApproxRadius(selfCol) : 0.25f;
            var bite = (genome != null ? genome.biteDistance : 0.45f) + myR + bitePadding;
            Gizmos.color = new Color(1, 0.6f, 0.1f, 0.25f);
            Gizmos.DrawWireSphere(transform.position, bite);
        }
#endif
    }
}