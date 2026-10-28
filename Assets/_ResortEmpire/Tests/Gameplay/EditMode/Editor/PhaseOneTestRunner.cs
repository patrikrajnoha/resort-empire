using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace ResortEmpire.Gameplay.Tests
{
    /// <summary>Editor-only bridge used to start this assembly's tests through Unity Test Runner.</summary>
    public static class PhaseOneTestRunner
    {
        private static TestRunnerApi testRunnerApi;

        [UnityEditor.MenuItem("Resort Empire/Run Phase 1 Edit Mode Tests")]
        public static void Run()
        {
            SessionState.SetString("ResortEmpire.PhaseOneTestSummary", "requested");
            testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            testRunnerApi.hideFlags = HideFlags.HideAndDontSave;
            testRunnerApi.RegisterCallbacks(new Callbacks());
            testRunnerApi.RetrieveTestList(TestMode.EditMode, tests =>
            {
                testRunnerApi.Execute(new ExecutionSettings(new Filter
                {
                    testMode = TestMode.EditMode,
                    assemblyNames = new[] { "ResortEmpire.Gameplay.EditMode.Tests" }
                }));
            });
            Debug.Log("PHASE1_TEST_RUN_REQUESTED");
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                SessionState.SetString("ResortEmpire.PhaseOneTestSummary", "started:testCases=" + testsToRun.TestCaseCount);
                Debug.Log("PHASE1_TEST_RUN_STARTED");
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result.TestStatus == TestStatus.Failed)
                    Debug.LogError("PHASE1_TEST_FAILED=" + result.FullName + " :: " + result.Message);
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetString(
                    "ResortEmpire.PhaseOneTestSummary",
                    "finished:testCases=" + result.Test.TestCaseCount
                    + ":passed=" + result.PassCount
                    + ":failed=" + result.FailCount
                    + ":skipped=" + result.SkipCount
                    + ":inconclusive=" + result.InconclusiveCount
                    + ":status=" + result.TestStatus);
                Debug.Log(
                    "PHASE1_TEST_RUN_FINISHED"
                    + " testCases=" + result.Test.TestCaseCount
                    + " passed=" + result.PassCount
                    + " failed=" + result.FailCount
                    + " skipped=" + result.SkipCount
                    + " inconclusive=" + result.InconclusiveCount
                    + " status=" + result.TestStatus);
            }
        }
    }
}
