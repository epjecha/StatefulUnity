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

        protected override void Reset()
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

        protected override void Reset()
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

            foreach (var toRemove in target.Except(GetElementsInternal().Select(x => x.Key)).ToArray())
                target.Remove(toRemove);

            foreach (var toAdd in GetElementsInternal().Select(x => x.Key).Except(target).ToArray())
                target.Add(toAdd);
        }
    }

    public abstract class ReadOnlyStateValueSet<T> : ObservableSetBase<T>,
        IReadOnlyStateValueSet,
        ISetObservable<T>,
        IEnumerable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public abstract bool isView { get; }

        public int count => GetCountInternal();

        Type IReadOnlyStateValueSet.elementType => typeof(T);

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => GetElementsInternal().Select(x => x.Key).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetElementsInternal().Select(x => x.Key).GetEnumerator();

        public ReadOnlyStateValueSet() : base(null) { }

        protected virtual void InitializeInternal() { }

        protected override void SendOperation(ISetObserver<T> observer, SetOp<T> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(operation.isRemove ? OpType.Remove : OpType.Add, this, operation.element, operation.elementId)}");
            base.SendOperation(observer, operation);
        }

        public bool Contains(T element)
            => ContainsInternal(element);

        bool IReadOnlyStateValueSet.Contains(object element)
            => Contains((T)element);

        protected abstract void Reset();

        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            InitializeInternal();
            initialized = true;
            ((IStateNode)this).PostInitialize();
        }

        // IStateNode
        public void Initialize(IStateNode parent, string name)
        {
            if (name == null)
                throw new ArgumentNullException(nameof(name));

            if (initialized)
                throw new Exception($"{nodePath} has already been initialized");

            context = parent.context;
            root = parent.root;
            this.parent = parent;
            logger = parent.logger;
            nodeName = name;
            nodePath = $"{parent.nodePath}/{nodeName}";
            InitializeInternal();
            initialized = true;
        }

        void IStateNode.PostInitialize() { }

        void IStateNode.Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
        }

        IStateNode IStateNode.GetChild(string name)
            => throw new NotImplementedException();

        bool IStateNode.TryGetChild(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        public abstract void CopyTo(IStateNode copyTo);
        public abstract void FromJSON(JSONNode json);

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            foreach (var element in GetElementsInternal())
                array.Add(serializer.toJSON(element.Key));

            return array;
        }

        void IStateNode.Reset()
            => Reset();

        // IObserver<StateOperation>
        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = element, elementId = id }),
                onRemove: (id, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = element, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}