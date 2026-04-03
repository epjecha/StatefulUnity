using UnityEngine;

namespace FofX.Stateful
{
    public abstract class StateTransaction<T> where T : IStateNode
    {
        public void ExecuteTransaction(T state)
        {
            state.context.PauseExecution();
            Execute(state);
            state.context.ResumeExecution();
        }

        protected abstract void Execute(T state);
    }
}