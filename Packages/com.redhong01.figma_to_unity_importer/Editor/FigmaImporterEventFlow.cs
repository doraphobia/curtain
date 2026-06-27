using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FigmaImporter.Editor
{
    internal static class FigmaImporterEventFlow
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, string> LastStepByChain = new Dictionary<string, string>();
        private static int _chainCounter;

        public static string Start(string feature, string trigger, string details = null)
        {
            var chainId = $"{DateTime.Now:HHmmss.fff}-{Interlocked.Increment(ref _chainCounter)}";
            lock (SyncRoot)
            {
                LastStepByChain[chainId] = string.Empty;
            }

            LogFlow($"[FigmaImporter][Flow][{feature}][{chainId}] START trigger={Safe(trigger)} details={Safe(details)}");
            return chainId;
        }

        public static void Step(
            string feature,
            string chainId,
            string step,
            string details = null,
            bool allowDuplicate = false)
        {
            if (string.IsNullOrWhiteSpace(chainId))
            {
                return;
            }

            lock (SyncRoot)
            {
                if (!LastStepByChain.ContainsKey(chainId))
                {
                    return;
                }

                var normalizedStep = step ?? string.Empty;
                if (!allowDuplicate)
                {
                    if (LastStepByChain.TryGetValue(chainId, out var lastStep) &&
                        string.Equals(lastStep, normalizedStep, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                LastStepByChain[chainId] = normalizedStep;
            }

            LogFlow($"[FigmaImporter][Flow][{feature}][{chainId}] STEP {Safe(step)} details={Safe(details)}");
        }

        public static void End(string feature, string chainId, string result, string details = null)
        {
            if (string.IsNullOrWhiteSpace(chainId))
            {
                return;
            }

            var removed = false;
            lock (SyncRoot)
            {
                removed = LastStepByChain.Remove(chainId);
            }

            if (!removed)
            {
                return;
            }

            LogFlow($"[FigmaImporter][Flow][{feature}][{chainId}] END result={Safe(result)} details={Safe(details)}");
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Replace('\n', ' ').Replace('\r', ' ');
        }

        private static void LogFlow(string message)
        {
            // Flow logs are high-frequency operational telemetry.
            // No stacktrace keeps Console readable while preserving structured chain data.
            Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
        }
    }
}
