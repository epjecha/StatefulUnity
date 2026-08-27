using System;
using System.Collections.Generic;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public abstract class StateNode : IStateNode
    {
        public ObservationContext context { get; private set; }
        public Attribute[] attributes { get; private set; }
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public IStateNode root { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode parent { get; private set; }
        public abstract IEnumerable<IStateNode> children { get; }
        public abstract int childCount { get; }
        public abstract bool isView { get; }
        public bool initialized { get; private set; }
        public bool disposed { get; private set; }

        public StateNode() { }

        public void Initialize(ObservationContext context, ILogger logger, string name = "root", Attribute[] attributes = null)
        {
            this.context = context;
            root = this;
            this.logger = logger;
            nodeName = name;
            nodePath = name;
            this.attributes = attributes ?? new Attribute[0];
            InitializeInternal();
            initialized = true;
            PostInitialize();
        }

        public void Initialize(IStateNode parent, string name, Attribute[] attributes)
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
            this.attributes = attributes ?? new Attribute[0];
            InitializeInternal();
            initialized = true;
        }

        public void PostInitialize()
        {
            PostInitializeInternal();

            foreach (var child in children)
                child.PostInitialize();
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

        public bool TryGetChild(string path, out IStateNode child)
        {
            if (string.IsNullOrEmpty(path))
            {
                child = this;
                return true;
            }

            if (!path.Contains('/'))
                return TryGetChildInternal(path, out child);

            string[] pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            IStateNode currDownstream = this;

            for (int i = 0; i < pathSegments.Length; i++)
            {
                if (i == 0 && pathSegments[i] == nodeName)
                    continue;

                if (!currDownstream.TryGetChild(pathSegments[i], out currDownstream))
                {
                    child = default;
                    return false;
                }
            }

            child = currDownstream;
            return true;
        }

        protected virtual void InitializeInternal() { }
        protected virtual void PostInitializeInternal() { }
        protected virtual void DisposeInternal() { }

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
            foreach (var child in children)
                child.Rename(child.nodeName);
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            logger.Generic(LogLevel.Trace, $"[DISPOSE] source={nodePath}");

            DisposeInternal();

            foreach (var child in children)
                child.Dispose();
        }

        public abstract IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null);
        public abstract IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null);
    }
}