using System;
using System.IO;
using System.Diagnostics;

namespace MyDiagnostics
{
    public static class Precondition
    {
        public static void Check(bool condition, string message)
        {
            if (!condition)
            {
                StackFrame frame = new StackTrace(1, true).GetFrame(0);

                string fileName = frame != null
                    ? Path.GetFileName(frame.GetFileName())
                    : "Unknown File";

                int lineNumber = frame != null
                    ? frame.GetFileLineNumber()
                    : 0;

                Console.WriteLine(
                    "[PRECONDITION FAILED] in {0}:{1}{2}",
                    fileName,
                    lineNumber,
                    message == null ? "" : " => " + message);
            }
        }
    }
}