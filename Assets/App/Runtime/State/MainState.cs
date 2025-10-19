using App.Runtime.Common;
using App.Runtime.Framework;

namespace App.Runtime.State
{
    public class MainState : SimpleStateMachine.State
    {
        public override void OnUpdate()
        {
            var helthUI = InGameContents.Instance.HealthUI;
            var myPredator = InGameContents.Instance.MyPredator;
            if (helthUI != null && myPredator != null)
            {
                helthUI.SetHealth(myPredator.GetHealthNormalized());
            }
        }
    }
}