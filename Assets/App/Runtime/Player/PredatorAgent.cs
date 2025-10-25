using System.Collections.Generic;
using App.Runtime.Framework;
using App.Runtime.Player.Param;
using App.Runtime.Player.PredatorState;
using UnityEngine;
using UnityEngine.Events;

namespace App.Runtime.Player
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class PredatorAgent : MonoBehaviour
    {
        private static readonly int IsDead = Animator.StringToHash("isDead");
        
        [SerializeField] private PredatorParam _param;
        [SerializeField] private bool _aiControlled = true;
        [SerializeField] private LayerMask preyMask;
        [SerializeField] private ParticleSystem eatParticlePrefab;
        [SerializeField] private ParticleSystem damageParticlePrefab;
        [SerializeField] private Animator animator;
        [SerializeField] private ContactFilter2D mouthContact;
        [SerializeField] private PlayerSineMove sineMove;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip seHuntStart;
        [SerializeField] private AudioClip seHuntEnd;

        public CircleCollider2D mouthSensor;
        public bool autoCreateMouthSensor = true;
        
        public Prey weakPoint;
        
        public PredatorParam Param { get; private set; }
        public PreyAgent ExternalTarget { get; private set; }
        public Vector2 Separation { get; private set; }
        public Vector2 Alignment { get; private set; }
        public Vector2 Cohesion { get; private set; }
        
        public bool AIControlled => _aiControlled;
        public LayerMask PreyMask => preyMask;
        public Animator Animator => animator;
        public SimpleStateMachine StateMachine => stateMachine;
        public PlayerSineMove SineMove => sineMove;

        public void ReceiveFlockingVectors(Vector2 separation, Vector2 alignment, Vector2 cohesion)
        {
            this.Separation = separation;
            this.Alignment = alignment;
            this.Cohesion = cohesion;
        }

        [Header("Flocking")]
        public float separationWeight = 1.5f;
        public float alignmentWeight = 1.0f;
        public float cohesionWeight = 1.0f;
        public float flockingRange = 5f;

        public readonly UnityEvent OnProgress = new();
        
        // === 内部 ===
        private Rigidbody2D rb;
        public Rigidbody2D Rb => rb;
        private CircleCollider2D selfCol;

        private readonly List<Collider2D> _hits = new(32);
        private SimpleStateMachine stateMachine;

        private void OnDestroy()
        {
            if (FlockingSystem.Instance != null)
            {
                FlockingSystem.Instance.Unregister(this);
            }
        }

        private void Awake()
        {
            Param = Instantiate(_param);
        }

        private void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            rb.gravityScale = 0;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            selfCol = GetComponent<CircleCollider2D>();

            Param.currentHealth = Param.maxHealth;

            if (EcoSystem.Instance != null)
            {
                EcoSystem.Instance.Register(this);
            }
            
            if (FlockingSystem.Instance != null)
            {
                FlockingSystem.Instance.Register(this);
            }

            stateMachine = new SimpleStateMachine();
            if (AIControlled)
            {
                stateMachine.ChangeState(new PredatorAIMoveState(this));
            }
            else
            {
                stateMachine.ChangeState(new PredatorMoveState(this));
            }
        }

        private void FixedUpdate()
        {
            stateMachine.Update();
        }

        public void ApplyDrive(Vector2 dir, float boost, ForceMode2D forceMode)
        {
            if (dir.magnitude == 0 && Mathf.Approximately(boost, 0f))
            {
                return;
            }
            
            dir = dir.normalized;

            // 揺らぎの計算
            var wobble = Mathf.Sin(Param.wobblePhase) * Param.moveWobbleAmplitude;
            var perpendicular = new Vector2(-dir.y, dir.x);
            var wobbleDir = dir + perpendicular * wobble;

            var force = Param.accel + boost;
            var forward = dir;
            rb.AddForce(forward * force, forceMode);
            
            if (wobbleDir.sqrMagnitude < 0.0001f)
                wobbleDir = dir;
            var targetRotation = Quaternion.LookRotation(Vector3.forward, wobbleDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Param.rotationSpeed);
        }

        public void ApplyDamping()
        {
            var k = Mathf.Clamp01(1f - Time.fixedDeltaTime * Param.dragLinear);
            rb.linearVelocity *= k;
        }

        public void UpdateHunting()
        {
            if (!weakPoint)
            {
                return;
            }
            
            _hits.Clear();
            mouthSensor.Overlap(mouthContact, _hits);

            if (_hits.Count == 0) return;

            Collider2D nearest = null;
            var best = float.MaxValue;
            Vector2 pos = transform.position;
            foreach (var hit in _hits)
            {
                if (!hit) continue;
                if (hit.gameObject == weakPoint.gameObject) continue;
                
                var d = ((Vector2)hit.transform.position - pos).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = hit;
                }
            }

            if (nearest && nearest.TryGetComponent(out PreyAgent prey))
            {
                Bite(prey);
            }
            else if (nearest && nearest.TryGetComponent(out Prey pray))
            {
                Bite(pray);
            }
        }
        
        public void EmitEatParticle(Vector2 position)
        {
            Instantiate(eatParticlePrefab, position, Quaternion.identity);
        }
        
        public void EmitDamageParticle()
        {
            damageParticlePrefab.Play();
        }
        
        public bool IsEat()
        {
            return stateMachine.CurrentState is PredatorEatState or PredatorEatenState;
        }

        private void Bite(Prey prey)
        {
            var targetPredator = prey.gameObject.GetComponentInParent<PredatorAgent>();
            if (targetPredator.IsEat())
            {
                return;
            }
            stateMachine.ChangeState(new PredatorEatState(this, prey));
        }

        private void Bite(PreyAgent prey)
        {
            prey.OnEaten();
            OnProgress.Invoke();
            rb.AddForce(transform.up * Param.eatImpulse, ForceMode2D.Impulse);
            Param.currentHealth = Mathf.Min(Param.maxHealth, Param.currentHealth + Param.healthRecoverOnEat);
            Instantiate(eatParticlePrefab, prey.gameObject.transform.position, Quaternion.identity);
        }

        private void ToDie()
        {
            animator.SetBool(IsDead, true);
        }

        public void Die()
        {
            Destroy(gameObject);
        }

        public void ReceiveExternalTarget(PreyAgent prey)
        {
            ExternalTarget = prey;
        }

        // ===================== Utility =====================
        public Collider2D FindClosestPrey()
        {
            var radius = selfCol.radius;
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
            Param.currentHealth -= damage;
            if (Param.currentHealth <= 0)
            {
                // 死亡処理
                if (LifeSpawner.Instance != null) LifeSpawner.Instance.NotifyDeath(true); // Predatorなのでtrue
                else Debug.LogWarning("LifeSpawner.Instance is null. Cannot notify death.");
                ToDie();
            }
        }

        private void OnCollisionEnter2D(Collision2D other)
        {
            if (other.gameObject.TryGetComponent<PredatorAgent>(out var otherPredator))
            {
                // 自分自身との衝突は無視
                if (otherPredator == this) return;

                // ダメージを与える
                TakeDamage(Param.collisionDamage);
            }
        }

        public float GetHealthNormalized()
        {
            return Param.currentHealth / Param.maxHealth;
        }
        
        public void PlaySeHuntStart()
        {
            audioSource.PlayOneShot(seHuntStart);
        }
        
        public void PlaySeHuntEnd()
        {
            audioSource.PlayOneShot(seHuntEnd);
        }
    }
}
