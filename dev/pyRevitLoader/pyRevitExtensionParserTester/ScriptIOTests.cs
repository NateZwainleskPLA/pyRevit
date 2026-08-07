using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;

using NUnit.Framework;
using PyRevitLabs.PyRevit.Runtime;

namespace pyRevitExtensionParserTester {
    [TestFixture]
    public class ScriptIOTests {
        [Test]
        public void WorkerWriteDoesNotCreateWpfOutput() {
            var logPath = Path.Combine(
                Path.GetTempPath(),
                "pyrevit-worker-output-" + Guid.NewGuid().ToString("N") + ".log");

            try {
                var runtime = CreateRuntime(logPath);
                var outputStream = new ScriptIO(runtime);
                Exception? writeError = null;

                var worker = new Thread(() => {
                    try {
                        outputStream.write("worker output");
                    }
                    catch (Exception error) {
                        writeError = error;
                    }
                });
                worker.SetApartmentState(ApartmentState.MTA);
                worker.Start();

                Assert.That(worker.Join(TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(writeError, Is.Null);
                Assert.That(HasOutputWindow(runtime), Is.False);
                Assert.That(File.ReadAllText(logPath), Does.Contain("worker output"));
            }
            finally {
                if (File.Exists(logPath))
                    File.Delete(logPath);
            }
        }

        private static ScriptRuntime CreateRuntime(string logFilePath) {
#pragma warning disable SYSLIB0050 // A host-free runtime is required for this worker-thread test.
            var runtime = (ScriptRuntime)FormatterServices.GetUninitializedObject(
                typeof(ScriptRuntime));
#pragma warning restore SYSLIB0050
            SetProperty(runtime, "ScriptData", new ScriptData {
                CommandName = "Worker Output Test",
                CommandUniqueId = "worker-output-test"
            });
            SetProperty(runtime, "ScriptRuntimeConfigs", new ScriptRuntimeConfigs {
                LogFilePath = logFilePath
            });
            InitializeOutputReference(runtime);
            return runtime;
        }

        private static bool HasOutputWindow(ScriptRuntime runtime) {
            var outputReference = GetField(runtime, "_scriptOutput");
            var arguments = new object?[] { null };
            var tryGetTarget = outputReference.GetType().GetMethod("TryGetTarget");
            return (bool)tryGetTarget!.Invoke(outputReference, arguments)!;
        }

        private static object GetField(object target, string fieldName) {
            return target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic)
                !.GetValue(target)!;
        }

        private static void InitializeOutputReference(ScriptRuntime runtime) {
            var outputField = runtime.GetType().GetField(
                "_scriptOutput",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var outputReference = Activator.CreateInstance(
                outputField.FieldType,
                new object?[] { null });
            outputField.SetValue(runtime, outputReference);
        }

        private static void SetProperty(object target, string propertyName, object value) {
            target.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                !.SetValue(target, value);
        }
    }
}
