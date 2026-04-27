using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservables
    {
        public static ICollectionObservable<IStateNode> ObservableChildrenRecursive(this IStateNode source, ObservationContext context = default)
            => new CollectionOperator<IStateNode>(context, receiver => new RecursiveChildrenObservable(source, receiver));

        public static ICollectionObservable<IStateNode> ObservableChildren(ObservationContext context, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableSelectMany(x => x.ObservableChildren(), context);

        public static ICollectionObservable<IStateNode> ObservableChildren(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableSelectMany(x => x.ObservableChildren());

        public static ICollectionObservable<IStateNode> ObservableChildren(IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableSelectMany(x => x.ObservableChildren(), context);

        public static ICollectionObservable<IStateNode> ObservableChildren(this IStateNode source, ObservationContext context = default)
            => new CollectionOperator<IStateNode>(context, receiver => new ChildrenObservable(source, receiver));

        public static ObserveThing.IObservable<IStateOperation> CombineOperations(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableCombineOperations();

        public static ObserveThing.IObservable<IStateOperation> CombineOperations(ObservationContext context, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context);

        public static ObserveThing.IObservable<IStateOperation> CombineOperations(IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context);

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(this ISetObservable<IStateNode> source, ObservationContext context = default)
            => new CombineStateObservable(context, source);
    }

    public static class StateObservers
    {
        public static IDisposable SubscribeRecursive(this IStateNode source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableChildrenRecursive().ObservableDistinct().ObservableCombineOperations().Subscribe(onOperation, onError, onDispose);

        public static IDisposable SubscribeRecursive(this IStateNode source, Func<IStateNode, IValueObservable<bool>> filter, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableChildrenRecursive().ObservableWhere(filter).ObservableDistinct().ObservableCombineOperations().Subscribe(onOperation, onError, onDispose);
    }
}