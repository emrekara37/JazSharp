﻿using JazSharp.Reflection;
using JazSharp.Testing.AssemblyLoading;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JazSharp.Testing
{
    /// <summary>
    /// Represents a test execution plan. <see cref="ExecuteAsync"/> can be called to execute
    /// the tests.
    /// </summary>
    public sealed class TestRun : IDisposable
    {
        private readonly AssemblyContext _assemblyContext;
        private readonly MethodInfo _setupTestExecutionContextMethod;
        private readonly MethodInfo _clearTestExecutionContextMethod;
        private readonly IEnumerable<string> _temporaryDirectories;
        private bool _isExecuting;
        private CancellationTokenSource _cancellationTokenSource;

        /// <summary>
        /// The tests planned for execution.
        /// </summary>
        public IReadOnlyList<Test> Tests { get; }

        /// <summary>
        /// An event that is triggered when one of the tests has completed. This will
        /// not trigger on the same thread as the call to <see cref="ExecuteAsync"/>.
        /// </summary>
        public event Action<TestResultInfo> TestCompleted;

        /// <summary>
        /// An event that is triggered when all tests have completed. This will not trigger
        /// on the same thread as the call to <see cref="ExecuteAsync"/>.
        /// </summary>
        public event Action TestRunCompleted;

        private TestRun(AssemblyContext assemblyContext, IEnumerable<Test> tests, IEnumerable<string> temporaryDirectories)
        {
            _assemblyContext = assemblyContext;
            _temporaryDirectories = temporaryDirectories;
            Tests = ImmutableArray.CreateRange(tests);

            var jazAssembly = _assemblyContext.LoadedAssemblies[typeof(Jaz).Assembly.GetName().Name];
            var jaz = jazAssembly.GetType(typeof(Jaz).FullName);

            _setupTestExecutionContextMethod = jaz.GetMethod(nameof(Jaz.SetupTestExecutionContext), BindingFlags.Static | BindingFlags.NonPublic);
            _clearTestExecutionContextMethod = jaz.GetMethod(nameof(Jaz.ClearTestExecutionContext), BindingFlags.Static | BindingFlags.NonPublic);

            jazAssembly
                .GetType(typeof(AssemblyContext).FullName)
                .GetMethod(nameof(AssemblyContext.SetupCurrent), BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { _assemblyContext.LoadedAssemblies });
        }

        /// <summary>
        /// Executes the test run and returns the results when awaited.
        /// </summary>
        /// <returns>A task to retrieve the results of the test run.</returns>
        public Task<TestResultInfo[]> ExecuteAsync()
        {
            if (_isExecuting)
            {
                throw new InvalidOperationException("A test run can only be executed once at a time.");
            }

            _isExecuting = true;
            _cancellationTokenSource = new CancellationTokenSource();

            return ExecuteSequenceAsync();
        }

        /// <summary>
        /// Cancels the active test run (if any). This call is ignored if no test run is in progress.
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }

        /// <summary>
        /// Disposes the test run by deleting the temporary shadow-copy directories that were created.
        /// </summary>
        public void Dispose()
        {
            _assemblyContext.Dispose();

            foreach (var directory in _temporaryDirectories)
            {
                //TODO: Delete temporary directory once assembly is unloaded.
                //Directory.Delete(directory, true);
            }
        }

        private Task<TestResultInfo[]> ExecuteSequenceAsync()
        {
            return Task.Run(async () =>
            {
                List<TestResultInfo> results = new List<TestResultInfo>();

                foreach (var test in Tests)
                {
                    var result = await TestExecutionAsync(test, Tests.Any(x => x.IsFocused));
                    results.Add(result);

                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        return results.ToArray();
                    }
                }

                TestRunCompleted?.Invoke();

                return results.ToArray();
            });
        }

        private async Task<TestResultInfo> TestExecutionAsync(Test test, bool onlyFocused)
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
            {
                return null;
            }

            Stopwatch stopwatch = new Stopwatch();
            StringBuilder output = new StringBuilder();
            Exception exception = null;
            TestResult testResult;

            await Jaz.CurrentTestSemaphore.WaitAsync();
            _setupTestExecutionContextMethod.Invoke(null, new object[] { test.FullName, output });

            if (test.IsExcluded)
            {
                testResult = TestResult.Skipped;
                output.Append("Test skipped due to having been defined with xDescribe or xIt.");
            }
            else if (onlyFocused && !test.IsFocused)
            {
                testResult = TestResult.Skipped;
                output.Append("Test skipped due to other tests being defined with fDescribe or fIt.");
            }
            else
            {
                try
                {
                    stopwatch.Start();

                    foreach (var method in test.Execution.GetDelegates())
                    {
                        if (method is Action action)
                        {
                            action.Invoke();
                        }
                        else
                        {
                            await ((Func<Task>)method).Invoke();
                        }
                    }

                    stopwatch.Stop();
                    output.Append(Environment.NewLine).Append("Test completed successfully.");
                    testResult = TestResult.Passed;
                }
                catch (Exception ex)
                {
                    var innermostException = ex;

                    while (innermostException.InnerException != null)
                    {
                        innermostException = innermostException.InnerException;
                    }

                    output.Append(Environment.NewLine).Append(innermostException.Message);
                    exception = ex;
                    testResult = TestResult.Failed;
                }
            }

            _clearTestExecutionContextMethod.Invoke(null, new object[0]);
            Jaz.CurrentTestSemaphore.Release();

            var result = new TestResultInfo(test, testResult, output.ToString().Trim(), exception, stopwatch.Elapsed);

            try
            {
                TestCompleted?.Invoke(result);
            }
            catch
            {
            }

            return result;
        }

        /// <summary>
        /// Creates a test run from a test collection - using the current tests in the collection
        /// as the source of tests to be executed. Changing the test collection after this call
        /// will not affect the created test run.
        /// </summary>
        /// <param name="testCollection">The test collection providing tests for the run.</param>
        /// <returns>The created test run.</returns>
        public static TestRun FromCollection(TestCollection testCollection)
        {
            if (testCollection == null)
            {
                throw new ArgumentNullException(nameof(testCollection));
            }

            var sourcesGroupedByDirectory = testCollection.Sources.GroupBy(Path.GetDirectoryName).Distinct();
            var sources = new List<string>();
            var temporaryDirectories = new List<string>();

            foreach (var directory in sourcesGroupedByDirectory)
            {
                var shadowcopyTargetDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                if (Directory.Exists(shadowcopyTargetDirectory))
                {
                    Directory.Delete(shadowcopyTargetDirectory, true);
                }

                CopyDirectory(directory.Key, shadowcopyTargetDirectory);
                temporaryDirectories.Add(shadowcopyTargetDirectory);

                foreach (var source in directory)
                {
                    var newSourcePath = source.Replace(directory.Key, shadowcopyTargetDirectory);
                    sources.Add(newSourcePath);
                }

                foreach (var dll in Directory.GetFiles(shadowcopyTargetDirectory, "*.dll", SearchOption.TopDirectoryOnly))
                {
                    var dllLower = Path.GetFileNameWithoutExtension(dll).ToLower();

                    if (dllLower != "jazsharp" && dllLower != "jazsharp.testadapter")
                    {
                        AssemblyRewriteHelper.RewriteAssembly(dll);
                    }
                }
            }

            var assemblyContext = new AssemblyContext();
            var executionReadyAssemblies = sources.Select(assemblyContext.Load).ToList();
            assemblyContext.InitialiseDependencyResolvers();
            var jazSharpAssembly = assemblyContext.LoadByName(typeof(Jaz).Assembly.GetName());

            return
                new TestRun(
                    assemblyContext,
                    testCollection.Tests.Select(x =>
                        x.Prepare(
                            executionReadyAssemblies.First(y => y.FullName == x.Execution.Main.Method.Module.Assembly.FullName),
                            jazSharpAssembly)),
                    temporaryDirectories);
        }

        private static void CopyDirectory(string source, string destination)
        {
            foreach (string dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(source, destination));
            }

            foreach (string newPath in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(destination);
                File.Copy(newPath, newPath.Replace(source, destination), true);
            }
        }
    }
}
