using System;
using System.Diagnostics;

namespace MyDiagnostics
{
    public static class Precondition
    {
        public static void Check(bool condition)
        {
            Check(condition, null);
        }

        public static void Check(bool condition, string message)
        {
            if (!condition)
            {
                StackTrace trace = new StackTrace(1, true);
                StackFrame frame = trace.GetFrame(0);

                string fileName = frame != null ? frame.GetFileName() : "Unknown File";
                int lineNumber = frame != null ? frame.GetFileLineNumber() : 0;
                fileName = fileName.Substring(fileName.LastIndexOf('\\') + 1);
                
                Console.WriteLine("[PRECONDITION FAILED] in {0}:{1}" + (message == null ? "" : " => {2}"), fileName, lineNumber, message);
            }
        }
    }
}