// using System;
// using System.Collections.Generic;
// using System.Linq;
// using ObserveThing;
// using IStateObserver = ObserveThing.IObserver<FofX.Stateful.IStateOperation>;

// namespace FofX.Stateful
// {
//     public class CombineStateObservable : ObserveThing.IObservable<IStateOperation>, IDisposable
//     {
//         private class ObserverData : IPendingObserver, IDisposable
//         {
//             public IStateObserver observer { get; }
//             public uint priority { get; }
//             public bool immediate => observer.immediate;
//             public bool pending;
//             public bool disposed { get; private set; }

//             private List<IStateOperation> _pendingOperations;
//             private List<IStateOperation> _pendingOperations1 = new List<IStateOperation>();
//             private List<IStateOperation> _pendingOperations2 = new List<IStateOperation>();

//             private Action<ObserverData> _onDispose;

//             public ObserverData(IStateObserver observer, uint priority, Action<ObserverData> onDispose)
//             {
//                 this.observer = observer;
//                 this.priority = priority;

//                 _onDispose = onDispose;

//                 SwitchPendingOperationsList();
//             }

//             private void SwitchPendingOperationsList()
//             {
//                 if (_pendingOperations == _pendingOperations1)
//                 {
//                     _pendingOperations = _pendingOperations2;
//                 }
//                 else
//                 {
//                     _pendingOperations = _pendingOperations1;
//                 }
//             }

//             public void EnqueuePendingOperation(IStateOperation operation)
//             {
//                 _pendingOperations.Add(operation);
//             }

//             public void SendNext()
//             {
//                 if (_pendingOperations.Count == 0)
//                     return;

//                 var ops = _pendingOperations;
//                 SwitchPendingOperationsList();
//                 pending = false;

//                 try
//                 {
//                     observer.OnOperation(ops);
//                 }
//                 catch (Exception exc)
//                 {
//                     observer.OnError(exc);
//                 }

//                 ops.Clear();
//             }

//             public void Dispose()
//             {
//                 if (disposed)
//                     return;

//                 disposed = true;

//                 _onDispose?.Invoke(this);

//                 observer.OnDispose();
//             }
//         }

//         public ObservationContext context { get; protected set; }
//         public bool disposed { get; private set; }

//         private List<ObserverData> _observers = new List<ObserverData>();
//         private ISetObservable<IStateNode> _source;
//         private bool _disposeOnSourceEmpty;
//         private IDisposable _sourceSubscription;
//         private Dictionary<IStateNode, IDisposable> _observables = new Dictionary<IStateNode, IDisposable>();
//         private bool _initializingAddedState;

//         private Queue<Operation<IStateOperation>> _opPool = new Queue<Operation<IStateOperation>>();
//         private List<Operation<IStateOperation>> _opList = new List<Operation<IStateOperation>>();

//         public CombineStateObservable(ObservationContext context, ISetObservable<IStateNode> source, bool disposeOnSourceEmpty = false)
//         {
//             this.context = context ?? Settings.DefaultObservationContext;
//             _source = source;
//             _disposeOnSourceEmpty = disposeOnSourceEmpty;
//         }

//         private void HandleObserverDisposed(ObserverData data)
//         {
//             if (disposed)
//                 return;

//             if (!_observers.Remove(data))
//                 return;

//             if (_observers.Count == 0)
//                 OnLastLastRemoved();

//             context.DeallocateObserverPriority(data.priority);
//         }

//         protected void HandleCombinedSourceChanged(IReadOnlyList<IStateOperation> operations)
//         {
//             if (disposed)
//                 throw new ObjectDisposedException(GetType().Name);

//             if (_initializingAddedState)
//                 return;

//             foreach (var op in operations)
//             {
//                 var opClone = op.Clone();

//                 foreach (var observer in _observers)
//                 {
//                     observer.EnqueuePendingOperation(opClone);

//                     if (!observer.pending)
//                         context.RegisterPendingObserver(observer);
//                 }
//             }

//             context.NotifyPendingObserversIfNecessary();
//         }

//         protected IReadOnlyList<IStateOperation> GetInitializationOperations()
//         {
//             List<IStateOperation> ops = new List<IStateOperation>();
//             foreach (var observable in _observables.Keys)
//             {
//                 var initSubscription = observable.Subscribe(x => ops.AddRange(x.Select(x => x.Clone())));
//                 initSubscription.Dispose();
//             }
//             return ops;
//         }

//         protected void OnFirstObserverAdded()
//         {
//             _sourceSubscription = _source.Subscribe(
//                 onAdd: HandleSourceAdded,
//                 onRemove: HandleSourceRemoved,
//                 onError: OnError,
//                 onDispose: Dispose,
//                 immediate: true
//             );
//         }

//         protected void OnLastLastRemoved()
//         {
//             _sourceSubscription?.Dispose();
//             _sourceSubscription = null;
//         }

//         private void HandleSourceAdded(IStateNode observable)
//         {
//             bool wasInitializingAddedState = _initializingAddedState;
//             _initializingAddedState = true;

//             _observables.Add(
//                 observable,
//                 observable.Subscribe(
//                     onOperation: HandleCombinedSourceChanged,
//                     onError: OnError,
//                     onDispose: () => HandleSourceRemoved(observable),
//                     immediate: true
//                 )
//             );

//             _initializingAddedState = wasInitializingAddedState;
//         }

//         private void HandleSourceRemoved(IStateNode observable)
//         {
//             if (!_observables.TryGetValue(observable, out var subscription))
//                 return;

//             _observables.Remove(observable);
//             subscription.Dispose();

//             if (_disposeOnSourceEmpty && _observables.Count == 0)
//                 Dispose();
//         }

//         protected void OnError(Exception error)
//         {
//             foreach (var observer in _observers.OrderByDescending(x => x.immediate).ThenBy(x => x.priority))
//                 observer.observer.OnError(error);
//         }

//         public IDisposable Subscribe(IStateObserver observer)
//         {
//             if (disposed)
//             {
//                 var disposed = new Disposable(observer.OnDispose);
//                 disposed.Dispose();
//                 return disposed;
//             }

//             var observerData = new ObserverData(observer, context.AllocateObserverPriority(), HandleObserverDisposed);

//             if (_observers.Count == 0)
//                 OnFirstObserverAdded();

//             // do this after calling OnFirstSubscriberAdded so any resulting operations won't be queued (they'll be reflected in GetInitializationOperations)
//             _observers.Add(observerData);
//             observer.OnOperation(GetInitializationOperations());

//             return observerData;
//         }

//         public IDisposable Subscribe(IObserver observer)
//             => Subscribe(new Observer<IStateOperation>(
//                 onOperation: ops =>
//                 {
//                     foreach (var op in ops)
//                     {
//                         if (!_opPool.TryDequeue(out var operation))
//                             operation = new Operation<IStateOperation>(this);

//                         operation.value = op;
//                         _opList.Add(operation);
//                     }

//                     observer.OnOperation(_opList);

//                     foreach (var op in _opList)
//                     {
//                         op.value = default;
//                         _opPool.Enqueue(op);
//                     }

//                     _opList.Clear();
//                 },
//                 onError: observer.OnError,
//                 onDispose: observer.OnDispose,
//                 immediate: observer.immediate
//             ));

//         public void Dispose()
//         {
//             if (disposed)
//                 return;

//             disposed = true;

//             foreach (var observer in _observers.OrderByDescending(x => x.immediate).ThenBy(x => x.priority))
//             {
//                 observer.Dispose();
//                 context.DeallocateObserverPriority(observer.priority);
//             }

//             _observers.Clear();
//         }
//     }
// }