using System;
using System.IO;
using System.Diagnostics;

namespace MyDiagnostics
{
    public static class Precondition
    {
        public static void ASSERT(bool condition)
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

                string message = string.Format("[PRECONDITION FAILED] in {0}:{1}", fileName, lineNumber);
                throw new InvalidOperationException(message);
            }
        }
    }
}