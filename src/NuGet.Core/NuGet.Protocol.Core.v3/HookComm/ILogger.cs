using System;
using JetBrains.Annotations;

namespace HookComm
{
    public interface ILogger
    {
        void Log([NotNull] string message);
    }
}