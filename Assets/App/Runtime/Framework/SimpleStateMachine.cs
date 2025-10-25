namespace App.Runtime.Framework
{
    public class SimpleStateMachine
    {
        public abstract class State
        {
            public SimpleStateMachine Parent { get; set; }
            public virtual void OnEnter() { }
            public virtual void OnExit() { }
            public virtual void OnUpdate() { }
        }
        
        private State _currentState;

        public State CurrentState => _currentState;
        
        public void ChangeState(State newState)
        {
            _currentState?.OnExit();
            _currentState = newState;
            newState.Parent = this;
            _currentState?.OnEnter();
        }

        public void Update()
        {
            _currentState?.OnUpdate();
        }
    }
}