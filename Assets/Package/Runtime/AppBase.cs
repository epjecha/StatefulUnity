using System;
using System.Collections.Generic;
using System.Linq;

using UnityEngine;

namespace FofX.Stateful
{
    public abstract class AppBase<T> : MonoBehaviour where T : IStateNode, new()
    {
        public static T state { get; private set; }
        private static AppBase<T> _instance;

        protected virtual void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                throw new Exception($"There can only be one instance of {nameof(AppBase<T>)} in the scene at a time.");
            }

            _instance = this;
            state = new T();
            InitializeState(state);
        }

        protected abstract void InitializeState(T state);

        public static void ExecuteTransaction(bool silent, IEnumerable<Action<T>> transactions)
        {
            var logLevel = state.logger.logLevel;

            if (silent && logLevel > LogLevel.Warn)
                state.logger.logLevel = LogLevel.Warn;

            state.context.ExecuteBatchOperation(() =>
            {
                foreach (var transaction in transactions)
                    transaction(state);
            });

            state.logger.logLevel = logLevel;
        }

        public static void ExecuteTransaction(bool silent, params Action<T>[] transactions)
            => ExecuteTransaction(silent, (IEnumerable<Action<T>>)transactions);

        public static void ExecuteTransaction(IEnumerable<Action<T>> transactions)
            => ExecuteTransaction(false, transactions);

        public static void ExecuteTransaction(params Action<T>[] transactions)
            => ExecuteTransaction(false, (IEnumerable<Action<T>>)transactions);

        public static void ExecuteTransaction(bool silent, IEnumerable<StateTransaction<T>> transactions)
            => ExecuteTransaction(silent, transactions.Select<StateTransaction<T>, Action<T>>(x => x.ExecuteTransaction));

        public static void ExecuteTransaction(bool silent, params StateTransaction<T>[] transactions)
            => ExecuteTransaction(silent, transactions.Select<StateTransaction<T>, Action<T>>(x => x.ExecuteTransaction));

        public static void ExecuteTransaction(IEnumerable<StateTransaction<T>> transactions)
            => ExecuteTransaction(false, transactions.Select<StateTransaction<T>, Action<T>>(x => x.ExecuteTransaction));

        public static void ExecuteTransaction(params StateTransaction<T>[] transactions)
            => ExecuteTransaction(false, transactions.Select<StateTransaction<T>, Action<T>>(x => x.ExecuteTransaction));
    }
}
