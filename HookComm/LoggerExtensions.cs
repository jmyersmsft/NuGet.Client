using System;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace HookComm
{
    public static class LoggerExtensions
    {
        /*
        public static void Log(this ILogger logger, IFormattable formattable)
        {
            logger.Log("Using IFormattable extension");
            logger.Log(formattable.ToString(null, CultureInfo.InvariantCulture));
        }*/
        /*
        class StringFormattable : IFormattable
        {
            public string Message { get; }

            public StringFormattable(string message)
            {
                Message = message;
            }

            public string ToString(string format, IFormatProvider formatProvider)
            {
                return Message;
            }
        }

        public static void Log([NotNull] this ILogger logger, [NotNull] string message)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (message == null) throw new ArgumentNullException(nameof(message));

            logger.Log(new StringFormattable(message));
        }*/
    }
}