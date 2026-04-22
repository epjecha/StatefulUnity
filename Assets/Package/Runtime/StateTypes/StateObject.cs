using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using SimpleJSON;

namespace FofX.Stateful
{
    public class StateObject : StateNode<object>
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

        protected override IStateNode GetChildInternal(string childName)
            => _children[childName];

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
            => _children.TryGetValue(childName, out child);

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((StateObject)copyTo);

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
    }
}