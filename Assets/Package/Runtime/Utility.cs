using System;
using System.Collections.Generic;
using System.Linq;

namespace FofX.Stateful
{
    public static class Utility
    {
        public static void SetFrom<T>(this StateValueSet<T> set, IEnumerable<T> from)
        {
            if (from == null)
            {
                set.Clear();
                return;
            }

            var missing = set.Except(from).ToArray();
            var added = from.Except(set).ToArray();

            foreach (var toRemove in missing)
                set.Remove(toRemove);

            foreach (var toAdd in added)
                set.Add(toAdd);
        }

        public static void SetFrom<TKey, TValue>(this StateDictionary<TKey, TValue> dict, TKey[] keys, Action<KeyValuePair<TKey, TValue>> onAdd = default)
            where TValue : IStateNode, new()
        {
            if (keys == null)
            {
                dict.Clear();
                return;
            }

            var added = keys.Except(dict.keys).ToArray();
            var removed = dict.keys.Except(keys).ToArray();

            foreach (var keyToAdd in added)
            {
                var newValue = dict.Add(keyToAdd);
                onAdd?.Invoke(new KeyValuePair<TKey, TValue>(keyToAdd, newValue));
            }

            foreach (var keyToRemove in removed)
                dict.Remove(keyToRemove);
        }

        public static void SetFrom<TKey, TState, TSource>(this StateDictionary<TKey, TState> dict, Dictionary<TKey, TSource> source, bool refreshOldEntries = false, Action<TKey, TSource, TState> copy = default)
            where TState : IStateNode, new()
        {
            if (source == null)
            {
                dict.Clear();
                return;
            }

            var removed = dict.keys.Except(source.Keys).ToArray();

            if (refreshOldEntries)
            {
                foreach (var kvp in source)
                    copy?.Invoke(kvp.Key, kvp.Value, dict.GetOrAdd(kvp.Key));
            }
            else
            {
                foreach (var kvp in source)
                {
                    if (dict.TryGetValue(kvp.Key, out var dictValue))
                        continue;

                    copy?.Invoke(kvp.Key, kvp.Value, dict.Add(kvp.Key));
                }
            }

            foreach (var toRemove in removed)
                dict.Remove(toRemove);
        }

        public static string FormatOperationLog(OpType opType, IStateNode source, object param = default, IStateNode child = default)
            => $"[{opType}] source={source.nodePath} param={param?.ToString() ?? "NULL"} child={child?.nodeName ?? "NULL"}";
    }
}
