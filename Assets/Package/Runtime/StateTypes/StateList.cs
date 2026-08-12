using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateList : IEnumerable, IStateNode, IListObservable
    {
        Type itemType { get; }
        IStateNode this[int index] { get; }
        int Count { get; }
        IStateNode Add();
        IStateNode Insert(int index);
        bool Remove(IStateNode node);
        void RemoveAt(int index);
        int IndexOf(IStateNode node);
        void Clear();
    }

    public struct StateListOperation<T> : IStateOperation where T : IStateNode
    {
        public IStateNode source { get; set; }
        public OpType opType { get; set; }
        public T element { get; set; }
        public uint elementId { get; set; }
        public int index { get; set; }

        object IStateOperation.param => index;
        public IStateNode child => element;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={index}";
        }
    }

    public class StateList<T> : StateNode<IListObserver<T>, StateListOperation<T>>,
        IStateList,
        IListObservable<T>,
        IEnumerable<T>
        where T : IStateNode, new()
    {
        public int Count => _list.Count;
        public T this[int index] => _list[index];
        public override int childCount => _list.Count;
        public override IEnumerable<IStateNode> children => _list.Select(x => (IStateNode)x);
        public override bool derived => _deriveStream != null;

        public Type itemType => throw new NotImplementedException();

        IStateNode IStateList.this[int index] => this[index];

        private IDisposable _deriveStream;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_list).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_list).GetEnumerator();

        private List<T> _list = new List<T>();
        private List<uint> _ids = new List<uint>();
        private Func<T[]> _getInitialValue;
        private CollectionIdProvider _idProvider;

        public StateList() : this(default) { }

        public StateList(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
            _idProvider = new CollectionIdProvider(_ids.Contains);
        }

        protected override void InitializeInternal()
        {
            if (_getInitialValue == null)
                return;

            var initValues = _getInitialValue();
            foreach (var element in _getInitialValue())
            {
                var id = _idProvider.GetUnusedId();

                _list.Add(element);
                _ids.Add(id);
            }
        }

        protected override IEnumerable<StateListOperation<T>> GetInitializationOperations()
        {
            for (int i = 0; i < _list.Count; i++)
                yield return new StateListOperation<T>() { source = this, index = i, elementId = _ids[i], opType = OpType.Add };
        }

        protected override void SendStateOperation(IListObserver<T> observer, StateListOperation<T> operation)
        {
            if (operation.opType == OpType.Add)
            {
                observer.OnAdd(operation.elementId, operation.index, operation.element);
            }
            else if (operation.opType == OpType.Remove)
            {
                observer.OnRemove(operation.elementId, operation.index, operation.element);
            }
            else
            {
                throw new Exception($"Unhandled op type {operation.opType}");
            }
        }

        protected override IStateNode GetChildInternal(string childName)
            => _list[int.Parse(childName)];

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            var index = int.Parse(childName);
            if (index >= _list.Count)
            {
                child = default;
                return false;
            }

            child = _list[index];
            return true;
        }

        private void InsertInternal(int index, T element)
        {
            if (element.parent == null)
            {
                element.Initialize(this, index.ToString());
                element.PostInitialize();
            }

            var id = _idProvider.GetUnusedId();

            _list.Insert(index, element);
            _ids.Insert(index, id);

            for (int i = index + 1; i < _list.Count; i++)
                _list[i].Rename(i.ToString());

            EnqueuePendingStateOperation(new() { source = this, opType = OpType.Add, elementId = id, index = index, element = element });
        }

        private void RemoveAtInternal(int index)
        {
            var element = _list[index];
            var id = _ids[index];

            _list.RemoveAt(index);
            _ids.RemoveAt(index);

            for (int i = index; i < _list.Count; i++)
                _list[i].Rename(i.ToString());

            if (element.parent == this)
                element.Dispose();

            EnqueuePendingStateOperation(new() { source = this, opType = OpType.Remove, elementId = id, index = index, element = element });
        }

        public T Add()
            => Insert(_list.Count);

        public T Insert(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            T element = new T();
            InsertInternal(index, element);

            return element;
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            var index = _list.IndexOf(element);

            if (index == -1)
                return false;

            RemoveAtInternal(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            RemoveAtInternal(index);
        }

        public bool Contains(T element)
            => _list.Contains(element);

        public int IndexOf(T element)
            => _list.IndexOf(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            for (int i = _list.Count - 1; i >= 0; i--)
                RemoveAtInternal(i);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateList<T>)copyTo;

            while (target.Count < Count)
                target.Add();

            while (target.Count > Count)
                target.RemoveAt(target.Count - 1);

            for (int i = 0; i < Count; i++)
                _list[i].CopyTo(target[i]);
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            Clear();

            if (_getInitialValue != null)
            {
                foreach (var element in _getInitialValue())
                    _list.Add(element);
            }

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
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

            JSONArray array = (JSONArray)json;

            while (Count > array.Count)
                RemoveAt(Count - 1);

            while (Count < array.Count)
                Add();

            for (int i = 0; i < Count; i++)
                _list[i].FromJSON(array[i]);
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();

            for (int i = 0; i < _list.Count; i++)
            {
                var item = _list[i];
                if (filter(item))
                    array.Add(item.ToJSON(filter));
            }

            return array;
        }

        protected override void DisposeInternal()
        {
            foreach (var child in children)
                child.Dispose();

            _deriveStream?.Dispose();
        }

        public void Derive(IListObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: InsertInternal,
                onRemove: (index, _) => RemoveAtInternal(index),
                immediate: true
            );
        }

        IStateNode IStateList.Add()
            => Add();

        IStateNode IStateList.Insert(int index)
            => Insert(index);

        bool IStateList.Remove(IStateNode node)
            => Remove((T)node);

        int IStateList.IndexOf(IStateNode node)
            => IndexOf((T)node);

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnNext(new StateListOperation<T>() { source = this, elementId = id, index = index, element = element, opType = OpType.Add }),
                onRemove: (id, index, element) => observer.OnNext(new StateListOperation<T>() { source = this, elementId = id, index = index, element = element, opType = OpType.Remove }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IListObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnAdd(id, index, element),
                onRemove: (id, index, element) => observer.OnRemove(id, index, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnAdd(id, element),
                onRemove: (id, index, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(ICollectionObserver<T> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnAdd(id, element),
                onRemove: (id, index, element) => observer.OnRemove(id, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}