#if !DISABLE_ADDONS
using ChakraHost.Hosting;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ChakraHost.Hosting
{
    public class ChakraJavaScriptHost : IDisposable
    {
        public static ChakraJavaScriptHost Currect
        {
            get
            {
                if(currect==null)
                {
                    currect = new ChakraJavaScriptHost();
                    currect.Init();
                }
                return currect;
            }
            set
            {
                currect = value;
            }
        }
        [ThreadStatic]
        public static ChakraJavaScriptHost currect = null;
        public List<string> AllowNameSpace { get; set; } = new List<string>();
        private JavaScriptSourceContext currentSourceContext = JavaScriptSourceContext.FromIntPtr(IntPtr.Zero);
        private JavaScriptRuntime runtime;
        private Queue<JavaScriptValue> taskQueue = new Queue<JavaScriptValue>();
        public ChakraJavaScriptHost()
        {
        }
        public string Init(JavaScriptRuntimeAttributes jsrt= JavaScriptRuntimeAttributes.None)
        {
            JavaScriptContext context;

            Native.ThrowIfError(Native.JsCreateRuntime(jsrt, null, out runtime));
            Native.ThrowIfError(Native.JsCreateContext(runtime, out context));
            ResetContext(context);

            // ES6 Promise callback
            JavaScriptPromiseContinuationCallback promiseContinuationCallback = delegate (JavaScriptValue task, IntPtr callbackState)
            {
                taskQueue.Enqueue(task);
            };

            if (Native.JsSetPromiseContinuationCallback(promiseContinuationCallback, IntPtr.Zero) != JavaScriptErrorCode.NoError)
                return "failed to setup callback for ES6 Promise";

            foreach (var one in AllowNameSpace)
            {
                Native.ThrowIfError(Native.JsProjectWinRTNamespace(one));
            }

            if (Native.JsStartDebugging() != JavaScriptErrorCode.NoError)
                return "failed to start debugging.";

            return "NoError";
        }

        private static void ResetContext(JavaScriptContext context)
        {
            Native.ThrowIfError(Native.JsSetCurrentContext(context));
        }
        public JavaScriptValue RunScriptForJavaScriptValue(string script)
        {
            JavaScriptValue result;
            if (Native.JsRunScript(script, currentSourceContext++, string.Empty, out result) != JavaScriptErrorCode.NoError)
            {
                throw new Exception(GetErrorMessage());
            }

            // Execute promise tasks stored in taskQueue 
            while (taskQueue.Count != 0)
            {
                JavaScriptValue task = taskQueue.Dequeue();
                JavaScriptValue promiseResult;
                JavaScriptValue global;
                Native.JsGetGlobalObject(out global);
                JavaScriptValue[] args = new JavaScriptValue[1] { global };
                Native.JsCallFunction(task, args, 1, out promiseResult);
            }

            return result;
        }
        public string RunScript(string script)
        {
            IntPtr returnValue;

            try
            {
                JavaScriptValue result;
                if (Native.JsRunScript(script, currentSourceContext++, "", out result) != JavaScriptErrorCode.NoError)
                {
                    throw new Exception(GetErrorMessage());
                }

                // Execute promise tasks stored in taskQueue 
                while (taskQueue.Count != 0)
                {
                    JavaScriptValue task = taskQueue.Dequeue();
                    JavaScriptValue promiseResult;
                    JavaScriptValue global;
                    Native.JsGetGlobalObject(out global);
                    JavaScriptValue[] args = new JavaScriptValue[1] { global };
                    Native.JsCallFunction(task, args, 1, out promiseResult);
                }

                // Convert the return value.
                JavaScriptValue stringResult;
                UIntPtr stringLength;
                if (Native.JsConvertValueToString(result, out stringResult) != JavaScriptErrorCode.NoError)
                    return "failed to convert value to string.";
                if (Native.JsStringToPointer(stringResult, out returnValue, out stringLength) != JavaScriptErrorCode.NoError)
                    return "failed to convert return value.";
            }
            catch (Exception e)
            {
                return "chakrahost: fatal error: internal error: " + e.Message;
            }
            return Marshal.PtrToStringUni(returnValue);
        }
        private static string GetErrorMessage()
        {
            // Get error message and clear exception
            JavaScriptValue exception;
            if (Native.JsGetAndClearException(out exception) != JavaScriptErrorCode.NoError)
                return "failed to get and clear exception";

            JavaScriptPropertyId messageName;
            if (Native.JsGetPropertyIdFromName("message",
                out messageName) != JavaScriptErrorCode.NoError)
                return "failed to get error message id";

            JavaScriptValue messageValue;
            if (Native.JsGetProperty(exception, messageName, out messageValue)
                != JavaScriptErrorCode.NoError)
                return "failed to get error message";

            IntPtr message;
            UIntPtr length;
            if (Native.JsStringToPointer(messageValue, out message, out length) != JavaScriptErrorCode.NoError)
                return "failed to convert error message";

            return Marshal.PtrToStringUni(message);
        }

        public void Dispose()
        {
            System.Diagnostics.Debug.WriteLine("Disposing " + GetHashCode().ToString());

            try
            {
                Native.ThrowIfError(Native.JsSetCurrentContext(JavaScriptContext.Invalid));
            }
            catch (Exception e)
            {
                throw new Exception("JsSetCurrentContext faild" + GetErrorMessage(), e);
            }
            try
            {
                Native.ThrowIfError(Native.JsDisableRuntimeExecution(runtime));
            }
            catch (Exception e)
            {
                throw new Exception("JsDisableRuntimeExecution faild" + GetErrorMessage(), e);
            }
            try
            {
                runtime.Dispose();
            }
            catch (Exception e)
            {
                throw new Exception("JsDispose faild" + GetErrorMessage(), e);
            }
        }
    }
}
#endif 