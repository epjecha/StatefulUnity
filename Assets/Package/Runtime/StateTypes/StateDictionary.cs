using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;

using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IStateDictionary : IEnumerable, IStateNode, ICollectionObservable
    {
        Type keyType { get; }
        Type valueType { get; }
        int Count { get; }
        IStateNode this[object key] { get; }
        IEnumerable keys { get; }
        IEnumerable<IStateNode> values { get; }
        IStateNode Add(object key);
        bool Remove(object key);
        bool TryGetValue(object key, out IStateNode value);
        IStateNode GetOrAdd(object key);
        void Clear();
    }

    public struct StateDictionaryOperation<TKey, TValue> : IStateOperation where TValue : IStateNode
    {
        public IStateNode source { get; set; }
        public OpType opType { get; set; }
        public TKey key { get; set; }
        public TValue value { get; set; }
        public uint elementId { get; set; }

        object IStateOperation.param => key;
        public IStateNode child => value;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={key}";
        }
    }

    public interface IDerivedDictionaryAccess<TKey, TValue>
    {
        TValue Add(TKey key);
        bool Remove(TKey key);
        void Clear();
    }

    public class StateDictionary<TKey, TValue> : StateNode<IDictionaryObserver<TKey, TValue>, StateDictionaryOperation<TKey, TValue>>,
        IStateDictionary,
        IDictionaryObservable<TKey, TValue>,
        IEnumerable<KeyValuePair<TKey, TValue>>
        where TValue : IStateNode, new()
    {
        private class DerivedDictionaryAccess : IDerivedDictionaryAccess<TKey, TValue>
        {
            public Func<TKey, TValue> add;
            public Func<TKey, bool> remove;
            public Action clear;

            public TValue Add(TKey key)
                => add(key);

            public bool Remove(TKey key)
                => remove(key);

            public void Clear()
                => clear();
        }

        public int Count => _dictionary.Count;
        public TValue this[TKey key] => _dictionary[key];
        public IEnumerable<TKey> keys => _dictionary.Keys;
        public IEnumerable<TValue> values => _dictionary.Values;
        public override int childCount => _dictionary.Count;
        public override IEnumerable<IStateNode> children => _dictionary.Select(x => (IStateNode)x.Value);
        public override bool derived => _deriveStream != null;

        public Type keyType => typeof(TKey);
        public Type valueType => typeof(TValue);

        IEnumerable IStateDictionary.keys => keys;

        IEnumerable<IStateNode> IStateDictionary.values => _dictionary.Values.Select(x => (IStateNode)x);

        IStateNode IStateDictionary.this[object key] => _dictionary[(TKey)key];

        private IDisposable _deriveStream;

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => ((IEnumerable<KeyValuePair<TKey, TValue>>)_dictionary).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_dictionary).GetEnumerator();

        private Dictionary<TKey, TValue> _dictionary = new Dictionary<TKey, TValue>();
        private Dictionary<TKey, uint> _elementIds = new Dictionary<TKey, uint>();
        private CollectionIdProvider _idProvider;
        private Action<StateDictionary<TKey, TValue>> _initializer;

        public StateDictionary() : this(default) { }

        public StateDictionary(Action<StateDictionary<TKey, TValue>> initializer) : base()
        {
            _initializer = initializer;
            _idProvider = new CollectionIdProvider(_elementIds.ContainsValue);
        }

        protected override IEnumerable<StateDictionaryOperation<TKey, TValue>> GetInitializationOperations()
        {
            foreach (var kvp in _dictionary)
            {
                yield return new StateDictionaryOperation<TKey, TValue>()
                {
                    source = this,
                    opType = OpType.Add,
                    key = kvp.Key,
                    value = kvp.Value,
                    elementId = _elementIds[kvp.Key]
                };
            }
        }

        protected override void SendStateOperation(IDictionaryObserver<TKey, TValue> observer, StateDictionaryOperation<TKey, TValue> operation)
        {
            if (operation.opType == OpType.Add)
            {
                observer.OnAdd(operation.elementId, KeyValuePair.Create(operation.key, operation.value));
            }
            else if (operation.opType == OpType.Remove)
            {
                observer.OnRemove(operation.elementId, KeyValuePair.Create(operation.key, operation.value));
            }
            else
            {
                throw new Exception($"Unhandled op type {operation.opType}");
            }
        }

        protected override void InitializeInternal()
        {
            _initializer?.Invoke(this);
        }

        protected override IStateNode GetChildInternal(string childName)
            => _dictionary.First(x => x.Key.ToString() == childName).Value;

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = _dictionary.FirstOrDefault(x => x.Key.ToString() == childName).Value;
            return child != null;
        }

        private TValue AddInternal(TKey key)
        {
            var elementId = _idProvider.GetUnusedId();

            TValue value = new TValue();

            if (value is IKeyedStateNode<TKey> keyedNode)
                keyedNode.AssignKey(key);

            value.Initialize(this, key.ToString());
            value.PostInitialize();

            _dictionary.Add(key, value);
            _elementIds.Add(key, elementId);

            EnqueuePendingStateOperation(new() { source = this, opType = OpType.Add, key = key, value = value, elementId = elementId });

            return value;
        }

        private bool RemoveInternal(TKey key)
        {
            if (!_dictionary.TryGetValue(key, out var value))
                return false;

            var elementId = _elementIds[key];

            _dictionary.Remove(key);
            _elementIds.Remove(key);

            value.Dispose();

            EnqueuePendingStateOperation(new() { source = this, opType = OpType.Remove, key = key, value = value, elementId = elementId });

            return true;
        }

        private void ClearInternal()
        {
            foreach (var key in _dictionary.Keys.ToArray())
                Remove(key);

            _idProvider.Reset();
        }

        public TValue Add(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return AddInternal(key);
        }

        public bool Remove(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return RemoveInternal(key);
        }

        public bool ContainsKey(TKey key)
            => _dictionary.ContainsKey(key);

        public bool ContainsValue(TValue value)
            => _dictionary.ContainsValue(value);

        public bool TryGetValue(TKey key, out TValue value)
            => _dictionary.TryGetValue(key, out value);

        public TValue GetOrAdd(TKey key)
        {
            if (_dictionary.TryGetValue(key, out var value))
                return value;

            return Add(key);
        }

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            ClearInternal();
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");

            Clear();

            _initializer?.Invoke(this);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateDictionary<TKey, TValue>)copyTo;

            var toRemove = target.keys.Except(keys).ToArray();

            foreach (var keyToRemove in toRemove)
                target.Remove(keyToRemove);

            foreach (var kvpToCopy in _dictionary)
                kvpToCopy.Value.CopyTo(target.GetOrAdd(kvpToCopy.Key));
        }

        public override void FromJSON(JSONNode json)
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

            JSONObject dict = (JSONObject)json;
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            Clear();

            foreach (var value in dict)
                Add(serializer.fromJSON(value.Key)).FromJSON(value.Value);
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject dict = new JSONObject();
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            foreach (var kvp in _dictionary.Where(x => filter(x.Value)))
                dict.Add(serializer.toJSON(kvp.Key), kvp.Value.ToJSON(filter));

            return dict;
        }

        protected override void DisposeInternal()
        {
            foreach (var child in children)
                child.Dispose();

            _deriveStream?.Dispose();
        }

        public void Derive(Func<IDerivedDictionaryAccess<TKey, TValue>, IDisposable> derive)
        {
            _deriveStream = derive(new DerivedDictionaryAccess() { add = AddInternal, remove = RemoveInternal, clear = ClearInternal });
        }

        IStateNode IStateDictionary.Add(object key)
            => Add((TKey)key);

        bool IStateDictionary.Remove(object key)
            => Remove((TKey)key);

        bool IStateDictionary.TryGetValue(object key, out IStateNode value)
        {
            if (TryGetValue((TKey)key, out var stateValue))
            {
                value = stateValue;
                return true;
            }

            value = default;
            return false;
        }

        IStateNode IStateDictionary.GetOrAdd(object key)
            => GetOrAdd((TKey)key);

        public IDisposable Subscribe(ICollectionObserver<KeyValuePair<TKey, TValue>> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: observer.OnAdd,
                onRemove: observer.OnRemove,
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

        public IDisposable Subscribe(ICollectionObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnAdd(id, kvp),
                onRemove: (id, kvp) => observer.OnRemove(id, kvp),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (id, kvp) => observer.OnNext(new StateDictionaryOperation<TKey, TValue>() { source = this, opType = OpType.Add, key = kvp.Key, value = kvp.Value, elementId = id }),
                onRemove: (id, kvp) => observer.OnNext(new StateDictionaryOperation<TKey, TValue>() { source = this, opType = OpType.Remove, key = kvp.Key, value = kvp.Value, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}