using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public class StateObject : ObservableBase<IObserverBase, StateOperation>, IStateNode
    {
        public string nodeName { get; private set; }
        public string nodePath { get; private set; }
        public ILogger logger { get; private set; }
        public IStateNode root { get; private set; }
        public IStateNode parent { get; private set; }
        public bool initialized { get; private set; }

        public int childCount => _children.Count;
        public IEnumerable<IStateNode> children => _children.Values;
        public bool derived => false;


        private Dictionary<string, IStateNode> _children = new Dictionary<string, IStateNode>();

        public StateObject() : base(null) { }

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

        private void InitializeInternal()
        {
            var type = GetType();
            while (type != typeof(StateObject))
            {
                // certain platforms require that binding flags be set explicitly, or all inherited
                // properties will be returned with each type up the inheritance chain
                var properties = type
                    .GetProperties(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public)
                    .Where(x => x.SetMethod != null &&
                        typeof(IStateNode).IsAssignableFrom(x.PropertyType) &&
                        x.Name != nameof(IStateNode.parent) &&
                        x.Name != nameof(IStateNode.root)
                    );

                foreach (var property in properties)
                {
                    IStateNode child = (IStateNode)(property.GetValue(this) ?? Activator.CreateInstance(property.PropertyType));
                    property.SetValue(this, child);
                    _children.Add(property.Name, child);
                    child.Initialize(this, property.Name);
                }

                type = type.BaseType;
            }
        }

        protected override IEnumerable<StateOperation> GetInitializationOperations()
        {
            yield break;
        }

        protected override void SendOperation(IObserverBase observer, StateOperation operation)
        {
            throw new NotImplementedException();
        }

        protected virtual void PostInitializeInternal() { }

        void IStateNode.PostInitialize()
        {
            PostInitializeInternal();

            foreach (var child in children)
                child.PostInitialize();
        }

        public void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");

            foreach (var child in children.Where(x => !x.derived))
                child.Reset();
        }

        public void CopyTo(StateObject copyTo)
        {
            foreach (var child in children)
            {
                var destChild = copyTo.GetChild(child.nodeName);

                if (destChild.derived)
                    continue;

                child.CopyTo(destChild);
            }
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

            foreach (var child in children)
                child.FromJSON(json[child.nodeName]);
        }

        public JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject obj = new JSONObject();

            foreach (var child in children.Where(filter))
                obj.Add(child.nodeName, child.ToJSON(filter));

            return obj;
        }

        protected override void DisposeInternal()
        {
            foreach (var child in children)
                child.Dispose();
        }

        public void CopyTo(IStateNode copyTo)
        {
            foreach (var child in children)
                child.CopyTo(copyTo.GetChild(child.nodeName));
        }

        public void Rename(string name)
        {
            nodeName = name;
            nodePath = parent == null ? name : $"{parent}/{name}";
            foreach (var child in _children.Values)
                child.Rename(child.nodeName);
        }

        public IStateNode GetChild(string childName)
            => _children[childName];

        public bool TryGetChild(string childName, out IStateNode child)
            => _children.TryGetValue(childName, out child);

        public IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe((IObserverBase)new Observer<StateOperation>());
    }
}