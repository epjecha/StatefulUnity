using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IReadOnlyStateList : IStateNode, IListObservable, IEnumerable
    {
        Type itemType { get; }
        IStateNode this[int index] { get; }
        int count { get; }
        int IndexOf(IStateNode node);
        bool Contains(IStateNode node);
    }

    public interface IStateList : IReadOnlyStateList
    {
        IStateNode Add();
        IStateNode Insert(int index);
        bool Remove(IStateNode node);
        void RemoveAt(int index);
        void Clear();
    }

    public interface IStateListViewMutator<T>
    {
        T Add();
        T Insert(int index);
        bool Remove(T node);
        void RemoveAt(int index);
        void Clear();
    }

    public class StateListView<T> : ReadOnlyStateList<T> where T : IStateNode, new()
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateListViewMutator<T>
        {
            public Func<T> add;
            public Func<int, T> insert;
            public Func<T, bool> remove;
            public Action<int> removeAt;
            public Action clear;

            public T Add()
                => add();

            public T Insert(int index)
                => insert(index);

            public bool Remove(T node)
                => remove(node);

            public void RemoveAt(int index)
                => removeAt(index);

            public void Clear()
                => clear();
        }


        public StateListView() : base()
        {
            _mutator = new Mutator()
            {
                add = AddInternal,
                insert = InsertInternal,
                remove = RemoveInternal,
                removeAt = RemoveAtInternal,
                clear = ClearInternal
            };
        }

        public void InitializeView(Action<IStateListViewMutator<T>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateListViewMutator<T>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
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

            foreach (var child in this)
                child.Reset();
        }

        protected override void DisposeInternal()
        {
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateList<T> : ReadOnlyStateList<T>, IStateList where T : IStateNode, new()
    {
        public override bool isView => false;
        private Action<ReadOnlyStateList<T>> _initializer;

        public StateList() : this(default) { }
        public StateList(Action<ReadOnlyStateList<T>> initializer) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            base.InitializeInternal();
            _initializer?.Invoke(this);
        }

        public T Insert(int index)
            => InsertInternal(index);

        public void RemoveAt(int index)
            => RemoveAtInternal(index);

        public T Add()
            => AddInternal();

        public bool Remove(T element)
            => RemoveInternal(element);

        public void Clear()
            => ClearInternal();

        IStateNode IStateList.Add()
            => Add();

        IStateNode IStateList.Insert(int index)
            => Insert(index);

        bool IStateList.Remove(IStateNode node)
            => Remove((T)node);

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

            JSONArray array = (JSONArray)json;

            while (count > array.Count)
                RemoveAt(count - 1);

            while (count < array.Count)
                Add();

            for (int i = 0; i < count; i++)
                this[i].FromJSON(array[i]);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            var target = (StateList<T>)copyTo;

            while (target.count < count)
                target.Add();

            while (target.count > count)
                target.RemoveAt(target.count - 1);

            for (int i = 0; i < count; i++)
                this[i].CopyTo(target[i]);
        }
    }

    public abstract class ReadOnlyStateList<T> : StateNode,
        IReadOnlyStateList,
        IListObservable<T>,
        IEnumerable<T>
        where T : IStateNode, new()
    {
        public int count => _list.count;
        public T this[int index] => _list[index];

        public Type itemType => typeof(T);
        public override int childCount => _list.count;
        public override IEnumerable<IStateNode> children => _list.Cast<IStateNode>();

        IStateNode IReadOnlyStateList.this[int index] => this[index];
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ((IEnumerable<T>)_list).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable)_list).GetEnumerator();

        private ObservableList<T> _list;

        public ReadOnlyStateList() : base() { }

        protected override void InitializeInternal()
        {
            _list = new ObservableList<T>(context);
        }

        protected T AddInternal()
            => InsertInternal(count);

        protected T InsertInternal(int index)
        {
            var element = new T();
            element.Initialize(this, index.ToString(), attributes.Where(x => x is IInheritableStateAttribute inheritable && inheritable.inherit).ToArray());

            for (int i = index + 1; i < count; i++)
                this[i].Rename(i.ToString());

            logger.Trace(Utility.FormatOperationLog(OpType.Add, this, index, element));
            _list.Insert(index, element);

            return element;
        }

        protected bool RemoveInternal(T element)
        {
            var index = _list.IndexOf(element);

            if (index == -1)
                return false;

            RemoveAtInternal(index);
            return true;
        }

        protected void RemoveAtInternal(int index)
        {
            var element = this[index];
            element.Dispose();

            for (int i = index; i < count; i++)
                this[i].Rename(i.ToString());

            logger.Trace(Utility.FormatOperationLog(OpType.Remove, this, index, element));
            _list.RemoveAt(index);
        }

        protected void ClearInternal()
        {
            for (int i = count - 1; i >= 0; i--)
            {
                var element = this[i];
                logger.Trace(Utility.FormatOperationLog(OpType.Remove, this, i, element));
                RemoveAtInternal(i);
            }
        }

        public bool Contains(T element)
            => _list.Contains(element);

        public int IndexOf(T element)
            => _list.IndexOf(element);

        bool IReadOnlyStateList.Contains(IStateNode element)
            => Contains((T)element);

        int IReadOnlyStateList.IndexOf(IStateNode node)
            => IndexOf((T)node);

        protected override IStateNode GetChildInternal(string name)
        {
            var index = int.Parse(name);
            return _list[index];
        }

        protected override bool TryGetChildInternal(string name, out IStateNode child)
        {
            if (int.TryParse(name, out var index) && index < count)
            {
                child = _list[index];
                return true;
            }

            child = default;
            return false;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();

            for (int i = 0; i < count; i++)
            {
                var item = this[i];
                if (filter(item))
                    array.Add(item.ToJSON(filter));
            }

            return array;
        }

        protected override void DisposeInternal()
        {
            _list.Dispose();
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = index, child = element, elementId = id }),
                onRemove: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = index, child = element, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = index, child = element, elementId = id }),
                onRemove: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = index, child = element, elementId = id }),
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

        public IDisposable Subscribe(IListObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnAdd(id, index, element),
                onRemove: (id, index, element) => observer.OnRemove(id, index, element),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IListObserver<T> observer, bool immediate = false, uint? priority = null)
            => _list.Subscribe(observer, immediate, priority);
    }
}