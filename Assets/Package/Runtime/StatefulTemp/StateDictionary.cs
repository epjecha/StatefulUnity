using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;

using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IStateDictionary : IEnumerable, IStateNode
    {
        Type keyType { get; }
        Type valueType { get; }
        int count { get; }
        IStateNode this[object key] { get; }
        IEnumerable keys { get; }
        IEnumerable<IStateNode> values { get; }
        IStateNode Add(object key);
        bool Remove(object key);
        bool TryGetValue(object key, out IStateNode value);
        IStateNode GetOrAdd(object key);
        void Clear();
    }

    public interface IStateDictionary<TKey, TValue> : IDictionaryObservable<TKey, TValue>, IStateDictionary, IEnumerable<KeyValuePair<TKey, TValue>> where TValue : IStateNode, new()
    {
        TValue this[TKey key] { get; }
        new IEnumerable<TKey> keys { get; }
        new IEnumerable<TValue> values { get; }
        TValue Add(TKey key);
        bool Remove(TKey key);
        bool TryGetValue(TKey key, out TValue value);
        TValue GetOrAdd(TKey key);

        Type IStateDictionary.keyType => typeof(TKey);
        Type IStateDictionary.valueType => typeof(TValue);
        IStateNode IStateDictionary.this[object key] => this[(TKey)key];
        IEnumerable IStateDictionary.keys => keys;
        IEnumerable<IStateNode> IStateDictionary.values => values.Cast<IStateNode>();

        IStateNode IStateDictionary.Add(object key)
            => Add((TKey)key);

        bool IStateDictionary.Remove(object key)
            => Remove((TKey)key);

        bool IStateDictionary.TryGetValue(object key, out IStateNode value)
        {
            if (TryGetValue((TKey)key, out TValue v))
            {
                value = v;
                return true;
            }

            value = default;
            return false;
        }

        IStateNode IStateDictionary.GetOrAdd(object key)
            => GetOrAdd((TKey)key);
    }

    public class StateDictionary<TKey, TValue> : StateNode, IStateDictionary<TKey, TValue> where TValue : IStateNode, new()
    {
        public int count => _dictionary.count;
        public TValue this[TKey index] => _dictionary[index];
        public IEnumerable<TKey> keys => _dictionary.keys;
        public IEnumerable<TValue> values => _dictionary.values;
        public override int childCount => _dictionary.count;
        public override IEnumerable<IStateNode> children => _dictionary.Select(x => (IStateNode)x.Value);
        public override bool derived => _deriveStream != null;
        private IDisposable _deriveStream;

        private Func<KeyValuePair<TKey, TValue>[]> _getInitialValue;

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => ((IEnumerable<KeyValuePair<TKey, TValue>>)_dictionary).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_dictionary).GetEnumerator();

        private DictionaryObservable<TKey, TValue> _dictionary;

        public StateDictionary() : base() { }

        public StateDictionary(Func<KeyValuePair<TKey, TValue>[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
        }

        public StateDictionary(SynchronizationContext context, string name = "root", Func<KeyValuePair<TKey, TValue>[]> getInitialValue = default) : base(context, name)
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            _dictionary = _getInitialValue == null ?
                new DictionaryObservable<TKey, TValue>(parent.context) : new DictionaryObservable<TKey, TValue>(_getInitialValue(), parent.context);
        }

        protected override IStateNode GetChildInternal(string childName)
            => _dictionary.First(x => x.Key.ToString() == childName).Value;

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = _dictionary.FirstOrDefault(x => x.Key.ToString() == childName).Value;
            return child != null;
        }

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((IStateDictionary<TKey, TValue>)copyTo);

        public TValue Add(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            TValue value = new TValue();
            value.Initialize(this, key.ToString());
            value.PostInitialize();
            _dictionary.Add(key, value);
            return value;
        }

        public bool Remove(TKey key)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            if (!_dictionary.TryGetValue(key, out var value))
                return false;

            _dictionary.Remove(key);
            value.Dispose();

            return true;
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

            _dictionary.Clear();
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            _dictionary.Clear();

            if (_getInitialValue != null)
            {
                foreach (var kvp in _getInitialValue())
                    _dictionary.Add(kvp.Key, kvp.Value);
            }
        }

        public void CopyTo(IStateDictionary<TKey, TValue> copyTo)
        {
            var toRemove = copyTo.keys.Except(keys).ToArray();

            foreach (var keyToRemove in toRemove)
                copyTo.Remove(keyToRemove);

            foreach (var kvpToCopy in _dictionary)
                kvpToCopy.Value.CopyTo(copyTo.GetOrAdd(kvpToCopy.Key));
        }

        public IDisposable Subscribe(IDictionaryObserver<TKey, TValue> observer)
            => _dictionary.Subscribe(observer);

        public IDisposable Subscribe(ICollectionObserver<KeyValuePair<TKey, TValue>> observer)
            => _dictionary.Subscribe(observer);

        public override IDisposable Subscribe(IObserver observer)
            => _dictionary.Subscribe(observer);

        public override IDisposable Subscribe(IStateOpObserver observer)
            => _dictionary.Subscribe(new DictionaryObserver<TKey, TValue>(
                onAdd: (_, kvp) => observer.OnOperation(new StateOpArgs() { opType = OpType.Add, param = kvp.Key, child = kvp.Value, source = this }),
                onRemove: (_, kvp) => observer.OnOperation(new StateOpArgs() { opType = OpType.Remove, param = kvp.Key, child = kvp.Value, source = this }),
                onError: observer.OnError,
                onDispose: () =>
                {
                    if (disposed)
                        observer.OnOperation(new StateOpArgs() { opType = OpType.Dispose, source = this });

                    observer.OnDispose();
                }
            ));

        public override void FromJSON(string json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            JSONObject dict = (JSONObject)JSONNode.Parse(json);
            SerializationPair<TKey> serializer = JSONSerialization.GetSerializer<TKey>();

            Clear();

            foreach (var value in dict)
                Add(serializer.fromJSON(value.Key)).FromJSON(value.Value);
        }

        public override string ToJSON(Func<IStateNode, bool> filter)
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

            _dictionary.Dispose();
            _deriveStream?.Dispose();
        }

        public void Derive(IDictionaryObservable<TKey, TValue> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: kvp =>
                {
                    if (kvp.Value.parent == null)
                        kvp.Value.Initialize(this, kvp.Key.ToString());

                    _dictionary.Add(kvp.Key, kvp.Value);
                },
                onRemove: kvp =>
                {
                    _dictionary.Remove(kvp.Key);

                    if (kvp.Value.parent == this)
                        kvp.Value.Dispose();
                },
                immediate: true
            );
        }
    }
}