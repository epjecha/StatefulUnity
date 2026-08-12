using System;
using System.Collections.Generic;
using System.Linq;
using ObserveThing;

namespace FofX.Stateful
{
    public static class StateObservables
    {
        public static ISetObservable<IStateNode> ObservableChildrenRecursive(this ISetObservable<IStateNode> source)
            => source.ObservableSelectMany(x => x.ObservableChildrenRecursive()).ObservableDistinct();

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source[0].context, source).ObservableChildrenRecursive();

        public static ISetObservable<IStateNode> ObservableChildrenRecursive(this IStateNode source)
            => new SetOperator<IStateNode>(source.context, receiver => new RecursiveChildrenObservable(source, receiver));

        public static ISetObservable<IStateNode> ObservableChildren(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source[0].context, source).ObservableSelectMany(x => x.ObservableChildren()).ObservableDistinct();

        public static ISetObservable<IStateNode> ObservableChildren(this IStateNode source)
            => new SetOperator<IStateNode>(source.context, receiver => new ChildrenObservable(source, receiver));

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineRecursive(params IStateNode[] source)
            => new ObservableSet<IStateNode>(source[0].context, source).ObservableCombineRecursive();

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineRecursive(this ISetObservable<IStateNode> source)
            => source.ObservableChildrenRecursive().ObservableCombine();

        public static ObserveThing.IObservable<IStateOperation> ObservableCombineRecursive(this IStateNode source)
            => new ObservableSet<IStateNode>(source.context, source).ObservableCombineRecursive();
    }
}