using App.Runtime.Common;
using App.Runtime.Framework;

namespace App.Runtime.State
{
    public class MainState : SimpleStateMachine.State
    {
        private int _progress = 0;

        public override void OnEnter()
        {
            var myPredator = InGameContents.Instance.MyPredator;
            myPredator.OnProgress.AddListener(OnProgress);
        }
        
        public override void OnExit()
        {
            var myPredator = InGameContents.Instance.MyPredator;
            myPredator.OnProgress.RemoveListener(OnProgress);
        }

        public override void OnUpdate()
        {
            var myPredator = InGameContents.Instance.MyPredator;
            if (myPredator == null)
            {
                Parent.ChangeState(new GameOverState());
                return;
            }
            
            var helthUI = InGameContents.Instance.Health;
            helthUI.SetHealth(myPredator.GetHealthNormalized());
            
            var gameProgressUI = InGameContents.Instance.GameProgress;
            var gameMaster = InGameContents.Instance.MasterContainer.GameMaster;
            gameProgressUI.SetProgress(_progress, gameMaster.MaxProgress);
            
            myPredator.Control();
        }

        private void OnProgress()
        {
            _progress++;
        }
    }
}