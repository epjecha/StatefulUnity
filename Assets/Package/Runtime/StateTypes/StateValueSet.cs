using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using FofX.Serialization;

using ObserveThing;

using SimpleJSON;

namespace FofX.Stateful
{
    public interface IReadOnlyStateValueSet : IEnumerable, IStateNode, ICollectionObservable
    {
        Type elementType { get; }
        int count { get; }
        bool Contains(object element);
    }

    public interface IStateValueSet : IReadOnlyStateValueSet
    {
        bool Add(object element);
        bool Remove(object element);
        void Clear();
    }

    public interface IStateValueSetViewMutator<T>
    {
        bool Add(T value);
        bool Remove(T value);
        void Clear();
    }

    public class StateValueSetView<T> : ReadOnlyStateValueSet<T>
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateValueSetViewMutator<T>
        {
            public Func<T, bool> add;
            public Func<T, bool> remove;
            public Action clear;

            public bool Add(T value)
                => add(value);

            public bool Remove(T value)
                => remove(value);

            public void Clear()
                => clear();
        }

        public StateValueSetView() : base()
        {
            _mutator = new Mutator()
            {
                add = AddInternal,
                remove = RemoveInternal,
                clear = ClearInternal
            };
        }

        public void InitializeView(Action<IStateValueSetViewMutator<T>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateValueSetViewMutator<T>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            _subscription = initialize(_mutator);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            logger.Warning($"{nodePath} is a view. \'CopyTo\' will be ignored.");
        }

        public override void FromJSON(JSONNode json)
        {
            logger.Warning($"{nodePath} is a view. \'FromJSON\' will be ignored.");
        }

        public override void Reset()
        {
            logger.Warning($"{nodePath} is a view. \'Reset\' will be ignored for this object. Children will be reset.");
        }

        protected override void DisposeInternal()
        {
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateValueSet<T> : ReadOnlyStateValueSet<T>, IStateValueSet
    {
        public override bool isView => false;
        private Action<StateValueSet<T>> _initializer;

        public StateValueSet() : this(default(Action<StateValueSet<T>>)) { }
        public StateValueSet(params T[] elements) : this(x =>
        {
            foreach (var element in elements)
                x.Add(element);
        })
        { }

        public StateValueSet(Action<StateValueSet<T>> initializer) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            base.InitializeInternal();
            _initializer?.Invoke(this);
        }

        public bool Add(T element)
        {
            return AddInternal(element);
        }

        public bool Remove(T element)
        {
            return RemoveInternal(element);
        }

        public void Clear()
        {
            ClearInternal();
        }

        bool IStateValueSet.Add(object element)
            => Add((T)element);

        bool IStateValueSet.Remove(object element)
            => Remove((T)element);

        public override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Resetting {nodePath}");
            Clear();
            _initializer?.Invoke(this);
        }

        public override void FromJSON(JSONNode json)
        {
            if (json == null)
            {
                Clear();
                return;
            }

            JSONArray array = (JSONArray)json;
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            Clear();

            foreach (var value in array.Values)
                Add(serializer.fromJSON(value));
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueSet<T>)copyTo;

            foreach (var toRemove in target.Except(this).ToArray())
                target.Remove(toRemove);

            foreach (var toAdd in this.Except(target).ToArray())
                target.Add(toAdd);
        }
    }

    public abstract class ReadOnlyStateValueSet<T> : StateNode,
        IReadOnlyStateValueSet,
        ISetObservable<T>,
        IEnumerable<T>
    {
        public int count => _set.count;

        Type IReadOnlyStateValueSet.elementType => typeof(T);

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_set).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_set).GetEnumerator();

        protected ObservableSet<T> _set;

        public ReadOnlyStateValueSet() : base() { }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override void InitializeInternal()
        {
            _set = new ObservableSet<T>(context);
        }

        protected bool AddInternal(T element)
        {
            if (_set.Contains(element))
                return false;

            logger.Trace(Utility.FormatOperationLog(OpType.Add, this, element));
            return _set.Add(element);
        }

        protected bool RemoveInternal(T element)
        {
            if (!_set.Contains(element))
                return false;

            logger.Trace(Utility.FormatOperationLog(OpType.Remove, this, element));
            return _set.Remove(element);
        }

        protected void ClearInternal()
        {
            foreach (var element in _set.ToArray())
                RemoveInternal(element);
        }

        public bool Contains(T element)
            => _set.Contains(element);

        bool IReadOnlyStateValueSet.Contains(object element)
            => Contains((T)element);

        protected override IStateNode GetChildInternal(string name)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            foreach (var element in _set)
                array.Add(serializer.toJSON(element));

            return array;
        }

        protected override void DisposeInternal()
        {
            _set.Dispose();
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = element, elementId = id }),
                onRemove: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = element, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = element, elementId = id }),
                onRemove: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = element, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver<T> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ISetObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnAdd(id, element),
                onRemove: (id, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ISetObserver<T> observer, bool immediate = false, uint? priority = null)
            => _set.Subscribe(observer, immediate, priority);
    }
}