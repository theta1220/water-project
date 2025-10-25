using App.Runtime.Framework;
using App.Runtime.Player.Param;
using UnityEngine;

namespace App.Runtime.Player.PredatorState
{
    public class PredatorMoveState : SimpleStateMachine.State
    {
        private PredatorAgent Predator { get; }

        public PredatorMoveState(PredatorAgent predator)
        {
            Predator = predator;
        }

        public override void OnUpdate()
        {
            Predator.UpdateHunting();
            Predator.ApplyDamping();

            // 移動による体力減少
            var damage = Predator.Param.healthDecayRate * Time.fixedDeltaTime;
            Predator.TakeDamage(damage);

            Predator.Param.wobblePhase += Predator.Rb.linearVelocity.magnitude * Predator.Param.headWobbleFrequency *
                                          Time.fixedDeltaTime;
        }
    }
}