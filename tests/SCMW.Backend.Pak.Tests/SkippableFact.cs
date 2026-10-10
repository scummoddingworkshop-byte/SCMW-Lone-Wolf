// tests/SCMW.Backend.Pak.Tests/SkippableFact.cs
// A local [SkippableFact] so the env-gated real-install tests report SKIPPED (not
// PASS and not FAIL) when the required environment variables are absent. xUnit
// 2.5.3 has no built-in dynamic skip (the xunit.assert SkipException shim only
// works in v3), and adding the community Xunit.SkippableFact package is disallowed
// by the "no package changes" constraint. So this is the minimal Xunit.Sdk
// reimplementation: a custom Fact discoverer produces a test case whose
// GetSkipReason override surfaces an env-var check, which flows through the
// framework's own static-skip path (a test case with a non-null SkipReason is
// reported as skipped).
using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SCMW.Testing;

[XunitTestCaseDiscoverer("SCMW.Testing.SkippableFactDiscoverer", "SCMW.Backend.Pak.Tests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkippableFactAttribute : FactAttribute
{
    /// <summary>Semicolon-separated environment variable names; the test skips if any is unset.</summary>
    public string EnvVars { get; set; } = string.Empty;

    /// <summary>Reason reported when one of <see cref="EnvVars"/> is unset.</summary>
    public string EnvSkipReason { get; set; } = "Required environment variables are not set.";
}

public class SkippableFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    public SkippableFactDiscoverer(IMessageSink diagnosticMessageSink)
        => _diagnosticMessageSink = diagnosticMessageSink;

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        yield return new SkippableFactTestCase(
            _diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod);
    }
}

public class SkippableFactTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public SkippableFactTestCase() { }

    public SkippableFactTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions,
               testMethod, testMethodArguments)
    { }

    protected override string GetSkipReason(IAttributeInfo factAttribute)
    {
        var skipReason = base.GetSkipReason(factAttribute);
        if (!string.IsNullOrEmpty(skipReason))
            return skipReason;

        if (factAttribute is null)
            return null!;

        var envVars = factAttribute.GetNamedArgument<string>("EnvVars");
        if (string.IsNullOrWhiteSpace(envVars))
            return null!;

        foreach (var raw in envVars.Split(';'))
        {
            var name = raw.Trim();
            if (name.Length == 0)
                continue;

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                return factAttribute.GetNamedArgument<string>("EnvSkipReason")
                       ?? "Required environment variables are not set.";
        }

        return null!;
    }
}
