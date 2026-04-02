// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Interfaces;

namespace Sorcha.Blueprint.Engine.Testing;

/// <summary>
/// Test runner for JSON Logic expressions
/// </summary>
/// <remarks>
/// Allows running multiple test cases against a JSON Logic expression
/// to verify expected behavior
/// </remarks>
public class JsonLogicTester
{
    private readonly IJsonLogicEvaluator _evaluator;

    public JsonLogicTester(IJsonLogicEvaluator evaluator)
    {
        _evaluator = evaluator;
    }

    /// <summary>
    /// Run a single test case
    /// </summary>
    public async Task<TestResult> RunTestAsync(
        JsonNode expression,
        TestCase testCase,
        CancellationToken ct = default)
    {
        try
        {
            var startTime = DateTimeOffset.UtcNow;
            var result = _evaluator.Evaluate(expression, testCase.InputData);
            var duration = DateTimeOffset.UtcNow - startTime;

            var actualJson = JsonSerializer.Serialize(result);
            var expectedJson = JsonSerializer.Serialize(testCase.ExpectedOutput);

            var passed = actualJson == expectedJson;

            return new TestResult
            {
                Name = testCase.Name,
                Passed = passed,
                ActualOutput = result,
                ExpectedOutput = testCase.ExpectedOutput,
                InputData = testCase.InputData,
                Duration = duration,
                Error = null
            };
        }
        catch (Exception ex)
        {
            return new TestResult
            {
                Name = testCase.Name,
                Passed = false,
                ActualOutput = null,
                ExpectedOutput = testCase.ExpectedOutput,
                InputData = testCase.InputData,
                Duration = TimeSpan.Zero,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Run multiple test cases
    /// </summary>
    public async Task<TestReport> RunTestsAsync(
        JsonNode expression,
        IEnumerable<TestCase> testCases,
        CancellationToken ct = default)
    {
        var results = new List<TestResult>();
        var startTime = DateTimeOffset.UtcNow;

        foreach (var testCase in testCases)
        {
            var result = await RunTestAsync(expression, testCase, ct);
            results.Add(result);
        }

        var duration = DateTimeOffset.UtcNow - startTime;

        return new TestReport
        {
            TotalTests = results.Count,
            PassedTests = results.Count(r => r.Passed),
            FailedTests = results.Count(r => !r.Passed),
            Results = results,
            TotalDuration = duration,
            Expression = expression
        };
    }

    /// <summary>
    /// Create a test case builder
    /// </summary>
    public static TestCaseBuilder CreateTest(string name) => new(name);
}

/// <summary>
/// A single test case for a JSON Logic expression
/// </summary>
public class TestCase
{
    /// <summary>
    /// Test case name/description
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Input data for the expression
    /// </summary>
    public Dictionary<string, object> InputData { get; set; } = new();

    /// <summary>
    /// Expected output from the expression
    /// </summary>
    public object ExpectedOutput { get; set; } = null!;

    /// <summary>
    /// Optional description of what this test validates
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Result of running a single test case
/// </summary>
public class TestResult
{
    /// <summary>
    /// Test name
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Whether the test passed
    /// </summary>
    public bool Passed { get; set; }

    /// <summary>
    /// Actual output from the expression
    /// </summary>
    public object? ActualOutput { get; set; }

    /// <summary>
    /// Expected output
    /// </summary>
    public object? ExpectedOutput { get; set; }

    /// <summary>
    /// Input data used
    /// </summary>
    public Dictionary<string, object> InputData { get; set; } = new();

    /// <summary>
    /// Test execution duration
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Error message if test failed
    /// </summary>
    public string? Error { get; set; }

    public override string ToString()
    {
        var status = Passed ? "✓ PASS" : "✗ FAIL";
        var message = Error != null ? $" ({Error})" : "";

        return $"{status}: {Name} ({Duration.TotalMilliseconds:F2}ms){message}";
    }
}

/// <summary>
/// Report of multiple test runs
/// </summary>
public class TestReport
{
    /// <summary>
    /// Total number of tests
    /// </summary>
    public int TotalTests { get; set; }

    /// <summary>
    /// Number of passed tests
    /// </summary>
    public int PassedTests { get; set; }

    /// <summary>
    /// Number of failed tests
    /// </summary>
    public int FailedTests { get; set; }

    /// <summary>
    /// Individual test results
    /// </summary>
    public List<TestResult> Results { get; set; } = new();

    /// <summary>
    /// Total duration of all tests
    /// </summary>
    public TimeSpan TotalDuration { get; set; }

    /// <summary>
    /// The expression that was tested
    /// </summary>
    public JsonNode? Expression { get; set; }

    /// <summary>
    /// Pass rate (0.0 to 1.0)
    /// </summary>
    public double PassRate => TotalTests > 0 ? (double)PassedTests / TotalTests : 0;

    public override string ToString()
    {
        var percentText = $"{PassRate * 100:0}%";
        return $"Tests: {PassedTests}/{TotalTests} passed ({percentText}) in {TotalDuration.TotalMilliseconds:F2}ms";
    }
}

/// <summary>
/// Fluent builder for test cases
/// </summary>
public class TestCaseBuilder
{
    private readonly TestCase _testCase;

    public TestCaseBuilder(string name)
    {
        _testCase = new TestCase { Name = name };
    }

    public TestCaseBuilder WithDescription(string description)
    {
        _testCase.Description = description;
        return this;
    }

    public TestCaseBuilder WithInput(string key, object value)
    {
        _testCase.InputData[key] = value;
        return this;
    }

    public TestCaseBuilder WithInputData(Dictionary<string, object> data)
    {
        _testCase.InputData = data;
        return this;
    }

    public TestCaseBuilder ExpectOutput(object expectedOutput)
    {
        _testCase.ExpectedOutput = expectedOutput;
        return this;
    }

    public TestCase Build() => _testCase;
}
