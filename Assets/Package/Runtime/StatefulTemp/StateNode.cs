using System;
using System.Collections.Generic;

using ObserveThing;

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

    public struct StateOpArgs
    {
        public IStateNode source;
        public OpType opType;
        public object param;
        public IStateNode child;

        public override string ToString()
        {
            return $"StateOpArgs[source={source?.nodePath ?? "null"}, opType={opType}, param={param?.ToString() ?? "null"}, child={child?.nodePath ?? "null"}]";
        }
    }

    public interface IStateOpObserver : IObserverBase
    {
        void OnOperation(StateOpArgs args);
    }

    public class StateOpObserver : IStateOpObserver
    {
        public bool immediate { get; private set; }
        private Action<StateOpArgs> _onOperation;
        private Action<Exception> _onError;
        private Action _onDispose;

        public StateOpObserver(Action<StateOpArgs> onOperation = default, Action<Exception> onError = default, Action onDispose = default, bool immediate = false)
        {
            _onOperation = onOperation;
            _onError = onError;
            _onDispose = onDispose;
            this.immediate = immediate;
        }

        public void OnOperation(StateOpArgs opArgs)
        {
            try
            {
                _onOperation?.Invoke(opArgs);
            }
            catch (Exception exc)
            {
                OnError(exc);
            }
        }

        public void OnError(Exception error) => (_onError ?? Observers.DefaultExceptionHandler)?.Invoke(error);

        public void OnDispose()
            => _onDispose?.Invoke();
    }

    public interface IStateOpObservable : IObservable
    {
        IDisposable Subscribe(IStateOpObserver observer);
    }

    public interface IStateNode : IStateOpObservable, IDisposable
    {
        string nodeName { get; }
        string nodePath { get; }
        SynchronizationContext context { get; }
        IStateNode root { get; }
        IStateNode parent { get; }
        IEnumerable<IStateNode> children { get; }
        int childCount { get; }
        bool initialized { get; }
        bool disposed { get; }
        bool derived { get; }
        void Initialize(IStateNode parent, string name);
        void PostInitialize();
        void Reset();
        void CopyTo(IStateNode copyTo);
        string ToJSON(Func<IStateNode, bool> filter);
        void FromJSON(string json);
        void Rename(string name);
        IStateNode GetChild(string name);
        bool TryFindChild(string name, out IStateNode child);
    }

    public abstract class StateNode : IStateNode
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public SynchronizationContext context { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public abstract IEnumerable<IStateNode> children { get; }
        public abstract int childCount { get; }
        public abstract bool derived { get; }
        public bool initialized { get; private set; }
        public bool disposed { get; private set; }

        public StateNode() { }

        public StateNode(SynchronizationContext context, string name = "root")
        {
            this.context = context;
            root = this;
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
        protected virtual void DisposeInternal() { }

        protected abstract bool TryGetChildInternal(string childName, out IStateNode child);
        protected abstract IStateNode GetChildInternal(string childName);
        protected abstract void CopyToInternal(IStateNode copyTo);

        public abstract void Reset();

        public abstract IDisposable Subscribe(IStateOpObserver observer);
        public abstract IDisposable Subscribe(IObserver observer);

        public abstract string ToJSON(Func<IStateNode, bool> filter);
        public abstract void FromJSON(string json);

        void IStateNode.CopyTo(IStateNode copyTo)
            => CopyToInternal(copyTo);

        void IStateNode.Rename(string newName)
        {
            nodeName = newName;
            nodePath = parent == null ? newName : $"{parent.nodePath}/{newName}";
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;

            DisposeInternal();
        }
    }
}