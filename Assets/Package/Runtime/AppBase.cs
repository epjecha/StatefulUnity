using System;
using System.Collections.Concurrent;
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

        public static void ExecuteTransaction(Action transaction)
        {
            state.context.PauseExecution();
            transaction();
            state.context.ResumeExecution();
        }
    }
}
