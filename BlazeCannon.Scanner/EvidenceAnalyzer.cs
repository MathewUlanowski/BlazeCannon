using System.Text.RegularExpressions;
using BlazeCannon.Core.Models;
using Microsoft.Extensions.Logging;

namespace BlazeCannon.Scanner;

public class EvidenceAnalyzer
{
    private readonly ILogger<EvidenceAnalyzer> _logger;

    private static readonly string[] SqlErrorPatterns = {
        @"(?i)sqlite.*error", @"(?i)sql.*syntax", @"(?i)unclosed.*quotation",
        @"(?i)microsoft.*ole.*db", @"(?i)odbc.*driver", @"(?i)mysql.*error",
        @"(?i)pg.*error", @"(?i)ora-\d{5}", @"(?i)sql.*server.*error"
    };

    private static readonly string[] CommandOutputPatterns = {
        @"root:.*:0:0", @"uid=\d+", @"(?i)nt authority",
        @"(?i)volume.*serial", @"\[fonts\]", @"(?i)www-data"
    };

    public EvidenceAnalyzer(ILogger<EvidenceAnalyzer> logger)
    {
        _logger = logger;
    }

    public ScanResult? AnalyzeForXss(string responsePayload, PayloadDefinition payload, string pageUrl, string fieldId)
    {
        // Check if the payload appears unencoded in the response
        if (responsePayload.Contains(payload.Payload, StringComparison.Ordinal) ||
            (!string.IsNullOrEmpty(payload.ExpectedEvidence) && Regex.IsMatch(responsePayload, payload.ExpectedEvidence)))
        {
            return new ScanResult
            {
                VulnerabilityType = "XSS",
                Severity = ScanSeverity.High,
                Description = $"Reflected/Stored XSS via {payload.Name}",
                Payload = payload.Payload,
                Evidence = ExtractEvidence(responsePayload, payload.Payload),
                PageUrl = pageUrl,
                FieldIdentifier = fieldId
            };
        }
        return null;
    }

    public ScanResult? AnalyzeForSqli(string responsePayload, string baselinePayload, PayloadDefinition payload, string pageUrl, string fieldId)
    {
        // Check for SQL error messages
        foreach (var pattern in SqlErrorPatterns)
        {
            if (Regex.IsMatch(responsePayload, pattern))
            {
                return new ScanResult
                {
                    VulnerabilityType = "SQLi",
                    Severity = ScanSeverity.Critical,
                    Description = $"SQL Injection error disclosed via {payload.Name}",
                    Payload = payload.Payload,
                    Evidence = ExtractEvidenceByPattern(responsePayload, pattern),
                    PageUrl = pageUrl,
                    FieldIdentifier = fieldId
                };
            }
        }

        // Check for boolean-based differences
        if (!string.IsNullOrEmpty(baselinePayload) && responsePayload != baselinePayload)
        {
            if (!string.IsNullOrEmpty(payload.ExpectedEvidence) && Regex.IsMatch(responsePayload, payload.ExpectedEvidence))
            {
                return new ScanResult
                {
                    VulnerabilityType = "SQLi",
                    Severity = ScanSeverity.Critical,
                    Description = $"Possible SQL Injection via {payload.Name} (response differs from baseline)",
                    Payload = payload.Payload,
                    Evidence = "Response content differs from baseline -- possible boolean-based SQLi",
                    PageUrl = pageUrl,
                    FieldIdentifier = fieldId
                };
            }
        }

        return null;
    }

    public ScanResult? AnalyzeForCommandInjection(string responsePayload, PayloadDefinition payload, string pageUrl, string fieldId)
    {
        foreach (var pattern in CommandOutputPatterns)
        {
            if (Regex.IsMatch(responsePayload, pattern))
            {
                return new ScanResult
                {
                    VulnerabilityType = "CommandInjection",
                    Severity = ScanSeverity.Critical,
                    Description = $"Command injection via {payload.Name}",
                    Payload = payload.Payload,
                    Evidence = ExtractEvidenceByPattern(responsePayload, pattern),
                    PageUrl = pageUrl,
                    FieldIdentifier = fieldId
                };
            }
        }
        return null;
    }

    public ScanResult? AnalyzeForPathTraversal(string responsePayload, PayloadDefinition payload, string pageUrl, string fieldId)
    {
        foreach (var pattern in CommandOutputPatterns) // Reuses file content markers
        {
            if (Regex.IsMatch(responsePayload, pattern))
            {
                return new ScanResult
                {
                    VulnerabilityType = "PathTraversal",
                    Severity = ScanSeverity.High,
                    Description = $"Path traversal via {payload.Name}",
                    Payload = payload.Payload,
                    Evidence = ExtractEvidenceByPattern(responsePayload, pattern),
                    PageUrl = pageUrl,
                    FieldIdentifier = fieldId
                };
            }
        }
        return null;
    }

    private static string ExtractEvidence(string response, string searchTerm)
    {
        var idx = response.IndexOf(searchTerm, StringComparison.Ordinal);
        if (idx < 0) return searchTerm;
        var start = Math.Max(0, idx - 50);
        var end = Math.Min(response.Length, idx + searchTerm.Length + 50);
        return "..." + response[start..end] + "...";
    }

    private static string ExtractEvidenceByPattern(string response, string pattern)
    {
        var match = Regex.Match(response, pattern);
        if (!match.Success) return pattern;
        var idx = match.Index;
        var start = Math.Max(0, idx - 30);
        var end = Math.Min(response.Length, idx + match.Length + 30);
        return "..." + response[start..end] + "...";
    }
}
