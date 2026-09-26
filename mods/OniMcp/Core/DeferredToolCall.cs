using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OniMcp.Core
{
    // Only the direct HTTP tools/call route may start an operation that spans frames.
    // Batch/program/resource calls cannot accidentally start one and lose its result.
    internal static class DeferredToolCall
    {
        [ThreadStatic] private static JObject directArguments;
        private static int cancellationGeneration;
        internal static int CancellationGeneration => Volatile.Read(ref cancellationGeneration);
        internal static void CancelPending() => Interlocked.Increment(ref cancellationGeneration);
        internal static bool IsDirect(JObject arguments) => arguments != null && ReferenceEquals(arguments, directArguments);

        internal static CallToolResult Invoke(JObject arguments, Func<CallToolResult> action)
        {
            var previous = directArguments;
            directArguments = arguments;
            try { return action(); }
            finally { directArguments = previous; }
        }
    }

    internal sealed class DeferredToolResult : CallToolResult
    {
        [JsonIgnore] internal Task<CallToolResult> Completion { get; }

        internal DeferredToolResult(Task<CallToolResult> completion)
        {
            Completion = completion ?? throw new ArgumentNullException(nameof(completion));
            Content = new List<ToolContent>();
        }

        internal async Task<CallToolResult> Resolve()
        {
            CallToolResult result;
            try { result = await Completion.ConfigureAwait(false); }
            catch (Exception ex) { result = Error("Deferred tool failed: " + ex.Message); }
            if (result == null) result = Error("Deferred tool returned no result");
            if (result.Content == null) result.Content = new List<ToolContent>();
            // Preserve middleware notifications attached after the handler returned.
            result.Content.AddRange(Content);
            return result;
        }
    }
}
