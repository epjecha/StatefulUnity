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

        public override bool isView => true;

        private IDisposable _subscription;
        private Mutator _mutator;

        public StateListView() : base()
        {
            _mutator = new Mutator()
            {
                add = AddChild,
                insert = InsertChild,
                remove = RemoveChild,
                removeAt = RemoveChildAt,
                clear = ClearInternal
            };
        }

        public void InitializeView(Func<IStateListViewMutator<T>, IDisposable> initialize)
        {
            _subscription = initialize(_mutator);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            logger.Warning($"{nodePath} is a view. \'CopyTo\' will be ignored.");
        }

        protected override void Reset()
        {
            logger.Warning($"{nodePath} is a view. \'Reset\' will be ignored for this object. Children will be reset.");
            foreach (var child in ElementsInternal())
                child.value.Reset();
        }

        public override void FromJSON(JSONNode json)
        {
            logger.Warning($"{nodePath} is a view. \'FromJSON\' will be ignored.");
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
        private Action<ReadOnlyStateList<T>> _initializeState;

        public StateList() : this(default) { }

        public StateList(Action<ReadOnlyStateList<T>> initializeState) : base()
        {
            _initializeState = initializeState;
        }

        public T Insert(int index)
            => InsertChild(index);

        public void RemoveAt(int index)
            => RemoveChildAt(index);

        public T Add()
            => AddChild();

        public bool Remove(T element)
            => RemoveChild(element);

        public void Clear()
            => ClearInternal();

        protected override void Reset()
        {
            Clear();

            _initializeState?.Invoke(this);

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
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

        IStateNode IStateList.Add()
            => Add();

        IStateNode IStateList.Insert(int index)
            => Insert(index);

        bool IStateList.Remove(IStateNode node)
            => Remove((T)node);
    }

    public abstract class ReadOnlyStateList<T> : ObservableListBase<T>,
        IReadOnlyStateList,
        IEnumerable<T>
        where T : IStateNode, new()
    {
        public Type itemType => typeof(T);
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }
        public abstract bool isView { get; }

        public int count => GetCountInternal();
        public T this[int index] => ElementAtInternal(index);

        IStateNode IReadOnlyStateList.this[int index] => this[index];
        int IStateNode.childCount => GetCountInternal();
        IEnumerable<IStateNode> IStateNode.children => ElementsInternal().Select(x => (IStateNode)x.value);

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ElementsInternal().Select(x => x.value).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ElementsInternal().Select(x => x.value).GetEnumerator();

        public ReadOnlyStateList() : base(default) { }

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

        protected virtual void InitializeInternal() { }

        void IStateNode.PostInitialize()
        {
            foreach (var child in this)
                child.PostInitialize();
        }

        protected override void SendOperation(IListObserver<T> observer, ListOp<T> operation)
        {
            logger.Trace($"Notifying {Utility.FormatOperationLog(operation.isRemove ? OpType.Remove : OpType.Add, this, operation.index, operation.elementId, operation.element)}");
            base.SendOperation(observer, operation);
        }

        protected T AddChild()
            => InsertChild(count);

        protected T InsertChild(int index)
        {
            var element = new T();
            element.Initialize(this, index.ToString());

            for (int i = index + 1; i < count; i++)
                this[i].Rename(i.ToString());

            InsertInternal(index, element);

            return element;
        }

        protected bool RemoveChild(T element)
        {
            var index = IndexOfInternal(element);

            if (index == -1)
                return false;

            RemoveChildAt(index);
            return true;
        }

        protected void RemoveChildAt(int index)
        {
            var element = this[index];
            element.Dispose();

            for (int i = index; i < count; i++)
                this[i].Rename(i.ToString());

            RemoveAtInternal(index);
        }

        public bool Contains(T element)
            => ContainsInternal(element);

        public int IndexOf(T element)
            => IndexOfInternal(element);

        bool IReadOnlyStateList.Contains(IStateNode element)
            => Contains((T)element);

        public abstract void CopyTo(IStateNode copyTo);

        protected abstract void Reset();

        void IStateNode.Reset()
            => Reset();

        public abstract void FromJSON(JSONNode json);

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
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
            foreach (var child in ElementsInternal())
                child.value.Dispose();
        }

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
            foreach (var child in ElementsInternal())
                child.value.Rename(child.value.nodeName);
        }

        public IStateNode GetChild(string name)
        {
            var index = int.Parse(name);
            return ElementAtInternal(index);
        }

        public bool TryGetChild(string name, out IStateNode child)
        {
            if (int.TryParse(name, out var index) && index < count)
            {
                child = ElementAtInternal(index);
                return true;
            }

            child = default;
            return false;
        }

        int IReadOnlyStateList.IndexOf(IStateNode node)
            => IndexOf((T)node);

        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ListObserver<T>(
                onAdd: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Add, param = index, child = element, elementId = id }),
                onRemove: (id, index, element) => observer.OnNext(new StateOperation() { source = this, opType = OpType.Remove, param = index, child = element, elementId = id }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}