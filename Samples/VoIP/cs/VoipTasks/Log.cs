using System;
using System.IO;
using Windows.Storage;
using Windows.System.Diagnostics;

namespace VoipTasks
{
    public sealed class Log
    {
        static readonly object logLock = new object();

        static bool didInitialize;
        static uint processId;
        static StreamWriter logWriter;

        static void EnsureInitialization()
        {
            if (didInitialize)
            {
                return;
            }

            didInitialize = true;

            var processInfo = ProcessDiagnosticInfo.GetForCurrentProcess();
            var logFileName = $"{processInfo.ExecutableFileName}.log";
            processId = processInfo.ProcessId;
            
            var appDataFolder = ApplicationData.Current.LocalFolder.Path;
            logWriter = File.AppendText(Path.Combine(appDataFolder, logFileName));
            logWriter.AutoFlush = true;
        }

        public static void WriteLine(string lineText)
        {
            var logTime = DateTimeOffset.Now;
            var threadId = Environment.CurrentManagedThreadId;

            lock (logLock)
            {
                EnsureInitialization();

                logWriter.Write("[P:{0} T:{1:D2} {2:dd-MM-yy HH:mm:ss.fff}]: ", processId, threadId, logTime);
                logWriter.WriteLine(lineText);
            }
        }
    }
}
