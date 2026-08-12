using System;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public struct StateValueOperation<T> : IStateOperation
    {
        public IStateNode source { get; set; }
        public OpType opType => OpType.Set;
        public T value { get; set; }

        uint IStateOperation.elementId => 0;
        object IStateOperation.param => value;
        public IStateNode child => null;

        public override string ToString()
        {
            return $"[{opType.ToString().ToUpper()}] source={source.nodePath} param={value}";
        }
    }

    public interface IStateValue : IStateNode, IValueObservable
    {
        object value { get; set; }
        Type valueType { get; }
    }

    public class StateValue<T> : StateNode<IValueObserver<T>, StateValueOperation<T>>,
        IStateValue,
        IValueObservable<T>
    {
        public T value
        {
            get => _value;
            set
            {
                if (derived)
                    throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

                SetInternal(value);
            }
        }

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;

        object IStateValue.value { get => value; set => this.value = (T)value; }

        public Type valueType => typeof(T);

        private IDisposable _deriveStream;

        private T _value;
        private Func<T> _getInitialValue;

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        public StateValue() : this(default(Func<T>)) { }

        public StateValue(T value) : this(() => value) { }
        public StateValue(Func<T> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
        }

        protected override void InitializeInternal()
        {
            value = _getInitialValue == null ? default : _getInitialValue();
        }

        protected override IEnumerable<StateValueOperation<T>> GetInitializationOperations()
        {
            yield return new StateValueOperation<T>() { source = this, value = value };
        }

        protected override void SendStateOperation(IValueObserver<T> observer, StateValueOperation<T> operation)
        {
            if (operation.opType == OpType.Set)
            {
                observer.OnNext(operation.value);
            }
            else
            {
                throw new Exception($"Unhandled op type {operation.opType}");
            }
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        public override void CopyTo(IStateNode copyTo)
        {
            ((StateValue<T>)copyTo).value = value;
        }

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            value = _getInitialValue == null ? default : _getInitialValue();

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
            => JSONSerialization.ToJSON(value);

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

            value = JSONSerialization.FromJSON<T>(json);
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        private void SetInternal(T value)
        {
            if (Equals(_value, value))
                return;

            _value = value;
            EnqueuePendingStateOperation(new() { source = this, value = _value });
        }

        public void Derive(IValueObservable<T> source)
        {
            _deriveStream = source.Subscribe(
                SetInternal,
                immediate: true
            );
        }

        public override IDisposable Subscribe(ObserveThing.IObserver<IStateOperation> observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(new StateValueOperation<T>() { source = this, value = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver observer, bool immediate = false, uint? priority = null)
            => Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(x),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);
    }
}