namespace FofX.Stateful
{
    public interface IKeyedStateNode<T> : IStateNode
    {
        void AssignKey(T key);
    }
}