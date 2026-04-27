using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservables
    {
        public static ICollectionObservable<IStateNode> ObservableChildrenRecursive(this IStateNode source)
            => new CollectionOperator<IStateNode>(receiver => new RecursiveChildrenObservable(source, receiver));

        public static ICollectionObservable<IStateNode> ObservableChildren(this IStateNode source)
            => new CollectionOperator<IStateNode>(receiver => new ChildrenObservable(source, receiver));

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineState(this ISetObservable<IStateNode> source)
            => new CombineStateObservable(Settings.DefaultObservationContext, source);
    }

    public static class StateObservers
    {
        public static IDisposable SubscribeRecursive(this IStateNode source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableChildrenRecursive().ObservableDistinct().ObservableCombineState().Subscribe(onOperation, onError, onDispose);

        public static IDisposable SubscribeRecursive(this IStateNode source, Func<IStateNode, IValueObservable<bool>> filter, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableChildrenRecursive().ObservableWhere(filter).ObservableDistinct().ObservableCombineState().Subscribe(onOperation, onError, onDispose);
    }
}