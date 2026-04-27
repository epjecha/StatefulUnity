
namespace FofX.Stateful
{
    public abstract class StateTransaction<T> where T : IStateNode
    {
        private T _state;

        public void ExecuteTransaction(T state)
        {
            _state = state;
            state.context.ExecuteBatchOperation(ExecuteInternal);
        }

        private void ExecuteInternal()
        {
            Execute(_state);
        }

        protected abstract void Execute(T state);
    }
}