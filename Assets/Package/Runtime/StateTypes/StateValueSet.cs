using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using FofX.Serialization;

using ObserveThing;

using SimpleJSON;

namespace FofX.Stateful
{
    public struct StateSetOperation<T> : IStateOperation
    {
        public IStateNode source { get; set; }
        public OpType opType { get; set; }
        public T element { get; set; }
        public uint elementId { get; set; }

        object IStateOperation.param => element;
        public IStateNode child => null;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={element}";
        }
    }

    public interface IStateValueSet : IEnumerable, IStateNode, ICollectionObservable
    {
        Type elementType { get; }
        int Count { get; }
        bool Add(object element);
        bool Remove(object element);
        bool Contains(object element);
        void Clear();
    }

    public class StateValueSet<T> : ObservableSetBase<T>,
        IStateValueSet,
        ISetObservable<T>,
        IEnumerable<T>
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }

        public int Count => GetCountInternal();
        public bool derived => _deriveStream != null;

        int IStateNode.childCount => 0;
        IEnumerable<IStateNode> IStateNode.children => EmptyChildren();

        Type IStateValueSet.elementType => typeof(T);

        private IDisposable _deriveStream;

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => GetElementsInternal().Select(x => x.Key).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetElementsInternal().Select(x => x.Key).GetEnumerator();

        private Action<StateValueSet<T>> _initializer;

        public StateValueSet() : this(default(Action<StateValueSet<T>>)) { }

        public StateValueSet(params T[] elements) : this(x =>
        {
            foreach (var element in elements)
                x.Add(element);
        })
        { }

        public StateValueSet(Action<StateValueSet<T>> initializer) : base(null)
        {
            _initializer = initializer;
        }

        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            _initializer?.Invoke(this);
            initialized = true;
            ((IStateNode)this).PostInitialize();
        }

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
            _initializer?.Invoke(this);
            initialized = true;
        }

        void IStateNode.PostInitialize() { }

        public void CopyTo(IStateNode copyTo)
        {
            var target = (StateValueSet<T>)copyTo;

            foreach (var toRemove in target.Except(GetElementsInternal().Select(x => x.Key)).ToArray())
                target.Remove(toRemove);

            foreach (var toAdd in GetElementsInternal().Select(x => x.Key).Except(target).ToArray())
                target.Add(toAdd);
        }

        public bool Add(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return AddInternal(element);
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return RemoveInternal(element);
        }

        public bool Contains(T element)
            => ContainsInternal(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            ClearInternal();
        }

        public void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            Clear();

            _initializer?.Invoke(this);

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public void FromJSON(JSONNode json)
        {
            if (derived)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            if (json == null)
            {
                Reset();
                return;
            }

            JSONArray array = (JSONArray)json;
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            Clear();

            foreach (var value in array.Values)
                Add(serializer.fromJSON(value));
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();
            SerializationPair<T> serializer = JSONSerialization.GetSerializer<T>();

            foreach (var element in GetElementsInternal())
                array.Add(serializer.toJSON(element.Key));

            return array;
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        public void Derive(IListObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: item => AddInternal(item),
                onRemove: item => RemoveInternal(item),
                immediate: true
            );
        }

        bool IStateValueSet.Add(object element)
            => Add((T)element);

        bool IStateValueSet.Remove(object element)
            => Remove((T)element);

        bool IStateValueSet.Contains(object element)
            => Contains((T)element);

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
        }

        public IStateNode GetChild(string name)
            => throw new NotImplementedException();

        public bool TryGetChild(string name, out IStateNode child)
        {
            child = default;
            return false;
        }

        public IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new SetObserver<T>(
                onAdd: (id, element) => observer.OnNext(new StateSetOperation<T>() { source = this, elementId = id, element = element, opType = OpType.Add }),
                onRemove: (id, element) => observer.OnNext(new StateSetOperation<T>() { source = this, elementId = id, element = element, opType = OpType.Remove }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}