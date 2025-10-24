using App.Runtime.Framework;
using App.Runtime.State;
using UnityEngine;

namespace App.Runtime
{
    public class GameMain : MonoBehaviour
    {
        private SimpleStateMachine _stateMachine;
        
        private void Awake()
        {
            _stateMachine = new SimpleStateMachine();
            _stateMachine.ChangeState(new InitializeState());
        }
        
        private void Update()
        {
            _stateMachine.Update();
        }
    }
}