using App.Runtime.Framework;
using UnityEngine;

namespace App.Runtime.State
{
    public class GameOverState : SimpleStateMachine.State
    {
        public override void OnUpdate()
        {
            Parent.ChangeState(new InitializeState());
        }
    }
}