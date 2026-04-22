using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using ObserveThing;

namespace FofX.Stateful.Tests
{
    public class StateTests
    {
        [SetUp]
        public void SetUp()
        {
            Settings.DefaultExceptionHandler = UnityEngine.Debug.LogException;
        }

        public class TestState : StateObject
        {
            public StateDictionary<int, StateValue<string>> dict { get; private set; }
        }

        [Test]
        public void TestHierarchy()
        {
            var context = new ObservationContext();
            var state = new TestState();

            state.Initialize(context, new DefaultLogger() { logLevel = LogLevel.Trace });

            Assert.AreEqual("root", state.nodeName);
            Assert.AreEqual("root", state.nodePath);
            Assert.AreEqual(context, state.context);
            Assert.AreEqual(state, state.root);
            Assert.AreEqual(null, state.parent);
            Assert.AreEqual(1, state.children.Count());
            Assert.AreEqual("dict", state.dict.nodeName);
            Assert.AreEqual("root/dict", state.dict.nodePath);
            Assert.AreEqual(state, state.dict.parent);
            Assert.AreEqual(state, state.dict.root);
            Assert.AreEqual(context, state.dict.context);

            state.dict.Add(1);

            var added = state.dict[1];

            Assert.AreEqual(1, state.dict.count);
            Assert.AreEqual("1", added.nodeName);
            Assert.AreEqual("root/dict/1", added.nodePath);
            Assert.AreEqual(state.dict, added.parent);
            Assert.AreEqual(state, added.root);
            Assert.AreEqual(context, added.context);

            state.dict.Remove(1);

            Assert.AreEqual(true, added.disposed);
            Assert.AreEqual(0, state.dict.count);

            int addCount = 0;
            int removeCount = 0;

            bool addedKey2 = false;
            bool removedKey2 = false;
            bool disposed = false;
            Exception exception = default;

            var dictStream = state.dict.Subscribe(
                onAdd: x =>
                {
                    addCount++;

                    if (x.Key == 2)
                        addedKey2 = true;
                },
                onRemove: x =>
                {
                    removeCount++;

                    if (x.Key == 2)
                        removedKey2 = true;
                },
                onDispose: () => disposed = true,
                onError: exc => exception = exc
            );

            Assert.AreEqual(0, addCount);
            Assert.AreEqual(0, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            state.dict.Add(2);

            Assert.AreEqual(1, addCount);
            Assert.AreEqual(0, removeCount);
            Assert.AreEqual(true, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            addedKey2 = false;
            state.dict.Add(3);

            Assert.AreEqual(2, addCount);
            Assert.AreEqual(0, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            state.dict.Remove(3);

            Assert.AreEqual(2, addCount);
            Assert.AreEqual(1, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            state.dict.Remove(3);

            Assert.AreEqual(2, addCount);
            Assert.AreEqual(1, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            state.dict.Remove(2);

            Assert.AreEqual(2, addCount);
            Assert.AreEqual(2, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(true, removedKey2);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            removedKey2 = false;
            dictStream.Dispose();

            Assert.AreEqual(2, addCount);
            Assert.AreEqual(2, removeCount);
            Assert.AreEqual(false, addedKey2);
            Assert.AreEqual(false, removedKey2);
            Assert.AreEqual(true, disposed);
            Assert.AreEqual(null, exception);

            bool calledStream1First = false;
            bool calledStream2First = false;

            var stream1 = state.dict.Subscribe(onAdd: x =>
            {
                if (!calledStream2First)
                    calledStream1First = true;
            });

            var stream2 = state.dict.Subscribe(onAdd: x =>
            {
                if (!calledStream1First)
                    calledStream2First = true;
            });

            state.dict.Add(1);

            Assert.IsTrue(calledStream1First);
            Assert.IsFalse(calledStream2First);

            stream1.Dispose();
            stream2.Dispose();

            state.dict.Clear();

            Assert.AreEqual(0, state.dict.count);

            disposed = false;
            var observedOps = new List<(IStateNode source, OpType opType, object param, IStateNode child)>();

            var streamAll = StateObservers.SubscribeRecursive(
                state,
                onOperation: ops =>
                {
                    if (ops == null)
                        return;

                    observedOps.AddRange(ops.Select<IStateOperation, (IStateNode source, OpType opType, object param, IStateNode child)>(x => new(x.source, x.opType, x.param, x.child)));
                },
                onDispose: () => disposed = true,
                onError: exc => exception = exc
            );

            Assert.AreEqual(0, observedOps.Count);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            StateValue<string> element10 = default;

            state.context.ExecuteBatchOperation(() =>
            {
                element10 = state.dict.Add(10);
                state.dict.Add(100);
                state.dict[100].value = "me";
                state.dict[10].value = "you";
                state.dict.Remove(10);
            });

            Assert.AreEqual(5, observedOps.Count);
            Assert.AreEqual(false, disposed);
            Assert.AreEqual(null, exception);

            Assert.AreEqual(
                observedOps,
                new (IStateNode source, OpType opType, object param, IStateNode child)[]
                {
                    new(state.dict, OpType.Add, KeyValuePair.Create(10, element10), element10),
                    new(state.dict, OpType.Add, KeyValuePair.Create(100, state.dict[100]), state.dict[100]),
                    new(state.dict[100], OpType.Set, "me", null),
                    new(element10, OpType.Set, "you", null),
                    new(state.dict, OpType.Remove, KeyValuePair.Create(10, element10), element10),
                }
            );

            observedOps.Clear();

            streamAll.Dispose();

            Assert.AreEqual(0, observedOps.Count);
            Assert.AreEqual(true, disposed);
        }

        private void AssertStateOpArgsEquals(IStateOperation args, IStateNode source, OpType opType, object param, IStateNode child)
        {
            Assert.AreEqual(source, args.source);
            Assert.AreEqual(opType, args.opType);
            Assert.AreEqual(param, args.param);
            Assert.AreEqual(child, args.child);
        }

        [Test]
        public void TestStateValueArray()
        {
            var array = new StateValueArray<int>();
            array.Initialize(Settings.DefaultObservationContext, new DefaultLogger() { logLevel = LogLevel.Debug }, "root");

            int callCount = 0;
            IReadOnlyList<int> value = default;

            var stream = array.Subscribe(
                x =>
                {
                    callCount++;
                    value = x;
                }
            );

            array.SetValue(new int[] { 1, 2, 3 });

            Assert.AreEqual(2, callCount);
            Assert.AreEqual(new int[] { 1, 2, 3 }, value);
        }
    }
}