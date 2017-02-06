using System;
using System.Globalization;
using JetBrains.Annotations;

namespace HookComm
{
    public class StdErrLogger : ILogger
    {
        private readonly string prefix;

        public StdErrLogger([NotNull] string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(prefix));
            this.prefix = prefix;
        }

        public void Log([NotNull] string message)
        {
            Console.Error.WriteLine($"{prefix} {DateTime.Now:mm:ss.fff}> {message}");
        }


        /*

        public void Log(IFormattable formattable)
        {
            if (formattable == null) throw new ArgumentNullException(nameof(formattable));

            Console.Error.WriteLine($"{prefix}> {formattable.ToString(null, CultureInfo.InvariantCulture)}");
        }*/
    }
}