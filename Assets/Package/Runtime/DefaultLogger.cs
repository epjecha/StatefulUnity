using System;

namespace FofX
{
    public class DefaultLogger : ILogger
    {
        public LogLevel logLevel = LogLevel.Debug;

        public bool LevelEnabled(LogLevel logLevel)
            => this.logLevel <= logLevel;

        public void Generic(LogLevel logLevel, string message, Exception exception)
        {
            if (!LevelEnabled(logLevel))
                return;

            switch (logLevel)
            {
                case LogLevel.Trace:
                    Trace(message);
                    break;
                case LogLevel.Debug:
                    Debug(message);
                    break;
                case LogLevel.Info:
                    Info(message);
                    break;
                case LogLevel.Warn:
                    Warning(message);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    Error(message, exception);
                    break;
            }
        }

        public void Debug(string message)
        {
            if (LevelEnabled(LogLevel.Debug))
                UnityEngine.Debug.Log($"<color=#00FFFF>[DEBUG]</color> {message}");
        }

        public void Error(string message)
        {
            if (LevelEnabled(LogLevel.Error))
                UnityEngine.Debug.LogError($"<color=#FF0000>[ERROR]</color> {message}");
        }

        public void Error(Exception exception)
        {
            if (LevelEnabled(LogLevel.Error))
                UnityEngine.Debug.LogError($"<color=#FF0000>[ERROR]</color> {exception}");
        }

        public void Error(string message, Exception exception)
        {
            if (LevelEnabled(LogLevel.Error))
                UnityEngine.Debug.LogError($"<color=#FF0000>[ERROR]</color> Message: {message}\nException: {exception}");
        }

        public void Fatal(string message)
        {
            if (LevelEnabled(LogLevel.Fatal))
                UnityEngine.Debug.LogError($"<color=#FF0000>[ERROR]</color> {message}");
        }

        public void Info(string message)
        {
            if (LevelEnabled(LogLevel.Info))
                UnityEngine.Debug.Log($"<color=#00FF00>[INFO]</color> {message}");
        }

        public void Trace(string message)
        {
            if (LevelEnabled(LogLevel.Trace))
                UnityEngine.Debug.Log($"[TRACE] {message}");
        }

        public void Warning(string message)
        {
            if (LevelEnabled(LogLevel.Warn))
                UnityEngine.Debug.LogWarning($"<color=#FFFF00>[WARN]</color> Message: {message}");
        }
    }
}