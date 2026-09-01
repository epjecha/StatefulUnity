using System;
using System.Collections.Generic;

using ObserveThing;
using SimpleJSON;

namespace FofX.Stateful
{
    public interface IReadOnlyStateValue : IStateNode, IValueObservable
    {
        object value { get; }
        Type valueType { get; }
    }

    public interface IStateValue : IReadOnlyStateValue
    {
        new object value { get; set; }

        object IReadOnlyStateValue.value => value;
    }

    public interface IStateValueViewMutator<T>
    {
        T value { get; set; }
    }

    public class StateValueView<T> : ReadOnlyStateValue<T>
    {
        public override bool isView => true;
        private Mutator _mutator;
        private bool _viewInitialized;
        private IDisposable _subscription;

        private class Mutator : IStateValueViewMutator<T>
        {
            public Func<T> get;
            public Action<T> set;

            public T value
            {
                get => get();
                set => set(value);
            }
        }

        public StateValueView()
        {
            _mutator = new Mutator() { get = () => value, set = x => value = x };
        }

        public void InitializeView(Action<IStateValueViewMutator<T>> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            initialize(_mutator);
        }

        public void InitializeView(Func<IStateValueViewMutator<T>, IDisposable> initialize)
        {
            if (_viewInitialized)
                throw new Exception($"View already initialized. Path: {nodePath}");

            _viewInitialized = true;
            _subscription = initialize(_mutator);
        }

        public override void CopyTo(IStateNode copyTo)
        {
            logger.Warning($"{nodePath} is a view. \'CopyTo\' will be ignored.");
        }

        public override void FromJSON(JSONNode json)
        {
            logger.Warning($"{nodePath} is a view. \'FromJSON\' will be ignored.");
        }

        public override void Reset()
        {
            logger.Warning($"{nodePath} is a view. \'Reset\' will be ignored for this object. Children will be reset.");
        }

        protected override void DisposeInternal()
        {
            _subscription?.Dispose();
            base.DisposeInternal();
        }
    }

    public class StateValue<T> : ReadOnlyStateValue<T>, IStateValue
    {
        public override bool isView => false;

        new public T value
        {
            get => base.value;
            set => base.value = value;
        }

        object IStateValue.value
        {
            get => value;
            set => this.value = (T)value;
        }

        private Action<StateValue<T>> _initializer;

        public StateValue() : this(default) { }
        public StateValue(Action<StateValue<T>> initializer) : base()
        {
            _initializer = initializer;
        }

        protected override void InitializeInternal()
        {
            base.InitializeInternal();
            _initializer?.Invoke(this);
        }

        public override void Reset()
        {
            logger.Generic(LogLevel.Trace, $"Resetting {nodePath}");
            value = default;
            _initializer?.Invoke(this);
        }

        public override void FromJSON(JSONNode json)
        {
            if (json == null)
            {
                Reset();
                return;
            }

            value = JSONSerialization.FromJSON<T>(json);
        }

        public override void CopyTo(IStateNode copyTo)
            => ((StateValue<T>)copyTo).value = value;
    }

    public abstract class ReadOnlyStateValue<T> : StateNode,
        IReadOnlyStateValue,
        IValueObservable<T>
    {
        public Type valueType => typeof(T);
        public T value
        {
            get => _value.value;
            protected set
            {
                if (Equals(value, _value.value))
                    return;

                logger.Trace(Utility.FormatOperationLog(OpType.Set, this, value));
                _value.value = value;
            }
        }

        public override IEnumerable<IStateNode> children => EmptyChildren();
        public override int childCount => 0;

        object IReadOnlyStateValue.value => value;

        private ObservableValue<T> _value;

        protected override void InitializeInternal()
        {
            _value = new ObservableValue<T>(context);
        }

        private IEnumerable<IStateNode> EmptyChildren()
        {
            yield break;
        }

        protected override IStateNode GetChildInternal(string childName)
            => throw new NotImplementedException();

        protected override bool TryGetChildInternal(string childName, out IStateNode child)
        {
            child = default;
            return false;
        }

        public override JSONNode ToJSON(Func<IStateNode, bool> filter)
            => JSONSerialization.ToJSON(value);

        public override IDisposable Subscribe(ObserveThing.IObserver<IOperation> observer, bool immediate = false, uint? priority = null)
            => _value.Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public override IDisposable Subscribe(ObserveThing.IObserver<StateOperation> observer, bool immediate = false, uint? priority = null)
            => _value.Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(new StateOperation() { source = this, opType = OpType.Set, param = x }),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver observer, bool immediate = false, uint? priority = null)
            => _value.Subscribe(new ValueObserver<T>(
                onNext: x => observer.OnNext(x),
                onDispose: observer.OnDispose,
                onError: observer.OnError
            ), immediate, priority);

        public IDisposable Subscribe(IValueObserver<T> observer, bool immediate = false, uint? priority = null)
            => _value.Subscribe(observer, immediate, priority);
    }
}