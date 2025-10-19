namespace App.Runtime.Framework
{
    public class SimpleStateMachine
    {
        public abstract class State
        {
            public virtual void OnEnter() { }
            public virtual void OnExit() { }
            public virtual void OnUpdate() { }
        }
        
        private State _currentState;
        
        public void ChangeState(State newState)
        {
            _currentState?.OnExit();
            _currentState = newState;
            _currentState?.OnEnter();
        }

        public void Update()
        {
            _currentState?.OnUpdate();
        }
    }
}