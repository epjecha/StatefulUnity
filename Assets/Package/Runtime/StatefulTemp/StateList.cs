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
        int count { get; }
        IStateNode Add();
        IStateNode Insert(int index);
        bool Remove(IStateNode node);
        void RemoveAt(int index);
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

        void IStateList.CopyTo(IStateList copyTo)
            => CopyTo(copyTo);
    }

    public class StateList<T> : StateNode, IStateList<T> where T : IStateNode, new()
    {
        public int count => _list.count;
        public T this[int index] => _list[index];
        public override int childCount => _list.count;
        public override IEnumerable<IStateNode> children => _list.Select(x => (IStateNode)x);
        public override bool derived => _deriveStream != null;
        private IDisposable _deriveStream;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_list).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_list).GetEnumerator();

        private ListObservable<T> _list;
        private Func<T[]> _getInitialValue;

        public StateList() : base() { }

        public StateList(Func<T[]> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
        }

        public StateList(SynchronizationContext context, string name = "root", Func<T[]> getInitialValue = default) : base(context, name)
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            _list = _getInitialValue == null ?
                new ListObservable<T>(parent.context) : new ListObservable<T>(_getInitialValue.Invoke(), parent.context);
        }

        protected override IStateNode GetChildInternal(string childName)
            => _list[int.Parse(childName)];

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            var index = int.Parse(childName);
            if (index >= _list.count)
            {
                child = default;
                return false;
            }

            child = _list[index];
            return true;
        }

        public T Add()
            => Insert(_list.count);

        public T Insert(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            T element = new T();
            element.Initialize(this, index.ToString());
            element.PostInitialize();
            _list.Insert(index, element);
            return element;
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            if (!_list.Remove(element))
                return false;

            element.Dispose();
            return true;
        }

        public void RemoveAt(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            var element = _list[index];
            _list.RemoveAt(index);
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
                _list.AddRange(_getInitialValue());
        }

        public override void CopyTo(IStateNode copyTo)
            => CopyTo((IStateList<T>)copyTo);

        public void CopyTo(IStateList<T> copyTo)
        {
            while (copyTo.count < count)
                copyTo.Add();

            while (copyTo.count > count)
                copyTo.RemoveAt(copyTo.count - 1);

            for (int i = 0; i < count; i++)
                _list[i].CopyTo(copyTo[i]);
        }

        public IDisposable Subscribe(IListObserver<T> observer)
            => _list.Subscribe(observer);

        public IDisposable Subscribe(ICollectionObserver<T> observer)
            => _list.Subscribe(observer);

        public override IDisposable Subscribe(IObserver observer)
            => _list.Subscribe(observer);

        public override IDisposable Subscribe(IStateOpObserver observer)
            => _list.Subscribe(new ListObserver<T>(
                onAdd: (_, index, item) => observer.OnOperation(new StateOpArgs() { opType = OpType.Add, param = index, child = item, source = this }),
                onRemove: (_, index, item) => observer.OnOperation(new StateOpArgs() { opType = OpType.Remove, param = index, child = item, source = this }),
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

            JSONArray array = (JSONArray)JSONNode.Parse(json);

            while (count > array.Count)
                RemoveAt(count - 1);

            while (count < array.Count)
                Add();

            for (int i = 0; i < count; i++)
                _list[i].FromJSON(array[i]);
        }

        public override string ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();

            for (int i = 0; i < _list.count; i++)
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

            _list.Dispose();
            _deriveStream?.Dispose();
        }

        public void Derive(IListObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                onAdd: (index, item) =>
                {
                    if (item.parent == null)
                        item.Initialize(this, index.ToString());

                    _list.Add(item);
                },
                onRemove: (index, item) =>
                {
                    _list.RemoveAt(index);

                    if (item.parent == this)
                        item.Dispose();
                },
                immediate: true
            );
        }
    }
}