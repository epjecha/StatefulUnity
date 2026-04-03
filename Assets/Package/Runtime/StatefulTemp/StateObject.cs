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
        public override int childCount => _children.Count;
        public override IEnumerable<IStateNode> children => _children.Values;
        public override bool derived => false;

        private Dictionary<string, IStateNode> _children = new Dictionary<string, IStateNode>();
        private SynchronizedNotificationQueue<IStateOpObserver, bool> _observable;

        public StateObject() : base()
        {
            PopulateChildren();
        }

        public StateObject(SynchronizationContext context, string name = "root") : base(context, name)
        {
            PopulateChildren();
        }

        private void PopulateChildren()
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

        protected override void InitializeInternal()
        {
            _observable = new SynchronizedNotificationQueue<IStateOpObserver, bool>((_, _) => { }, context);
        }

        protected override IStateNode GetChildInternal(string childName)
            => _children[childName];

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
            => _children.TryGetValue(childName, out child);

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((StateObject)copyTo);

        public override void Reset()
        {
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

        public override void FromJSON(string json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            var obj = JSONNode.Parse(json);

            foreach (var child in children)
                child.FromJSON(obj[child.nodeName]);
        }

        public override string ToJSON(Func<IStateNode, bool> filter)
        {
            JSONObject obj = new JSONObject();

            foreach (var child in children.Where(filter))
                obj.Add(child.nodeName, child.ToJSON(filter));

            return obj;
        }

        public override IDisposable Subscribe(IObserver observer)
            => Subscribe(new StateOpObserver(
                onOperation: _ => observer.OnChange(),
                onError: observer.OnError,
                onDispose: observer.OnDispose
            ));

        public override IDisposable Subscribe(IStateOpObserver observer)
            => _observable.AddObserver(new StateOpObserver(
                onOperation: observer.OnOperation,
                onError: observer.OnError,
                onDispose: () =>
                {
                    if (disposed)
                        observer.OnOperation(new StateOpArgs() { opType = OpType.Dispose, source = this });

                    observer.OnDispose();
                }
            ));

        protected override void DisposeInternal()
        {
            foreach (var child in children)
                child.Dispose();

            _observable.Dispose();
        }
    }
}