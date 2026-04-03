using System;
using UnityEngine;

using FofX.Stateful;

namespace FofX
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

        public static void ExecuteTransaction(params StateTransaction<T>[] transactions)
        {
            foreach (var transaction in transactions)
                transaction.ExecuteTransaction(state);
        }
    }
}
