using System;
using System.Collections.Generic;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public enum OpType
    {
        None,
        Set,
        Add,
        Remove,
        Dispose
    }

    public interface IStateOperation
    {
        IStateNode source { get; }
        OpType opType { get; }
        object param { get; }
        uint collectionElementId { get; }
        int index { get; }
        IStateNode child { get; }
        IStateOperation Clone();
    }

    public struct StateOpArgs<T>
    {
        public IStateNode source { get; }
        public OpType opType { get; }
        public T param { get; }
        public uint collectionElementId { get; }
        public int index { get; }
        public IStateNode child { get; }

        public StateOpArgs(IStateNode source, OpType opType, T param, uint collectionElementId = 0, int index = -1, IStateNode child = null)
        {
            this.source = source;
            this.opType = opType;
            this.param = param;
            this.collectionElementId = collectionElementId;
            this.index = index;
            this.child = child;
        }

        public override string ToString()
        {
            return $"StateOpArgs[source={source?.nodePath ?? "null"}, opType={opType}, param={param?.ToString() ?? "null"}, child={child?.nodePath ?? "null"}]";
        }
    }

    public interface IStateNode : ObserveThing.IObservable<IStateOperation>, IDisposable
    {
        string nodeName { get; }
        string nodePath { get; }
        ObservationContext context { get; }
        IStateNode root { get; }
        ILogger logger { get; }
        IStateNode parent { get; }
        IEnumerable<IStateNode> children { get; }
        int childCount { get; }
        bool initialized { get; }
        bool disposed { get; }
        bool derived { get; }
        void Initialize(ObservationContext context, ILogger logger, string name = "root");
        void Initialize(IStateNode parent, string name);
        void PostInitialize();
        void Reset();
        void CopyTo(IStateNode copyTo);
        JSONNode ToJSON(Func<IStateNode, bool> filter);
        void FromJSON(JSONNode json);
        void Rename(string name);
        IStateNode GetChild(string name);
        bool TryFindChild(string name, out IStateNode child);
    }

    public abstract class StateNode<T> : Observable<StateOpArgs<T>>, IStateNode
    {
        protected class StateOperation : IStateOperation
        {
            public IStateNode source => args.source;
            public OpType opType => args.opType;
            public object param => args.param;
            public uint collectionElementId => args.collectionElementId;
            public int index => args.index;
            public IStateNode child => args.child;

            public StateOpArgs<T> args;

            public IStateOperation Clone()
                => new StateOperation() { args = args };
        }

        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public IStateNode root { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode parent { get; private set; }
        public abstract IEnumerable<IStateNode> children { get; }
        public abstract int childCount { get; }
        public abstract bool derived { get; }
        public bool initialized { get; private set; }

        private Queue<StateOperation> _opPool = new Queue<StateOperation>();
        private List<StateOperation> _opList = new List<StateOperation>();

        public StateNode() : base(default) { }

        public void Initialize(ObservationContext context, ILogger logger, string name = "root")
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            InitializeInternal();
            initialized = true;
            PostInitialize();
        }

        public void Initialize(IStateNode parent, string name)
        {
            if (name == null)
                throw new System.ArgumentNullException(nameof(name));

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

        public void PostInitialize()
        {
            PostInitializeInternal();

            foreach (var child in children)
                child.PostInitialize();
        }

        public bool TryFindChild(string path, out IStateNode child)
        {
            if (!path.Contains('/'))
                return TryGetChildInternal(path, out child);

            string[] pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            IStateNode currDownstream = this;

            for (int i = 0; i < pathSegments.Length; i++)
            {
                if (i == 0 && pathSegments[i] == nodeName)
                    continue;

                if (!currDownstream.TryFindChild(pathSegments[i], out currDownstream))
                {
                    child = default;
                    return false;
                }
            }

            child = currDownstream;
            return true;
        }

        public IStateNode GetChild(string path)
        {
            if (string.IsNullOrEmpty(path))
                return this;

            if (!path.Contains('/'))
                return GetChildInternal(path);

            string[] pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            IStateNode currDownstream = this;

            for (int i = 0; i < pathSegments.Length; i++)
            {
                if (i == 0 && this == root && pathSegments[i] == nodeName)
                    continue;

                currDownstream = currDownstream.GetChild(pathSegments[i]);
            }

            return currDownstream;
        }

        protected virtual void InitializeInternal() { }
        protected virtual void PostInitializeInternal() { }
        protected override void DisposeInternal()
        {
            LogOperation(OpType.Dispose);
        }

        protected abstract bool TryGetChildInternal(string childName, out IStateNode child);
        protected abstract IStateNode GetChildInternal(string childName);
        protected abstract void CopyToInternal(IStateNode copyTo);

        public abstract void Reset();

        public abstract JSONNode ToJSON(Func<IStateNode, bool> filter);
        public abstract void FromJSON(JSONNode json);

        void IStateNode.CopyTo(IStateNode copyTo)
            => CopyToInternal(copyTo);

        void IStateNode.Rename(string newName)
        {
            nodeName = newName;
            nodePath = parent == null ? newName : $"{parent.nodePath}/{newName}";
        }

        protected void LogOperation(OpType opType, object param = default, IStateNode child = default, LogLevel logLevel = LogLevel.Trace)
        {
            logger.Generic(logLevel, $"{opType} {nodePath} param={param?.ToString() ?? "null"} child={child?.nodeName ?? "null"}");
        }

        public IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        observer.OnOperation(null);
                        return;
                    }

                    foreach (var op in ops)
                    {
                        if (!_opPool.TryDequeue(out var operation))
                            operation = new StateOperation();

                        operation.args = op;
                        _opList.Add(operation);
                    }

                    observer.OnOperation(_opList);

                    foreach (var operation in _opList)
                        _opPool.Enqueue(operation);

                    _opList.Clear();
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));
    }
}