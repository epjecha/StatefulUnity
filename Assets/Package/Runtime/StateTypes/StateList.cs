using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateList : IStateNode, IListObservable, IEnumerable
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

    public class StateList<T> : ObservableListBase<T>,
        IStateList,
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
        public bool derived => _deriveSubscription != null;

        public int Count => GetCountInternal();
        public T this[int index] => ElementAtInternal(index);

        IStateNode IStateList.this[int index] => this[index];
        int IStateNode.childCount => GetCountInternal();
        IEnumerable<IStateNode> IStateNode.children => ElementsInternal().Select(x => (IStateNode)x.value);

        private IDisposable _deriveSubscription;

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => ElementsInternal().Select(x => x.value).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => ElementsInternal().Select(x => x.value).GetEnumerator();

        private Action<StateList<T>> _initializeState;

        public StateList() : this(default) { }

        public StateList(Action<StateList<T>> initializeState) : base(null)
        {
            _initializeState = initializeState;
        }

        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            _initializeState?.Invoke(this);
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
            _initializeState?.Invoke(this);
            initialized = true;
        }

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

        private T Insert_NoCheck(int index)
        {
            var element = new T();
            element.Initialize(this, index.ToString());

            for (int i = index + 1; i < Count; i++)
                this[i].Rename(i.ToString());

            InsertInternal(index, element);

            return element;
        }

        private void RemoveAt_NoCheck(int index)
        {
            var element = this[index];
            element.Dispose();

            for (int i = index; i < Count; i++)
                this[i].Rename(i.ToString());

            RemoveAtInternal(index);
        }

        public T Add()
            => Insert(Count);

        public T Insert(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            return Insert_NoCheck(index);
        }

        public bool Remove(T element)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            var index = IndexOfInternal(element);

            if (index == -1)
                return false;

            RemoveAt_NoCheck(index);
            return true;
        }

        public void RemoveAt(int index)
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            RemoveAt_NoCheck(index);
        }

        public bool Contains(T element)
            => ContainsInternal(element);

        public int IndexOf(T element)
            => IndexOfInternal(element);

        public void Clear()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            ClearInternal();
        }

        public void CopyTo(IStateNode copyTo)
        {
            var target = (StateList<T>)copyTo;

            while (target.Count < Count)
                target.Add();

            while (target.Count > Count)
                target.RemoveAt(target.Count - 1);

            for (int i = 0; i < Count; i++)
                this[i].CopyTo(target[i]);
        }

        public void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            ClearInternal();

            _initializeState?.Invoke(this);

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

            while (Count > array.Count)
                RemoveAt(Count - 1);

            while (Count < array.Count)
                Add();

            for (int i = 0; i < Count; i++)
                this[i].FromJSON(array[i]);
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONArray array = new JSONArray();

            for (int i = 0; i < Count; i++)
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

            _deriveSubscription?.Dispose();
        }

        public void Derive(IDisposable subscription)
        {
            _deriveSubscription = subscription;
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
            if (int.TryParse(name, out var index) && index < Count)
            {
                child = ElementAtInternal(index);
                return true;
            }

            child = default;
            return false;
        }

        IStateNode IStateList.Add()
            => Add();

        IStateNode IStateList.Insert(int index)
            => Insert(index);

        bool IStateList.Remove(IStateNode node)
            => Remove((T)node);

        int IStateList.IndexOf(IStateNode node)
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