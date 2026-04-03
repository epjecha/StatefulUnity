using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FofX.Serialization;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IStateValueArray<T> : IStateValue<IReadOnlyCollection<T>>, IEnumerable<T>
    {
        int count { get; }
    }

    public class StateValueArray<T> : StateValue<IReadOnlyCollection<T>>, IStateValueArray<T>
    {
        IEnumerator<T> IEnumerable<T>.GetEnumerator()
            => value?.GetEnumerator() ?? EmptyEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => value?.GetEnumerator() ?? EmptyEnumeratorObject();

        public int count => value?.Count ?? 0;

        private IEnumerator<T> EmptyEnumerator()
        {
            yield break;
        }

        private IEnumerator EmptyEnumeratorObject()
        {
            yield break;
        }

        public override string ToJSON(Func<IStateNode, bool> filter)
        {
            if (value == null)
                return JSONNull.CreateOrGet();

            JSONArray json = new JSONArray();
            var serializer = JSONSerialization.GetSerializer<T>();

            if (value != null)
            {
                foreach (var item in value)
                    json.Add(serializer.toJSON(item));
            }

            return json;
        }

        public override void FromJSON(string json)
        {
            var data = JSONNode.Parse(json);

            if (data.IsNull)
            {
                value = null;
                return;
            }

            var serializer = JSONSerialization.GetSerializer<T>();
            value = ((JSONArray)data).Linq.Select(x => serializer.fromJSON(x)).ToArray();
        }
    }
}