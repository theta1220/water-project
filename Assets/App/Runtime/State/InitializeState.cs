using App.Runtime.Common;
using App.Runtime.Framework;

namespace App.Runtime.State
{
    public class InitializeState : SimpleStateMachine.State
    {
        public override void OnUpdate()
        {
            InGameContents.Instance.Initialize();
            Parent.ChangeState(new MainState());
        }
    }
}