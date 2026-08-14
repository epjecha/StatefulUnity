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

    public struct StateOperation : IOperation
    {
        public IStateNode source;
        public OpType opType;
        public object param;
        public uint elementId;
        public IStateNode child;

        ObserveThing.IObservable<IOperation> IOperation.source => (ObserveThing.IObservable<IOperation>)source;

        public override string ToString()
            => $"[{opType}] source={source.nodePath} param={param?.ToString() ?? "NULL"} elementId={elementId} child={child?.nodeName ?? "NULL"}";
    }

    public interface IStateNode : ObserveThing.IObservable<StateOperation>, IDisposable
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
        bool TryGetChild(string name, out IStateNode child);

        IStateNode FindChild(string path)
        {
            var currDownstream = this;
            var pathSegments = path.Split('/');
            var startIndex = 0;

            if (pathSegments[0] == nodeName)
                startIndex = 1;

            for (int i = startIndex; i < pathSegments.Length; i++)
                currDownstream = currDownstream.GetChild(pathSegments[i]);

            return currDownstream;
        }

        bool TryFindChild(string path, out IStateNode child)
        {
            var currDownstream = this;
            var pathSegments = path.Split('/');
            var startIndex = 0;

            if (pathSegments[0] == nodeName)
                startIndex = 1;

            for (int i = startIndex; i < pathSegments.Length; i++)
            {
                if (!currDownstream.TryGetChild(pathSegments[i], out currDownstream))
                {
                    child = default;
                    return false;
                }
            }

            child = currDownstream;
            return true;
        }
    }
}