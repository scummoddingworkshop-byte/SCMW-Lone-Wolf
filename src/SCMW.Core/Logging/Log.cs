using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace SCMW.Core.Logging;

/// <summary>
/// Static file logger over Serilog. Re-initializable via <see cref="SetLogsDir"/>;
/// before initialization all writes are silent no-ops (never throws).
/// Every message is scrubbed against registered secret values before hitting the file,
/// so AES keys and other secrets never land in the log.
/// Design note: a single persistent Serilog logger is created in <see cref="SetLogsDir"/>
/// (disposing the previous one on re-init) and emits through <see cref="ImmediateFileSink"/>,
/// which appends synchronously with an open/write/close per event and serializes writers with
/// an internal lock. A persistent Serilog file sink holding an open handle cannot be used here:
/// Windows refuses a plain <c>File.ReadAllText</c> (which requests <c>FileShare.Read</c>) while a
/// writer handle with <c>FileAccess.Write</c> exists, regardless of Serilog's <c>shared</c>
/// option or flush interval. Opening per event keeps the log readable the instant a write
/// returns (tests, tailing viewers) while per-event locking guarantees concurrent writes
/// cannot collide and silently drop entries. File naming stays <c>scmw-yyyyMM.log</c>, monthly roll.
/// </summary>
public static class Log
{
    private const string RedactedPlaceholder = "[redacted]";

    private static readonly object Gate = new();
    private static readonly HashSet<string> Secrets = new(StringComparer.Ordinal);
    private static Logger? _logger;
    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    /// <summary>(Re)initializes the file logger pointed at the given logs directory.</summary>
    public static void SetLogsDir(string dir)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (IOException)
            {
                // keep the current setup; nothing to point at
                return;
            }

            var previous = _logger;
            _logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Sink(new ImmediateFileSink(dir, OutputTemplate))
                .CreateLogger();
            previous?.Dispose();
        }
    }

    public static void RegisterSecret(string secret)
    {
        if (!string.IsNullOrEmpty(secret))
            lock (Gate) Secrets.Add(secret);
    }

    /// <summary>
    /// Replaces every occurrence of <paramref name="secret"/> in <paramref name="message"/>
    /// with <see cref="RedactedPlaceholder"/>, case-insensitively, covering both the full
    /// form and the bare form with any leading "0x" stripped (longest first, so a full
    /// "0x…" occurrence is swallowed whole instead of leaving a stray "0x").
    /// </summary>
    public static string Scrub(string message, string secret)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrWhiteSpace(secret))
            return message;

        var forms = new[] { secret, BareHexBody(secret) }
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(f => f.Length);

        var safe = message;
        foreach (var form in forms)
            safe = Regex.Replace(safe, Regex.Escape(form), RedactedPlaceholder, RegexOptions.IgnoreCase);

        return safe;
    }

    private static string BareHexBody(string secret) =>
        secret.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? secret[2..] : secret;

    public static void Info(string message) => Write(LogEventLevel.Information, message);

    public static void Warn(string message) => Write(LogEventLevel.Warning, message);

    public static void Error(string message) => Write(LogEventLevel.Error, message);

    private static void Write(LogEventLevel level, string message)
    {
        Logger? logger;
        lock (Gate) logger = _logger;
        if (logger is null) return; // not initialized yet -> no-throw no-op

        var safe = Redact(message);

        try
        {
            // Emits synchronously; the sink has flushed and closed the file before returning.
            logger.Write(level, "{Message}", safe);
        }
        catch
        {
            // logging must never take the app down
        }
    }

    private static string Redact(string message)
    {
        string[] secrets;
        lock (Gate) secrets = Secrets.ToArray();

        var safe = message;
        foreach (var secret in secrets)
            safe = Scrub(safe, secret);

        return safe;
    }

    /// <summary>
    /// Appends each event to <c>scmw-yyyyMM.log</c> immediately, one short-lived handle per
    /// event, serialized so concurrent writers cannot collide. The handle is closed on return,
    /// so readers may open the log at any time.
    /// </summary>
    private sealed class ImmediateFileSink : ILogEventSink
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly string _dir;
        private readonly MessageTemplateTextFormatter _formatter;
        private readonly object _sync = new();

        public ImmediateFileSink(string dir, string outputTemplate)
        {
            _dir = dir;
            _formatter = new MessageTemplateTextFormatter(outputTemplate, CultureInfo.InvariantCulture);
        }

        public void Emit(LogEvent logEvent)
        {
            string line;
            using (var buffer = new StringWriter(CultureInfo.InvariantCulture))
            {
                _formatter.Format(logEvent, buffer);
                line = buffer.ToString();
            }

            lock (_sync)
            {
                try
                {
                    Directory.CreateDirectory(_dir);
                    var file = Path.Combine(
                        _dir,
                        "scmw-" + logEvent.Timestamp.ToString("yyyyMM", CultureInfo.InvariantCulture) + ".log");

                    // FileShare.ReadWrite so external tailers/readers can open the file freely.
                    using var stream = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, Utf8NoBom);
                    writer.Write(line);
                    writer.Flush();
                }
                catch
                {
                    // logging must never take the app down
                }
            }
        }
    }
}
