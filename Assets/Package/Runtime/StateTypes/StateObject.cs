using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public class StateObject : StateNode
    {
        public override IEnumerable<IStateNode> children => _children.Values;
        public override int childCount => _children.Count;
        public override bool isView => false;

        private Dictionary<string, IStateNode> _children = new Dictionary<string, IStateNode>();
        private Observable<StateOperation> _observable;

        public StateObject() : base() { }

        protected override void InitializeInternal()
        {
            _observable = new Observable<StateOperation>(context, default);

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

        public override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");

            foreach (var child in children.Where(x => !x.isView))
                child.Reset();
        }

        public override void FromJSON(JSONNode json)
        {
            if (isView)
            {
                logger.Warning($"Attempted to write to derived state from JSON. This will be ignored. Path: {nodePath}");
                return;
            }

            if (json == null)
            {
                Reset();
                return;
            }

            foreach (var child in children.Where(x => !x.isView))
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
            _observable.Dispose();
        }

        public override void CopyTo(IStateNode copyTo)
        {
            foreach (var child in children.Where(x => !x.isView))
                child.CopyTo(copyTo.GetChild(child.nodeName));
        }

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
            => _children.TryGetValue(childName, out child);

        protected override IStateNode GetChildInternal(string childName)
            => _children[childName];

        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => _observable.Subscribe(observer, immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => _observable.Subscribe(observer, immediate, priority);
    }
}