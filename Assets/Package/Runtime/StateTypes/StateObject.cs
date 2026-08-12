using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public struct StateObjectOperation : IStateOperation
    {
        public IStateNode source { get; set; }
        public OpType opType => OpType.None;
        public object param { get; }

        uint IStateOperation.elementId { get; }
        IStateNode IStateOperation.child { get; }

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={param}";
        }
    }

    public class StateObject : StateNode<IObserverBase, StateObjectOperation>
    {
        public override int childCount => _children.Count;
        public override IEnumerable<IStateNode> children => _children.Values;
        public override bool derived => false;

        private Dictionary<string, IStateNode> _children = new Dictionary<string, IStateNode>();

        public StateObject() : base() { }

        protected override void InitializeInternal()
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
                        x.Name != nameof(parent) &&
                        x.Name != nameof(root)
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

        protected override IEnumerable<StateObjectOperation> GetInitializationOperations()
        {
            yield break;
        }

        protected override void SendStateOperation(IObserverBase observer, StateObjectOperation operation)
        {
            throw new NotImplementedException();
        }

        protected override IStateNode GetChildInternal(string childName)
            => _children[childName];

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
            => _children.TryGetValue(childName, out child);

        public override void Reset()
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

            foreach (var child in children)
                child.FromJSON(json[child.nodeName]);
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
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

        public override void CopyTo(IStateNode copyTo)
        {
            foreach (var child in children)
                child.CopyTo(copyTo.GetChild(child.nodeName));
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe((IObserverBase)new Observer<IStateOperation>());
    }
}