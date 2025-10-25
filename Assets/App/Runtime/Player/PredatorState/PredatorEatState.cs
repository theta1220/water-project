using App.Runtime.Common;
using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player.PredatorState
{
    public class PredatorEatState : SimpleStateMachine.State
    {
        private static readonly int IsEat = Animator.StringToHash("isEat");
        private PredatorAgent Predator { get; }
        private Prey Prey { get; }

        public PredatorEatState(PredatorAgent predator, Prey prey)
        {
            Predator = predator;
            Prey = prey;
        }

        public override void OnEnter()
        {
            var gameMaster = InGameContents.Instance.MasterContainer.GameMaster;
            
            if (IsMyCharacter())
            {
                var dynamicFOV = InGameContents.Instance.DynamicFOV;
                dynamicFOV.SetTargetFOV(gameMaster.HuningFOV);
                
                Predator.PlaySeHuntStart();
            }
            
            Predator.transform.SetParent(Prey.transform);
            Predator.Rb.bodyType = RigidbodyType2D.Kinematic;
            Predator.Animator.SetBool(IsEat, true);

            var targetPredator = Prey.GetComponentInParent<PredatorAgent>();
            if (targetPredator)
            {
                targetPredator.SineMove.SetSpeed(targetPredator.Param.huntedSineSpeed);
                targetPredator.SineMove.SetScale(targetPredator.Param.huntedSineScale);
                targetPredator.StateMachine.ChangeState(new PredatorEatenState(targetPredator));
            }
        }

        public override void OnExit()
        {
            if (IsMyCharacter())
            {
                var dynamicFOV = InGameContents.Instance.DynamicFOV;
                dynamicFOV.SetTargetFOV(0);
                
                Predator.PlaySeHuntEnd();
            }

            Predator.Rb.bodyType = RigidbodyType2D.Dynamic;
            Predator.OnProgress.Invoke();
            Predator.Rb.AddForce(Predator.transform.right * Predator.Param.huntedForce, ForceMode2D.Impulse);
            Predator.Rb.AddTorque(Predator.Param.huntedTorque, ForceMode2D.Impulse);
            Predator.Param.currentHealth = Mathf.Min(Predator.Param.maxHealth,
                Predator.Param.currentHealth + Predator.Param.healthRecoverOnEat);
            Predator.EmitEatParticle(Predator.transform.position);
            Predator.Animator.SetBool(IsEat, false);
        }

        public override void OnUpdate()
        {
            if (!Prey)
            {
                ToMove();
                return;
            }

            Prey.OnEaten(Predator);

            if (Predator.transform.parent != null)
            {
                var gameMaster = InGameContents.Instance.MasterContainer.GameMaster;
                var rotation = Predator.transform.localRotation;
                rotation = Quaternion.Lerp(rotation, Quaternion.identity, gameMaster.SmoothDamping);
                Predator.transform.localRotation = rotation;

                var position = Predator.transform.localPosition;
                position = Vector2.Lerp(position, Vector2.zero, gameMaster.SmoothDamping);
                Predator.transform.localPosition = position;
            }

            if (Prey.IsDead())
            {
                ToMove();
            }
        }

        private void ToMove()
        {
            if (Predator.AIControlled)
            {
                Predator.StateMachine.ChangeState(new PredatorAIMoveState(Predator));
            }
            else
            {
                Predator.StateMachine.ChangeState(new PredatorMoveState(Predator));
            }
        }

        private bool IsMyCharacter()
        {
            return Predator == InGameContents.Instance.MyPredator;
        }
    }
}