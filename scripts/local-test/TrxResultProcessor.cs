using System.Globalization;
using System.Xml;
using System.Xml.Linq;

internal static class TrxResultProcessor
{
    internal static readonly XNamespace TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly string[] RequiredCounters = ["total", "executed", "passed", "failed"];
    private static readonly string[] OptionalCounters =
    [
        "error", "timeout", "aborted", "notExecuted", "inconclusive", "notRunnable",
        "completed", "disconnected", "inProgress", "passedButRunAborted", "pending", "warning"
    ];

    // Standard VSTest XML names are public structure. Extension names may contain free text.
    private static readonly HashSet<string> StandardElements =
    [
        "TestRun", "Times", "TestSettings", "Deployment", "Results", "UnitTestResult", "InnerResults",
        "TestDefinitions", "UnitTest", "Execution", "TestMethod", "TestEntries", "TestEntry", "TestLists",
        "TestList", "ResultSummary", "Counters", "Output", "StdOut", "StdErr", "DebugTrace", "ErrorInfo",
        "Message", "StackTrace", "RunInfos", "RunInfo", "Text", "Exception", "ResultFiles", "ResultFile",
        "CollectorDataEntries", "Collector", "UriAttachments", "UriAttachment", "A", "Owners", "Owner",
        "TestCategory", "TestCategoryItem", "Workitems", "Workitem"
    ];

    // Typed attributes are identified separately by their paths.
    private static readonly Dictionary<string, string[]> StandardAttributes = new(StringComparer.Ordinal)
    {
        ["TestRun"] = ["name", "runUser"],
        ["TestSettings"] = ["name"],
        ["Deployment"] = ["runDeploymentRoot"],
        ["UnitTestResult"] = ["computerName", "testName", "relativeResultsDirectory"],
        ["UnitTest"] = ["name", "storage", "priority"],
        ["TestMethod"] = ["adapterTypeName", "className", "codeBase", "name"],
        ["TestList"] = ["name", "parentListId"],
        ["RunInfo"] = ["computerName", "outcome", "timestamp"],
        ["ResultFile"] = ["path"],
        ["Collector"] = ["agentName", "collectorDisplayName", "uri"],
        ["UriAttachment"] = ["href"],
        ["Owner"] = ["name"],
        ["TestCategoryItem"] = ["TestCategory"]
    };

    internal sealed record TrxCounts(int Total, int Executed, int Passed, int Failed, int Skipped,
        string Outcome);

    internal enum TrxArtifactStatus { Saved, Missing, DiagnosticSaved, StorageFailure, UnsafeResidue }

    internal sealed record TrxResult(TrxCounts? Counts, TrxArtifactStatus Status,
        bool OriginalRemains = false, string? TemporaryRemains = null)
    {
        internal string DescribeFailure(string path) => Status switch
        {
            TrxArtifactStatus.Missing => "TRX missing; this lane is not accepted.",
            TrxArtifactStatus.DiagnosticSaved => "TRX processing failed; this lane is not accepted. Diagnostic: " + path,
            TrxArtifactStatus.StorageFailure => "TRX storage failed; this lane is not accepted.",
            TrxArtifactStatus.UnsafeResidue => "Unsafe TRX artifact could not be removed; this lane is not accepted."
                + (OriginalRemains ? " Original: " + path : "")
                + (TemporaryRemains is not null ? " Temporary: " + TemporaryRemains : ""),
            _ => throw new InvalidOperationException("TRX failure status expected.")
        };
    }

    // Per-call operations allow self-tests to exercise the real cleanup path.
    internal sealed record TrxIo(Action<XDocument, Stream>? Write = null,
        Action<string, string>? Replace = null, Action<string>? Delete = null);

    internal static TrxResult ReadAndRedactTrx(string path, Func<string, string> redact, TrxIo? io = null)
    {
        XDocument document;
        TrxCounts? counts;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true
            });
            document = XDocument.Load(reader);
        }
        catch (FileNotFoundException)
        {
            return new(null, TrxArtifactStatus.Missing);
        }
        catch (DirectoryNotFoundException)
        {
            return new(null, TrxArtifactStatus.Missing);
        }
        catch
        {
            return SaveTrx(path, FailureDocument(), null, io);
        }

        try
        {
            var typed = new HashSet<XAttribute>();
            counts = ReadCounts(document, typed);
            NormalizeStructuredFields(document.Root!, typed);
            RedactDiagnostics(document, typed, redact);
        }
        catch
        {
            document = FailureDocument();
            counts = null;
        }

        return SaveTrx(path, document, counts, io);
    }

    private static XDocument FailureDocument() => new(new XElement("TrxProcessingFailure"));

    private static TrxCounts ReadCounts(XDocument document, HashSet<XAttribute> typed)
    {
        var run = document.Root;
        var summary = document.Descendants().Single(element => element.Name.LocalName == "ResultSummary");
        var counters = document.Descendants().Single(element => element.Name.LocalName == "Counters");
        if (run?.Name != TrxNamespace + "TestRun"
            || summary.Parent != run || summary.Name != TrxNamespace + "ResultSummary"
            || counters.Parent != summary || counters.Name != TrxNamespace + "Counters")
        {
            throw new FormatException();
        }

        var outcome = summary.Attribute("outcome") ?? throw new FormatException();
        outcome.Value = ParseOutcome(outcome.Value);
        typed.Add(outcome);

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in RequiredCounters)
            values.Add(name, ReadCounter(counters.Attribute(name) ?? throw new FormatException(), typed));
        foreach (var name in OptionalCounters)
            values.Add(name, counters.Attribute(name) is { } field ? ReadCounter(field, typed) : 0);

        var failed = checked(values["failed"] + values["error"] + values["timeout"] + values["aborted"]);
        var skipped = checked(values["notExecuted"] + values["inconclusive"] + values["notRunnable"]);
        skipped = Math.Max(skipped, values["total"] - values["executed"]);
        return new(values["total"], values["executed"], values["passed"], failed, skipped, outcome.Value);
    }

    private static int ReadCounter(XAttribute field, HashSet<XAttribute> typed)
    {
        if (!int.TryParse(field.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            throw new FormatException();
        field.Value = value.ToString(CultureInfo.InvariantCulture);
        typed.Add(field);
        return value;
    }

    // VSTest uses the same TestOutcome vocabulary for the run and the individual results.
    private static string ParseOutcome(string value) => value switch
    {
        "Error" or "Failed" or "Timeout" or "Aborted" or "Inconclusive" or "PassedButRunAborted"
            or "NotRunnable" or "NotExecuted" or "Disconnected" or "Warning" or "Passed"
            or "Completed" or "InProgress" or "Pending" => value,
        _ => throw new FormatException()
    };

    private static void NormalizeStructuredFields(XElement run, HashSet<XAttribute> typed)
    {
        // Match the VSTest element paths, not just attribute names.
        var ns = TrxNamespace;
        var results = run.Elements(ns + "Results").Elements(ns + "UnitTestResult");
        var definitions = run.Elements(ns + "TestDefinitions").Elements(ns + "UnitTest");
        Normalize([run], typed, FormatGuid, "id");
        Normalize(run.Elements(ns + "TestSettings"), typed, FormatGuid, "id");
        Normalize(run.Elements(ns + "Times"), typed, FormatTimestamp, "creation", "queuing", "start", "finish");
        Normalize(results, typed, ParseOutcome, "outcome");
        Normalize(results, typed, FormatGuid, "executionId", "parentExecutionId", "testId", "testListId", "testType");
        Normalize(results, typed, FormatTimestamp, "startTime", "endTime");
        Normalize(results, typed, FormatDuration, "duration");
        Normalize(definitions, typed, FormatGuid, "id");
        Normalize(definitions.Elements(ns + "Execution"), typed, FormatGuid, "id", "parentId");
        Normalize(run.Elements(ns + "TestEntries").Elements(ns + "TestEntry"), typed, FormatGuid,
            "executionId", "testId", "testListId");
        Normalize(run.Elements(ns + "TestLists").Elements(ns + "TestList"), typed, FormatGuid, "id");
    }

    private static void Normalize(IEnumerable<XElement> elements, HashSet<XAttribute> typed,
        Func<string, string> format, params string[] names)
    {
        foreach (var element in elements)
        foreach (var name in names)
        {
            if (element.Attribute(name) is not { } field) continue;
            field.Value = format(field.Value);
            typed.Add(field);
        }
    }

    private static string FormatGuid(string value)
        => Guid.ParseExact(value, "D").ToString("D", CultureInfo.InvariantCulture);

    private static string FormatTimestamp(string value)
        => DateTimeOffset.ParseExact(value, "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK",
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).ToString("O", CultureInfo.InvariantCulture);

    private static string FormatDuration(string value)
    {
        var duration = TimeSpan.ParseExact(value, "c", CultureInfo.InvariantCulture);
        if (duration < TimeSpan.Zero) throw new FormatException();
        return duration.ToString("c", CultureInfo.InvariantCulture);
    }

    private static void RedactDiagnostics(XDocument document, HashSet<XAttribute> typed, Func<string, string> redact)
    {
        foreach (var element in document.Descendants())
        {
            var standardElement = element.Name.Namespace == TrxNamespace
                && StandardElements.Contains(element.Name.LocalName);
            CheckXmlName(element.Name, standardElement, redact);
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    CheckXmlNamespace(attribute.Value, redact);
                    if (attribute.Name.Namespace == XNamespace.Xmlns && attribute.Name.LocalName != "xml"
                        && redact(attribute.Name.LocalName) != attribute.Name.LocalName)
                        throw new FormatException();
                }
                else if (!typed.Contains(attribute))
                {
                    var standardAttribute = (standardElement && attribute.Name.Namespace == XNamespace.None
                            && StandardAttributes.TryGetValue(element.Name.LocalName, out var names)
                            && names.Contains(attribute.Name.LocalName, StringComparer.Ordinal))
                        || (attribute.Name.Namespace == XNamespace.Xml
                            && attribute.Name.LocalName is "space" or "lang" or "base" or "id");
                    CheckXmlName(attribute.Name, standardAttribute, redact);
                    if (attribute.Name == XNamespace.Xml + "space")
                    {
                        if (attribute.Value is not ("default" or "preserve")) throw new FormatException();
                    }
                    else
                    {
                        attribute.Value = redact(attribute.Value);
                    }
                }
            }

            // VSTest diagnostic bodies are text. Mixed extensions can split a token across tags.
            if (element.HasElements && element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
            {
                var content = element.Value;
                if (redact(content) != content) throw new FormatException();
            }

            // Merge contiguous text, including CDATA, before matching a token.
            for (var node = element.FirstNode; node is not null; node = node.NextNode)
            {
                if (node is not XText text) continue;
                while (text.NextNode is XText next)
                {
                    text.Value += next.Value;
                    next.Remove();
                }
                text.Value = redact(text.Value);
            }
        }
    }

    private static void CheckXmlName(XName name, bool standard, Func<string, string> redact)
    {
        CheckXmlNamespace(name.NamespaceName, redact);
        if (!standard && redact(name.LocalName) != name.LocalName)
            throw new FormatException();
    }

    private static void CheckXmlNamespace(string uri, Func<string, string> redact)
    {
        if (uri.Length > 0 && uri != TrxNamespace.NamespaceName && uri != XNamespace.Xml.NamespaceName
            && redact(uri) != uri)
            throw new FormatException();
    }

    private static TrxResult SaveTrx(string path, XDocument document, TrxCounts? counts, TrxIo? io)
    {
        var tempPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        var ownsTemp = false;
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.WriteThrough
            };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = PrivateFileMode;
            using (var stream = new FileStream(tempPath, options))
            {
                ownsTemp = true;
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(stream.SafeFileHandle, PrivateFileMode);
                if (io?.Write is { } write) write(document, stream);
                else document.Save(stream);
                stream.Flush(flushToDisk: true);
            }
            if (io?.Replace is { } replace) replace(tempPath, path);
            else File.Move(tempPath, path, overwrite: true);
            return new(counts, counts is null ? TrxArtifactStatus.DiagnosticSaved : TrxArtifactStatus.Saved);
        }
        catch
        {
            var delete = io?.Delete ?? File.Delete;
            var originalRemains = !TryDelete(path, delete);
            var tempRemains = ownsTemp && !TryDelete(tempPath, delete);
            return originalRemains || tempRemains
                ? new(null, TrxArtifactStatus.UnsafeResidue, originalRemains, tempRemains ? tempPath : null)
                : new(null, TrxArtifactStatus.StorageFailure);
        }
    }

    private static bool TryDelete(string path, Action<string> delete)
    {
        try { delete(path); return true; }
        catch { return false; }
    }

    internal static bool IsAccepted(int exitCode, TrxCounts counts)
        => exitCode == 0 && counts.Outcome == "Completed" && counts.Total > 0
            && counts.Total == counts.Executed && counts.Passed == counts.Executed
            && counts.Failed == 0 && counts.Skipped == 0;
}
