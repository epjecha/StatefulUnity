using System;
using System.Collections.Generic;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservables
    {
        public static ICollectionObservable<IStateNode> ObservableDeepChildren(this IStateNode source)
            => new CollectionObservableFactory<IStateNode>(receiver => new DeepChildrenObservable(source, receiver));

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineOperations(this ISetObservable<IStateNode> source)
            => new ObservableFactory<IStateOperation>(receiver => new CombineOperationsObservable(Settings.DefaultObservationContext, source, receiver));
    }

    public static class StateObservers
    {
        public static void test()
        {
            var state = new StateList<StateValue<int>>();

            state.SubscribeRecursive(
                ops =>
                {

                },
                onError: exc =>
                {

                },
                onDispose: () =>
                {

                }
            );
        }

        public static IDisposable SubscribeRecursive(this IStateNode source, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableDeepChildren().ObservableDistinct().ObservableCombineOperations().Subscribe(onOperation, onError, onDispose);

        public static IDisposable SubscribeRecursive(this IStateNode source, Func<IStateNode, IValueObservable<bool>> filter, Action<IReadOnlyList<IStateOperation>> onOperation = default, Action<Exception> onError = default, Action onDispose = default)
            => source.ObservableDeepChildren().ObservableWhere(filter).ObservableDistinct().ObservableCombineOperations().Subscribe(onOperation, onError, onDispose);
    }
}