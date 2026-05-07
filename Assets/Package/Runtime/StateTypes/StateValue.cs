using System;
using System.Collections.Generic;
using System.Linq;

using ObserveThing;
using SimpleJSON;

using FofX.Serialization;

namespace FofX.Stateful
{
    public interface IStateValue : IStateNode
    {
        object value { get; set; }
        Type valueType { get; }

        void CopyTo(IStateValue copyTo);
    }

    public interface IStateValue<T> : IValueObservable<T>, IStateValue
    {
        new T value { get; set; }

        object IStateValue.value
        {
            get => value;
            set => this.value = (T)value;
        }

        Type IStateValue.valueType => typeof(T);

        void CopyTo(IStateValue<T> copyTo);

        void IStateValue.CopyTo(IStateValue copyTo)
            => CopyTo((IStateValue<T>)copyTo);
    }

    public class StateValue<T> : StateNode<T>, IStateValue<T>
    {
        public T value
        {
            get => _value.value;
            set
            {
                if (derived)
                    throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

                if (Equals(_value.value, value))
                    return;

                LogOperation(OpType.Set, value);
                _value.value = value;
            }
        }

        public override int childCount => 0;
        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override bool derived => _deriveStream != null;
        private IDisposable _deriveStream;

        private ObservableValue<T> _value;
        private Func<T> _getInitialValue;
        private List<StateOpArgs<T>> _initOps = new List<StateOpArgs<T>>();

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        public StateValue() : this(default(Func<T>)) { }

        public StateValue(T value) : this(() => value) { }
        public StateValue(Func<T> getInitialValue) : base()
        {
            _getInitialValue = getInitialValue;
            _initOps.Add(default);
        }

        protected override void InitializeInternal()
        {
            _value = _getInitialValue == null ?
                new ObservableValue<T>(context) : new ObservableValue<T>(context, _getInitialValue());

            _value.Subscribe(HandleInternalOperation, immediate: true);
        }

        protected override IReadOnlyList<StateOpArgs<T>> GetInitializationOperations()
        {
            _initOps[0] = new StateOpArgs<T>(this, OpType.Set, value);
            return _initOps;
        }

        private void HandleInternalOperation(IReadOnlyList<T> ops)
        {
            if (ops == null)
                return;

            foreach (var op in ops)
            {
                EnqueuePendingOperation(new StateOpArgs<T>(
                    source: this,
                    opType: OpType.Set,
                    param: op
                ));
            }
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        protected override void CopyToInternal(IStateNode copyTo)
            => CopyTo((IStateValue<T>)copyTo);

        public override void Reset()
        {
            if (derived)
                throw new Exception($"Directly editing derived state is not allowed. Path: {nodePath}");

            value = _getInitialValue == null ? default : _getInitialValue();

            logger.Generic(LogLevel.Trace, $"Reset {nodePath}");
        }

        public void CopyTo(IStateValue<T> copyTo)
        {
            copyTo.value = value;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
        {
            return JSONSerialization.ToJSON(value);
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

            value = JSONSerialization.FromJSON<T>(json);
        }

        protected override void DisposeInternal()
        {
            _deriveStream?.Dispose();
        }

        public void Derive(IValueObservable<T> source)
        {
            _deriveStream = source.Subscribe(x => _value.value = x, immediate: true);
        }

        public IDisposable Subscribe(IValueObserver<T> observer)
            => Subscribe(new Observer<StateOpArgs<T>>(
                onOperation: ops =>
                {
                    if (ops == null)
                    {
                        observer.OnNext(_value.value);
                        return;
                    }

                    foreach (var op in ops)
                        observer.OnNext(op.param);
                },
                onError: observer.OnError,
                onDispose: observer.OnDispose,
                immediate: observer.immediate
            ));
    }
}