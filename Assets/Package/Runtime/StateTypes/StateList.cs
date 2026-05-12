using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateList : IEnumerable, IStateNode
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
        void CopyTo(IStateList copyTo);
    }

    public interface IStateList<T> : IListObservable<T>, IStateList, IEnumerable<T> where T : IStateNode, new()
    {
        new T this[int index] { get; }
        new T Add();
        new T Insert(int index);
        bool Remove(T element);
        new void RemoveAt(int index);
        int IndexOf(T value);
        void CopyTo(IStateList<T> copyTo);

        Type IStateList.itemType => typeof(T);
        IStateNode IStateList.this[int index] => this[index];

        IStateNode IStateList.Add()
            => Add();

        IStateNode IStateList.Insert(int index)
            => Insert(index);

        bool IStateList.Remove(IStateNode element)
            => Remove((T)element);

        void IStateList.RemoveAt(int index)
            => RemoveAt(index);

        int IStateList.IndexOf(IStateNode node)
            => IndexOf((T)node);

        void IStateList.CopyTo(IStateList copyTo)
            => CopyTo(copyTo);
    }

    public class StateList<T> : StateNode<T>, IStateList<T> where T : IStateNode, new()
    {
        public int Count => _list.Count;
        public T this[int index] => _list[index];
        public override int childCount => _list.Count;
        public override IEnumerable<IStateNode> children => _list.Select(x => (IStateNode)x);
        public override bool derived => _deriveStream != null;
        private IDisposable _deriveStream;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_list).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_list).GetEnumerator();

        private ObservableList<T> _list;
        private Func<T[]> _getInitialValue;

        public StateList() : this(default) { }

        public StateList(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            _list = _getInitialValue == null ?
                new ObservableList<T>(context) : new ObservableList<T>(context, _getInitialValue());

            _list.Subscribe(HandleInternalOperation, immediate: true);
        }

        protected override IReadOnlyList<StateOpArgs<T>> GetInitializationOperations()
        {
            var ops = new StateOpArgs<T>[_list.Count];

            for (int i = 0; i < _list.Count; i++)
            {
                var element = _list.ElementAndIdAt(i);
                ops[i] = new StateOpArgs<T>(this, OpType.Add, element.value, element.id, i, element.value);
            }

            return ops;
        }

        private void HandleInternalOperation(IReadOnlyList<ListOpArgs<T>> ops)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
            {
                EnqueuePendingOperation(new StateOpArgs<T>(
                    source: this,
                    opType: op.isRemove ? OpType.Remove : OpType.Add,
                    param: op.element,
                    collectionElementId: op.id,
                    index: op.index,
                    child: op.element
                ));
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

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((IStateList<T>)copyTo);

        public T Add()
            => Insert(_list.Count);

        public T Insert(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            T element = new T();
            element.Initialize(this, index.ToString());
            element.PostInitialize();

            _list.Insert(index, element);

            for (int i = index + 1; i < _list.Count; i++)
                _list[i].Rename(i.ToString());

            LogOperation(OpType.Add, index, element);

            return element;
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            var index = _list.IndexOf(element);

            if (index == -1)
                return false;

            RemoveAt(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            var element = _list[index];
            _list.RemoveAt(index);

            for (int i = index; i < _list.Count; i++)
                _list[i].Rename(i.ToString());

            LogOperation(OpType.Remove, index, element);

            element.Dispose();
        }

        public bool Contains(T element)
            => _list.Contains(element);

        public int IndexOf(T element)
            => _list.IndexOf(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            _list.Clear();
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            _list.Clear();

            if (_getInitialValue != null)
            {
                foreach (var element in _getInitialValue())
                    _list.Add(element);
            }

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public void CopyTo(IStateList<T> copyTo)
        {
            while (copyTo.Count < Count)
                copyTo.Add();

            while (copyTo.Count > Count)
                copyTo.RemoveAt(copyTo.Count - 1);

            for (int i = 0; i < Count; i++)
                _list[i].CopyTo(copyTo[i]);
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
                onAdd: (index, element) =>
                {
                    _list.Insert(index, element);

                    if (element.parent != null)
                        return;

                    element.Initialize(this, index.ToString());
                    element.PostInitialize();

                    for (int i = index + 1; i < _list.Count; i++)
                        _list[i].Rename(i.ToString());
                },
                onRemove: (index, item) =>
                {
                    var element = _list[index];
                    _list.RemoveAt(index);

                    if (element.parent != this)
                        return;

                    for (int i = index; i < _list.Count; i++)
                        _list[i].Rename(i.ToString());

                    LogOperation(OpType.Remove, index, element);

                    element.Dispose();
                },
                immediate: true
            );
        }

        public IDisposable Subscribe(IListObserver<T> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        int index = 0;
                        foreach (var pair in _list.ElementsWithIds)
                        {
                            observer.OnAdd(pair.id, index, pair.element);
                            index++;
                        }

                        return;
                    }

                    foreach (var op in ops)
                    {
                        if (op.opType == OpType.Add)
                        {
                            observer.OnAdd(op.collectionElementId, op.index, op.param);
                        }
                        else if (op.opType == OpType.Remove)
                        {
                            observer.OnRemove(op.collectionElementId, op.index, op.param);
                        }
                    }
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));

        public IDisposable Subscribe(ICollectionObserver<T> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        int index = 0;
                        foreach (var pair in _list.ElementsWithIds)
                        {
                            observer.OnAdd(pair.id, pair.element);
                            index++;
                        }

                        return;
                    }

                    foreach (var op in ops)
                    {
                        if (op.opType == OpType.Add)
                        {
                            observer.OnAdd(op.collectionElementId, op.param);
                        }
                        else if (op.opType == OpType.Remove)
                        {
                            observer.OnRemove(op.collectionElementId, op.param);
                        }
                    }
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));
    }
}