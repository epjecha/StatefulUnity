using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservables
    {
        public static ISetObservable<IStateNode> ObservableChildrenRecursive(ObservationContext context, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context);

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableChildrenRecursive();

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context);

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(this ISetObservable<IStateNode> source, ObservationContext context = default)
            => source.ObservableSelectMany(x => x.ObservableChildrenRecursive(context), context).ObservableDistinct(context);

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(this IStateNode source, ObservationContext context = default)
            => new SetOperator<IStateNode>(context, receiver => new RecursiveChildrenObservable(source, receiver));

        public static ISetObservable<IStateNode> ObservableChildren(ObservationContext context, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableSelectMany(x => x.ObservableChildren(context), context).ObservableDistinct(context);

        public static ISetObservable<IStateNode> ObservableChildren(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableSelectMany(x => x.ObservableChildren()).ObservableDistinct();

        public static ISetObservable<IStateNode> ObservableChildren(IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableSelectMany(x => x.ObservableChildren(context), context).ObservableDistinct(context);

        public static ISetObservable<IStateNode> ObservableChildren(this IStateNode source, ObservationContext context = default)
            => new SetOperator<IStateNode>(context, receiver => new ChildrenObservable(source, receiver));

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableCombineOperations();

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(ObservationContext context, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context);

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context);

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(this ISetObservable<IStateNode> source, ObservationContext context = default)
            => new CombineStateObservable(context, source);


        public static IDisposable SubscribeOperations(ObservationContext context, Action<IReadOnlyList<IStateOperation>> onOperation, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context).Subscribe(onOperation);

        public static IDisposable SubscribeOperations(Action<IReadOnlyList<IStateOperation>> onOperation, params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableCombineOperations().Subscribe(onOperation);

        public static IDisposable SubscribeOperations(Action<IReadOnlyList<IStateOperation>> onOperation, IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context).Subscribe(onOperation);

        public static IDisposable SubscribeOperations(ObservationContext context, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context).Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperations(Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableCombineOperations().Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperations(IEnumerable<IStateNode> source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableCombineOperations(context).Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperations(this IStateNode source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, ObservationContext context = default)
            => source.Subscribe(onOperation, onError, onDispose, immediate);
            

        public static IDisposable SubscribeOperationsRecursive(ObservationContext context, Action<IReadOnlyList<IStateOperation>> onOperation, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context).ObservableCombineOperations(context).Subscribe(onOperation);

        public static IDisposable SubscribeOperationsRecursive(Action<IReadOnlyList<IStateOperation>> onOperation, params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableChildrenRecursive().ObservableCombineOperations().Subscribe(onOperation);

        public static IDisposable SubscribeOperationsRecursive(Action<IReadOnlyList<IStateOperation>> onOperation, IEnumerable<IStateNode> source, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context).ObservableCombineOperations(context).Subscribe(onOperation);

        public static IDisposable SubscribeOperationsRecursive(ObservationContext context, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, params IStateNode[] source)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context).ObservableCombineOperations(context).Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperationsRecursive(Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, params IStateNode[] source)
            => new ObservableSet<IStateNode>(source).ObservableChildrenRecursive().ObservableCombineOperations().Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperationsRecursive(IEnumerable<IStateNode> source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, ObservationContext context = default)
            => new ObservableSet<IStateNode>(context, source).ObservableChildrenRecursive(context).ObservableCombineOperations(context).Subscribe(onOperation, onError, onDispose, immediate);

        public static IDisposable SubscribeOperationsRecursive(this IStateNode source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false, ObservationContext context = default)
            => source.ObservableChildrenRecursive(context).ObservableCombineOperations(context).Subscribe(onOperation, onError, onDispose, immediate);
    }
}