using System;
using System.Diagnostics;

namespace MyDiagnostics
{
    public static class SoftAssert
    {
        public static void Check(bool condition, string message)
        {
            if (!condition)
            {
                // Pass '1' to skip the current frame (SoftAssert.Check itself)
                // Pass 'true' to capture source file name and line number
                StackTrace trace = new StackTrace(1, true);
                StackFrame frame = trace.GetFrame(0);

                string fileName = frame != null ? frame.GetFileName() : "Unknown File";
                int lineNumber = frame != null ? frame.GetFileLineNumber() : 0;
                var method = frame != null ? frame.GetMethod() : null;
                string methodName = method != null ? method.DeclaringType.FullName + "." + method.Name : "Unknown Method";

                // Output to standard output (or Trace)
                Console.WriteLine("[SOFT ASSERT FAILED] {0}", message);
                Console.WriteLine("  -> at {0}", methodName);
                Console.WriteLine("  -> in {0}:line {1}", fileName, lineNumber);
            }
        }
    }
}