using System.Globalization;
using System.Xml.Linq;
using static TrxResultProcessor;

internal static partial class TestLauncher
{
    private const string CompleteTrx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <ResultSummary outcome="Completed">
            <Counters total="1" executed="1" passed="1" failed="0" />
          </ResultSummary>
          <Diagnostics>cOMPLETED</Diagnostics>
        </TestRun>
        """;

    // Paths and field formats taken from the offline VSTest/xUnit TRX.
    private const string SdkTrx = """
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"
                 id="A1020000-0000-0000-0000-000000000000" name="Passed 1" runUser="Passed">
          <Times creation="2026-11-08T08:00:00.0000000+08:00" queuing="2026-11-08T08:00:00.0000000+08:00"
                 start="2026-11-08T08:00:00.0000000+08:00" finish="2026-11-08T08:00:01.0000000+08:00" />
          <TestSettings id="B1020000-0000-0000-0000-000000000000" />
          <Results>
            <UnitTestResult executionId="C1020000-0000-0000-0000-000000000000"
                            testId="D1020000-0000-0000-0000-000000000000"
                            testListId="E1020000-0000-0000-0000-000000000000"
                            testType="F1020000-0000-0000-0000-000000000000"
                            computerName="Passed" testName="Passed 1" outcome="Passed"
                            startTime="2026-11-08T08:00:00.0000000+08:00"
                            endTime="2026-11-08T08:00:00.0000001+08:00" duration="00:00:00.0000001">
              <Output><StdOut>Passed 1</StdOut></Output>
            </UnitTestResult>
          </Results>
          <TestDefinitions>
            <UnitTest id="d1020000-0000-0000-0000-000000000000" name="Passed">
              <Execution id="c1020000-0000-0000-0000-000000000000" />
            </UnitTest>
          </TestDefinitions>
          <TestEntries>
            <TestEntry executionId="C1020000-0000-0000-0000-000000000000"
                       testId="D1020000-0000-0000-0000-000000000000"
                       testListId="E1020000-0000-0000-0000-000000000000" />
          </TestEntries>
          <TestLists><TestList id="e1020000-0000-0000-0000-000000000000" /></TestLists>
          <ResultSummary outcome="Completed">
            <Counters total="1" executed="1" passed="1" failed="0" completed="0" />
          </ResultSummary>
        </TestRun>
        """;

    private static void RunTrxResultSelfTests()
    {
        (string Id, Action<string> Check)[] cases =
        [
            ("summary-outcome-collision", CheckSummaryOutcomeCollision),
            ("sdk-fields-and-collisions", CheckSdkFields),
            ("failed-outcome", path => CheckVerdict(path, "Failed", expected: new(1, 1, 1, 0, 0, "Failed"))),
            ("aborted-outcome", path => CheckVerdict(path, "Aborted", expected: new(1, 1, 1, 0, 0, "Aborted"))),
            ("unknown-outcome", path => CheckVerdict(path, "unrecognized-state")),
            ("missing-outcome", path => CheckVerdict(path, null)),
            ("failed-count-collision", path => CheckVerdict(path, "Completed",
                "failed", "1", expected: new(1, 1, 1, 1, 0, "Completed"))),
            ("skip-count-collision", path => CheckVerdict(path, "Completed",
                "notExecuted", "1", expected: new(1, 1, 1, 0, 1, "Completed"))),
            ("missing-core-count", path => CheckVerdict(path, "Completed", "failed")),
            ("negative-count", path => CheckVerdict(path, "Completed", "failed", "-1")),
            ("invalid-optional-count", path => CheckVerdict(path, "Completed", "error", "invalid")),
            ("overflow-count", path => CheckVerdict(path, "Completed", "timeout", "2147483648")),
            ("failed-sum-overflow", path => CheckCounterOverflow(path, "failed", "error")),
            ("skip-sum-overflow", path => CheckCounterOverflow(path, "notExecuted", "inconclusive")),
            ("zero-tests", path => CheckCounters(path, 0, 0, 0, 0)),
            ("not-all-executed", path => CheckCounters(path, 2, 1, 1, 0)),
            ("not-all-passed", path => CheckCounters(path, 2, 2, 1, 0)),
            ("duplicate-summary", path => CheckDuplicate(path, "ResultSummary")),
            ("duplicate-counters", path => CheckDuplicate(path, "Counters")),
            ("invalid-result-outcome", path => CheckInvalidField(path, "UnitTestResult", "outcome")),
            ("invalid-guid", path => CheckInvalidField(path, "UnitTestResult", "testId")),
            ("invalid-timestamp", path => CheckInvalidField(path, "Times", "start")),
            ("invalid-duration", path => CheckInvalidField(path, "UnitTestResult", "duration")),
            ("entities-and-diagnostic-fields", CheckEntityRedaction),
            ("unsafe-extension-namespace", CheckExtensionNamespace),
            ("mixed-diagnostic-text", CheckMixedDiagnosticText),
            ("xml-space-collision", CheckXmlSpace),
            ("malformed-entity", CheckMalformedEntity),
            ("dtd-prohibited", CheckDtd),
            ("missing-trx", CheckMissingTrx),
            ("redaction-failed", CheckRedactionFailure),
            ("temporary-write-failed", path => CheckStorageFailure(path, new(Write: FailWrite))),
            ("atomic-replace-failed", path => CheckStorageFailure(path, new(Replace: FailReplace))),
            ("diagnostic-replace-failed", path => CheckStorageFailure(path, new(Replace: FailReplace), malformed: true)),
            ("unsafe-original-remains", path => CheckUnsafeResidue(path, leaveTemp: false)),
            ("unsafe-original-and-temp-remain", path => CheckUnsafeResidue(path, leaveTemp: true))
        ];
        var failed = false;
        foreach (var (id, check) in cases)
        {
            try
            {
                var directory = Directory.CreateTempSubdirectory("dameng-trx-verdict-").FullName;
                try { check(Path.Combine(directory, "sample.trx")); }
                finally { Directory.Delete(directory, recursive: true); }
            }
            catch (TrxAssertionException)
            {
                Console.Error.WriteLine(id + ": TRX assertion failed.");
                failed = true;
            }
            catch
            {
                Console.Error.WriteLine(id + ": TRX fixture or cleanup failed.");
                failed = true;
            }
        }
        if (failed) throw new InvalidOperationException("TRX verdict and redaction self-test failed.");
        Console.WriteLine("TRX verdict and redaction self-test passed.");
    }

    private static void CheckSummaryOutcomeCollision(string path)
    {
        File.WriteAllText(path, CompleteTrx);
        var result = ReadAndRedactTrx(path, CreateRedactor("User ID=cOMPLETED;Password=1"));
        RequireTrx(result.Status == TrxArtifactStatus.Saved && result.Counts == new TrxCounts(1, 1, 1, 0, 0, "Completed"));
        RequireTrx(IsAccepted(0, result.Counts!) && !IsAccepted(1, result.Counts!));
        var saved = XDocument.Load(path);
        var summary = saved.Descendants(TrxNamespace + "ResultSummary").Single();
        var counters = summary.Element(TrxNamespace + "Counters")!;
        RequireTrx((string?)summary.Attribute("outcome") == "Completed"
            && (int?)counters.Attribute("total") == result.Counts!.Total
            && (int?)counters.Attribute("executed") == result.Counts.Executed
            && (int?)counters.Attribute("passed") == result.Counts.Passed
            && (int?)counters.Attribute("failed") == result.Counts.Failed
            && saved.Descendants(TrxNamespace + "Diagnostics").Single().Value == "[redacted]");
        if (!OperatingSystem.IsWindows())
        {
            var ownerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            RequireTrx(File.GetUnixFileMode(path) == ownerFile
                && File.GetUnixFileMode(Path.GetDirectoryName(path)!) == (ownerFile | UnixFileMode.UserExecute));
        }
    }

    private static void CheckSdkFields(string path)
    {
        File.WriteAllText(path, SdkTrx);
        var result = ReadAndRedactTrx(path, CreateRedactor("User ID=Passed;Password=1"));
        RequireTrx(result.Counts is not null && IsAccepted(0, result.Counts));

        var run = XDocument.Load(path).Root!;
        var ns = TrxNamespace;
        var row = run.Element(ns + "Results")!.Elements(ns + "UnitTestResult").Single();
        var test = run.Element(ns + "TestDefinitions")!.Elements(ns + "UnitTest").Single();
        var entry = run.Element(ns + "TestEntries")!.Elements(ns + "TestEntry").Single();
        var list = run.Element(ns + "TestLists")!.Elements(ns + "TestList").Single();
        var times = run.Element(ns + "Times")!;

        RequireTrx((string?)row.Attribute("outcome") == "Passed"
            && (string?)row.Attribute("testName") == "[redacted] [redacted]"
            && (string?)row.Attribute("computerName") == "[redacted]"
            && (string?)run.Attribute("name") == "[redacted] [redacted]"
            && (string?)run.Attribute("runUser") == "[redacted]"
            && (string?)test.Attribute("name") == "[redacted]"
            && row.Descendants(ns + "StdOut").Single().Value == "[redacted] [redacted]");

        var testId = (string?)row.Attribute("testId");
        var executionId = (string?)row.Attribute("executionId");
        var listId = (string?)row.Attribute("testListId");
        RequireTrx(testId == "d1020000-0000-0000-0000-000000000000"
            && testId == (string?)test.Attribute("id") && testId == (string?)entry.Attribute("testId")
            && executionId == "c1020000-0000-0000-0000-000000000000"
            && executionId == (string?)test.Element(ns + "Execution")?.Attribute("id")
            && executionId == (string?)entry.Attribute("executionId")
            && listId == "e1020000-0000-0000-0000-000000000000"
            && listId == (string?)list.Attribute("id") && listId == (string?)entry.Attribute("testListId")
            && (string?)row.Attribute("testType") == "f1020000-0000-0000-0000-000000000000"
            && (string?)run.Attribute("id") == "a1020000-0000-0000-0000-000000000000"
            && (string?)run.Element(ns + "TestSettings")?.Attribute("id") == "b1020000-0000-0000-0000-000000000000");

        var start = new DateTimeOffset(2026, 11, 8, 8, 0, 0, TimeSpan.FromHours(8));
        RequireTrx(ReadTimestamp(times, "creation").EqualsExact(start)
            && ReadTimestamp(times, "queuing").EqualsExact(start)
            && ReadTimestamp(times, "start").EqualsExact(start)
            && ReadTimestamp(times, "finish").EqualsExact(start.AddSeconds(1))
            && ReadTimestamp(row, "startTime").EqualsExact(start)
            && ReadTimestamp(row, "endTime").EqualsExact(start.AddTicks(1))
            && TimeSpan.ParseExact((string)row.Attribute("duration")!, "c", CultureInfo.InvariantCulture).Ticks == 1);
    }

    private static DateTimeOffset ReadTimestamp(XElement element, string name)
        => DateTimeOffset.ParseExact((string)element.Attribute(name)!, "O", CultureInfo.InvariantCulture);

    private static void CheckVerdict(string path, string? outcome, string? counter = null,
        string? value = null, TrxCounts? expected = null)
    {
        var document = XDocument.Parse(CompleteTrx);
        var summary = document.Descendants(TrxNamespace + "ResultSummary").Single();
        summary.SetAttributeValue("outcome", outcome);
        if (counter is not null) summary.Element(TrxNamespace + "Counters")!.SetAttributeValue(counter, value);
        document.Save(path);
        var result = ReadAndRedactTrx(path, CreateRedactor("Password=1"));
        if (expected is null) RequireFailureDocument(path, result);
        else RequireTrx(result.Status == TrxArtifactStatus.Saved && result.Counts == expected && !IsAccepted(0, result.Counts!));
    }

    private static void CheckCounters(string path, int total, int executed, int passed, int failed)
    {
        var document = XDocument.Parse(CompleteTrx);
        var counters = document.Descendants(TrxNamespace + "Counters").Single();
        counters.SetAttributeValue("total", total);
        counters.SetAttributeValue("executed", executed);
        counters.SetAttributeValue("passed", passed);
        counters.SetAttributeValue("failed", failed);
        document.Save(path);
        var result = ReadAndRedactTrx(path, CreateRedactor("Password=1"));
        RequireTrx(result.Counts == new TrxCounts(total, executed, passed, failed, total - executed, "Completed")
            && !IsAccepted(0, result.Counts!));
    }

    private static void CheckCounterOverflow(string path, string first, string second)
    {
        var document = XDocument.Parse(CompleteTrx);
        var counters = document.Descendants(TrxNamespace + "Counters").Single();
        counters.SetAttributeValue(first, int.MaxValue);
        counters.SetAttributeValue(second, 1);
        document.Save(path);
        RequireFailureDocument(path, ReadAndRedactTrx(path, text => text));
    }

    private static void CheckDuplicate(string path, string name)
    {
        var document = XDocument.Parse(CompleteTrx);
        var element = document.Descendants(TrxNamespace + name).Single();
        element.AddAfterSelf(new XElement(element));
        document.Save(path);
        RequireFailureDocument(path, ReadAndRedactTrx(path, text => text));
    }

    private static void CheckInvalidField(string path, string element, string attribute)
    {
        var document = XDocument.Parse(SdkTrx);
        document.Descendants(TrxNamespace + element).Single().SetAttributeValue(attribute, "unrecognized-field");
        document.Save(path);
        RequireFailureDocument(path, ReadAndRedactTrx(path, text => text));
    }

    private static void CheckEntityRedaction(string path)
    {
        var document = XDocument.Parse(CompleteTrx);
        var diagnostic = XElement.Parse("""
            <Diagnostics id="demo&#x26;fragment" outcome="demo&amp;fragment" total="demo&amp;fragment">demo&amp;<!--demo&amp;fragment--><![CDATA[frag]]><?note demo&amp;fragment?>ment</Diagnostics>
            """);
        document.Root!.Element(TrxNamespace + "Diagnostics")!.ReplaceWith(diagnostic);
        document.AddFirst(new XProcessingInstruction("note", "demo&amp;fragment"));
        document.Save(path);
        var result = ReadAndRedactTrx(path, CreateRedactor("Password=demo&fragment"));
        var saved = XDocument.Load(path);
        var element = saved.Descendants("Diagnostics").Single();
        RequireTrx(result.Counts is not null && IsAccepted(0, result.Counts)
            && element.Value == "[redacted]" && element.Attributes().Where(a => !a.IsNamespaceDeclaration)
                .All(a => a.Value == "[redacted]")
            && !saved.DescendantNodes().Any(n => n is XComment or XProcessingInstruction));
    }

    private static void CheckMalformedEntity(string path)
    {
        File.WriteAllText(path, "<TestRun><Diagnostics>demo&amp;fragment");
        RequireFailureDocument(path, ReadAndRedactTrx(path, CreateRedactor("Password=demo&fragment")));
    }

    private static void CheckExtensionNamespace(string path)
    {
        var document = XDocument.Parse(CompleteTrx);
        document.Root!.SetAttributeValue(XNamespace.Xmlns + "aux", "urn:demo&fragment");
        document.Save(path);
        RequireFailureDocument(path, ReadAndRedactTrx(path, CreateRedactor("Password=demo&fragment")));
    }

    private static void CheckMixedDiagnosticText(string path)
    {
        var document = XDocument.Parse(CompleteTrx);
        document.Root!.Add(XElement.Parse("<Mixed>demo&amp;<Part>frag</Part>ment</Mixed>"));
        document.Save(path);
        RequireFailureDocument(path, ReadAndRedactTrx(path, CreateRedactor("Password=demo&fragment")));
    }

    private static void CheckXmlSpace(string path)
    {
        var document = XDocument.Parse(CompleteTrx);
        var diagnostic = document.Root!.Element(TrxNamespace + "Diagnostics")!;
        diagnostic.SetAttributeValue(XNamespace.Xml + "space", "preserve");
        diagnostic.Value = "preserve";
        document.Save(path);
        var result = ReadAndRedactTrx(path, CreateRedactor("Password=preserve"));
        var saved = XDocument.Load(path).Descendants(TrxNamespace + "Diagnostics").Single();
        RequireTrx(result.Counts is not null && IsAccepted(0, result.Counts)
            && (string?)saved.Attribute(XNamespace.Xml + "space") == "preserve" && saved.Value == "[redacted]");
    }

    private static void CheckDtd(string path)
    {
        File.WriteAllText(path, "<!DOCTYPE TestRun [<!ENTITY d 'demo&amp;fragment'>]>" + CompleteTrx);
        RequireFailureDocument(path, ReadAndRedactTrx(path, CreateRedactor("Password=demo&fragment")));
    }

    private static void CheckMissingTrx(string path)
    {
        var result = ReadAndRedactTrx(path, text => text);
        RequireTrx(result.Counts is null && result.Status == TrxArtifactStatus.Missing
            && !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Any());
    }

    private static void CheckRedactionFailure(string path)
    {
        File.WriteAllText(path, CompleteTrx);
        RequireFailureDocument(path, ReadAndRedactTrx(path, _ => throw new InvalidOperationException()));
    }

    private static void CheckStorageFailure(string path, TrxIo io, bool malformed = false)
    {
        File.WriteAllText(path, malformed ? "<TestRun>" : CompleteTrx);
        var result = ReadAndRedactTrx(path, text => text, io);
        RequireTrx(result.Counts is null && result.Status == TrxArtifactStatus.StorageFailure
            && !Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(path)!).Any());
    }

    private static void CheckUnsafeResidue(string path, bool leaveTemp)
    {
        File.WriteAllText(path, CompleteTrx);
        var result = ReadAndRedactTrx(path, text => text, new(Replace: FailReplace, Delete: file =>
        {
            if (file == path || leaveTemp) throw new IOException();
            File.Delete(file);
        }));
        RequireTrx(result.Counts is null && result.Status == TrxArtifactStatus.UnsafeResidue
            && result.OriginalRemains && File.Exists(path)
            && (result.TemporaryRemains is not null) == leaveTemp
            && Directory.EnumerateFiles(Path.GetDirectoryName(path)!).Count() == (leaveTemp ? 2 : 1));
        var expected = "Unsafe TRX artifact could not be removed; this lane is not accepted. Original: " + path;
        if (leaveTemp)
        {
            RequireTrx(File.Exists(result.TemporaryRemains));
            expected += " Temporary: " + result.TemporaryRemains;
        }
        RequireTrx(result.DescribeFailure(path) == expected);
    }

    private static void FailWrite(XDocument document, Stream stream)
    {
        stream.WriteByte(0);
        throw new IOException();
    }

    private static void FailReplace(string source, string destination) => throw new IOException();

    private static void RequireFailureDocument(string path, TrxResult result)
    {
        RequireTrx(result.Counts is null && result.Status == TrxArtifactStatus.DiagnosticSaved);
        var saved = XDocument.Load(path);
        RequireTrx(saved.Nodes().Count() == 1 && XNode.DeepEquals(saved.Root, new XElement("TrxProcessingFailure")));
    }

    private static void RequireTrx(bool condition)
    {
        if (!condition) throw new TrxAssertionException();
    }

    private sealed class TrxAssertionException : Exception;
}
