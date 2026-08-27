using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;

using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IReadOnlyStateDictionary : IEnumerable, IStateNode, IDictionaryObservable
    {
        Type keyType { get; }
        Type valueType { get; }
        int count { get; }
        IStateNode this[object key] { get; }
        IEnumerable keys { get; }
        IEnumerable<IStateNode> values { get; }
        bool TryGetValue(object key, out IStateNode value);
    }

    public interface IStateDictionary : IReadOnlyStateDictionary
    {
        IStateNode Add(object key);
        bool Remove(object key);
        IStateNode GetOrAdd(object key);
        void Clear();
    }

    public interface IStateDictionaryViewMutator<TKey, TValue>
    {
        TValue Add(TKey key);
        bool Remove(TKey key);
        TValue GetOrAdd(TKey key);
        void Clear();
    }

    public class StateDictionaryView<TKey, TValue> : ReadOnlyStateDictionary<TKey, TValue> where TValue : IStateNode, new()
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateDictionaryViewMutator<TKey, TValue>
        {
            public Func<TKey, TValue> add;
            public Func<TKey, bool> remove;
            public Func<TKey, TValue> getOrAdd;
            public Action clear;

            public TValue Add(TKey key)
                => add(key);

            public bool Remove(TKey key)
                => remove(key);

            public TValue GetOrAdd(TKey key)
                => getOrAdd(key);

            public void Clear()
                => clear();
        }

        public StateDictionaryView() : base()
        {
            _mutator = new Mutator()
            {
                add = AddInternal,
                remove = RemoveInternal,
                getOrAdd = GetOrAdd,
                clear = ClearInternal
            };
        }

        public void InitializeView(Action<IStateDictionaryViewMutator<TKey, TValue>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateDictionaryViewMutator<TKey, TValue>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            _subscription = initialize(_mutator);
        }

        private TValue GetOrAdd(TKey key)
        {
            if (TryGetValue(key, out var value))
                return value;

            return AddInternal(key);
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

            foreach (var child in values)
                child.Reset();
        }

        protected override void DisposeInternal()
        {
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateDictionary<TKey, TValue> : ReadOnlyStateDictionary<TKey, TValue>, IStateDictionary where TValue : IStateNode, new()
    {
        public override bool isView => false;
        private Action<StateDictionary<TKey, TValue>> _initializer;

        public StateDictionary() : this(default) { }
        public StateDictionary(Action<StateDictionary<TKey, TValue>> initializer = default) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            base.InitializeInternal();
            _initializer?.Invoke(this);
        }

        public TValue Add(TKey key)
            => AddInternal(key);

        public bool Remove(TKey key)
            => RemoveInternal(key);

        public TValue GetOrAdd(TKey key)
        {
            if (TryGetValue(key, out var value))
                return value;

            return AddInternal(key);
        }

        public void Clear()
            => ClearInternal();

        IStateNode IStateDictionary.GetOrAdd(object key)
            => GetOrAdd((TKey)key);

        IStateNode IStateDictionary.Add(object key)
            => AddInternal((TKey)key);

        bool IStateDictionary.Remove(object key)
            => RemoveInternal((TKey)key);

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
                Reset();
                return;
            }

            JSONObject dict = (JSONObject)json;
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            Reset();

            foreach (var value in dict)
                AddInternal(serializer.fromJSON(value.Key)).FromJSON(value.Value);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateDictionary<TKey, TValue>)copyTo;

            var toRemove = target.keys.Except(keys).ToArray();

            foreach (var keyToRemove in toRemove)
                target.Remove(keyToRemove);

            foreach (var kvpToCopy in this)
            {
                if (!target.TryGetValue(kvpToCopy.Key, out var child))
                    child = target.Add(kvpToCopy.Key);

                kvpToCopy.Value.CopyTo(child);
            }
        }
    }

    public abstract class ReadOnlyStateDictionary<TKey, TValue> : StateNode,
        IReadOnlyStateDictionary,
        IDictionaryObservable<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>
        where TValue : IStateNode, new()
    {
        public int count => _dictionary.Count;
        public TValue this[TKey key] => _dictionary[key];
        public IEnumerable<TKey> keys => _dictionary.Keys;
        public IEnumerable<TValue> values => _dictionary.Values;

        public Type keyType => typeof(TKey);
        public Type valueType => typeof(TValue);

        IEnumerable IReadOnlyStateDictionary.keys => keys;
        IEnumerable<IStateNode> IReadOnlyStateDictionary.values => values.Cast<IStateNode>();

        IStateNode IReadOnlyStateDictionary.this[object key] => this[(TKey)key];

        public override int childCount => count;
        public override IEnumerable<IStateNode> children => values.Cast<IStateNode>();

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => ((IEnumerable<KeyValuePair<TKey, TValue>>)_dictionary).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<KeyValuePair<TKey, TValue>>)_dictionary).GetEnumerator();

        private ObservableDictionary<TKey, TValue> _dictionary;

        public ReadOnlyStateDictionary() : base() { }

        protected override void InitializeInternal()
        {
            _dictionary = new ObservableDictionary<TKey, TValue>(context);
        }

        protected TValue AddInternal(TKey key)
        {
            TValue value = new TValue();

            if (value is IKeyedStateNode<TKey> keyedNode)
                keyedNode.AssignKey(key);

            value.Initialize(this, key.ToString(), attributes.Where(x => x is IInheritableStateAttribute inheritable && inheritable.inherit).ToArray());
            value.PostInitialize();

            logger.Trace(Utility.FormatOperationLog(OpType.Add, this, key, value));
            _dictionary.Add(key, value);

            return value;
        }

        protected bool RemoveInternal(TKey key)
        {
            if (!_dictionary.TryGetValueInternal(key, out var value))
                return false;

            value.Dispose();

            logger.Trace(Utility.FormatOperationLog(OpType.Remove, this, key, value));
            _dictionary.Remove(key);

            return true;
        }

        protected void ClearInternal()
        {
            foreach (var key in keys.ToArray())
                RemoveInternal(key);
        }

        public bool ContainsKey(TKey key)
            => _dictionary.ContainsKey(key);

        public bool ContainsValue(TValue value)
            => _dictionary.ContainsValue(value);

        public bool TryGetValue(TKey key, out TValue value)
            => _dictionary.TryGetValueInternal(key, out value);

        bool IReadOnlyStateDictionary.TryGetValue(object key, out IStateNode value)
        {
            if (TryGetValue((TKey)key, out var stateValue))
            {
                value = stateValue;
                return true;
            }

            value = default;
            return false;
        }

        protected override IStateNode GetChildInternal(string name)
            => _dictionary.First(x => x.Key.ToString() == name).Value;

        protected override bool TryGetChildInternal(string name, out IStateNode child)
        {
            child = _dictionary.FirstOrDefault(x => x.Key.ToString() == name).Value;
            return child != null;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject dict = new JSONObject();
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            foreach (var kvp in _dictionary)
                dict.Add(serializer.toJSON(kvp.Key), kvp.Value.ToJSON(filter));

            return dict;
        }

        protected override void DisposeInternal()
        {
            _dictionary.Dispose();
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, child = kvp.Value, elementId = id }),
                onRemove: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, child = kvp.Value, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = kvp.Key, child = kvp.Value, elementId = id }),
                onRemove: (id, kvp) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = kvp.Key, child = kvp.Value, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver<KeyValuePair<TKey, TValue>> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnAdd(id, kvp),
                onRemove: (id, kvp) => observer.OnRemove(id, kvp),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnAdd(id, kvp),
                onRemove: (id, kvp) => observer.OnRemove(id, kvp),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IDictionaryObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnAdd(id, new KeyValuePair<object, object>(kvp.Key, kvp.Value)),
                onRemove: (id, kvp) => observer.OnRemove(id, new KeyValuePair<object, object>(kvp.Key, kvp.Value)),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IDictionaryObserver<TKey, TValue> observer, bool immediate = false, uint? priority = null)
            => _dictionary.Subscribe(observer, immediate, priority);
    }
}