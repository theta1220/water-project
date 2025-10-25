using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player.PredatorState
{
    public class PredatorAIMoveState : SimpleStateMachine.State
    {
        private static readonly int IsDead = Animator.StringToHash("isDead");

        private PredatorAgent Predator { get; }

        private float targetTimer;
        private float wanderTimer;
        private Vector2 wanderTargetDir;
        private Vector2 aiMoveDir;
        private Collider2D currentTarget;

        public PredatorAIMoveState(PredatorAgent predator)
        {
            Predator = predator;
        }

        public override void OnEnter()
        {
            ResetWander();
        }

        public override void OnUpdate()
        {
            var dir = Vector2.zero;

            // フロッキングの計算
            var flockingVector = Predator.Separation * Predator.separationWeight +
                                 Predator.Alignment * Predator.alignmentWeight +
                                 Predator.Cohesion * Predator.cohesionWeight;

            // 外部ターゲットを優先
            if (Predator.Param.useExternalTarget && Predator.ExternalTarget)
            {
                currentTarget = Predator.ExternalTarget ? Predator.ExternalTarget.GetComponent<Collider2D>() : null;
            }
            else
            {
                targetTimer -= Time.fixedDeltaTime;
                if (targetTimer <= 0)
                {
                    targetTimer = Predator.Param.targetRefreshInterval;
                    currentTarget = Predator.FindClosestPrey();
                }
            }

            if (currentTarget)
            {
                var toPrey = (Vector2)currentTarget.transform.position - Predator.Rb.position;
                Predator.Rb.AddForce(toPrey.normalized * Predator.Param.suctionForce, ForceMode2D.Force);
                dir = toPrey.normalized + flockingVector * 0.5f; // ターゲットがいる場合はフロッキングの影響を減らす
            }
            else
            {
                wanderTimer -= Time.fixedDeltaTime;
                if (wanderTimer <= 0) ResetWander();
                var blend = 1f - Mathf.Exp(-Predator.Param.wanderStrength * Time.fixedDeltaTime);
                if (blend > 0f)
                {
                    aiMoveDir = Vector2.Lerp(aiMoveDir, wanderTargetDir, blend);
                }

                if (aiMoveDir.sqrMagnitude < 0.0001f)
                    aiMoveDir = wanderTargetDir;

                aiMoveDir = aiMoveDir.normalized;
                dir = aiMoveDir + flockingVector;
            }

            if (dir == Vector2.zero) ResetWander();
            Predator.ApplyDrive(dir, 1f, ForceMode2D.Force);

            Predator.UpdateHunting();
            Predator.ApplyDamping();

            // 移動による体力減少
            var damage = Predator.Param.healthDecayRate * Time.fixedDeltaTime;
            Predator.TakeDamage(damage);

            Predator.Param.wobblePhase += Predator.Rb.linearVelocity.magnitude * Predator.Param.headWobbleFrequency * Time.fixedDeltaTime;

            Predator.Animator.SetBool(IsDead, !Predator.weakPoint | Predator.Animator.GetBool(IsDead));
        }

        private void ResetWander()
        {
            wanderTimer = Predator.Param.wanderInterval * Random.Range(0.7f, 1.3f);
            var baseDir = wanderTargetDir.sqrMagnitude > 0.0001f ? wanderTargetDir : (aiMoveDir.sqrMagnitude > 0.0001f ? aiMoveDir : Random.insideUnitCircle);
            if (baseDir == Vector2.zero)
                baseDir = Vector2.up;
            baseDir = baseDir.normalized;

            var jitterAngle = Predator.Param.wanderJitter * 90f; // wanderJitter=1 => ±90°
            var angle = Random.Range(-jitterAngle, jitterAngle);
            var rot = Quaternion.AngleAxis(angle, Vector3.forward);
            wanderTargetDir = ((Vector2)(rot * baseDir)).normalized;

            if (aiMoveDir == Vector2.zero)
                aiMoveDir = wanderTargetDir;
        }
    }
}