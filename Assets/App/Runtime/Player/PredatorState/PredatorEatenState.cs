using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.Player.PredatorState
{
    public class PredatorEatenState : SimpleStateMachine.State
    {
        private static readonly int IsDead = Animator.StringToHash("isDead");
        private PredatorAgent Predator { get; }
        
        public PredatorEatenState(PredatorAgent predator)
        {
            Predator = predator;
        }
        
        public override void OnEnter()
        {
            Predator.Animator.SetBool(IsDead, true);
        }
    }
}