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
        Remove
    }

    public interface IStateOperation : IOperation
    {
        new IStateNode source { get; }
        OpType opType { get; }
        object param { get; }
        public uint elementId { get; }
        IStateNode child { get; }

        ObserveThing.IObservable<IOperation> IOperation.source => source;
    }

    public interface IStateNode : ObserveThing.IObservable<IStateOperation>, IDisposable
    {
        string nodeName { get; }
        string nodePath { get; }
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

    public abstract class StateNode<TObserver, TOperation> : ObservableBase<TObserver, TOperation>, ObserveThing.IObservable<IStateOperation>, IStateNode
        where TObserver : IObserverBase
        where TOperation : IStateOperation
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public IStateNode root { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode parent { get; private set; }
        public abstract IEnumerable<IStateNode> children { get; }
        public abstract int childCount { get; }
        public abstract bool derived { get; }
        public bool initialized { get; private set; }

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

        protected void EnqueuePendingStateOperation(TOperation operation)
        {
            logger.Generic(LogLevel.Trace, $"[ENQUEUING] {operation}");
            EnqueuePendingOperation(operation);
        }

        protected override void SendOperation(TObserver observer, TOperation operation)
        {
            logger.Generic(LogLevel.Trace, $"[SENDING] {operation}");
            SendStateOperation(observer, operation);
        }

        protected virtual void InitializeInternal() { }
        protected virtual void PostInitializeInternal() { }
        protected abstract void SendStateOperation(TObserver observer, TOperation opreation);
        protected override void DisposeInternal()
        {
            logger.Generic(LogLevel.Trace, $"[DISPOSE] source={nodePath}");
        }

        protected abstract bool TryGetChildInternal(string childName, out IStateNode child);
        protected abstract IStateNode GetChildInternal(string childName);

        public abstract void Reset();
        public abstract void CopyTo(IStateNode copyTo);

        public abstract JSONNode ToJSON(Func<IStateNode, bool> filter);
        public abstract void FromJSON(JSONNode json);

        void IStateNode.Rename(string newName)
        {
            nodeName = newName;
            nodePath = parent == null ? newName : $"{parent.nodePath}/{newName}";
        }

        public abstract IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null);
    }
}