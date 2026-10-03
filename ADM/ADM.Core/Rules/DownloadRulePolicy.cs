using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ADM.Core.Downloader.Adaptive.Dash;
using ADM.Core.Downloader.Adaptive.Hls;
using ADM.Core.Downloader.Progressive.DualHttp;
using ADM.Core.Downloader.Progressive.SingleHttp;

namespace ADM.Core.Rules
{
    public interface IDownloadRuleProvider
    {
        IReadOnlyList<DownloadRule> GetRules();
    }

    public sealed class EmptyDownloadRuleProvider : IDownloadRuleProvider
    {
        public IReadOnlyList<DownloadRule> GetRules() => Array.Empty<DownloadRule>();
    }

    public sealed class DownloadCreationRuleOutcome
    {
        public DownloadCreationRuleOutcome(
            RuleEvaluationResult evaluation,
            string? destinationFolder,
            string? queueId,
            bool? startImmediately,
            int? speedLimitKiB,
            int? maxConnections,
            IReadOnlyList<string> categoryTags)
        {
            Evaluation = evaluation;
            DestinationFolder = destinationFolder;
            QueueId = queueId;
            StartImmediately = startImmediately;
            SpeedLimitKiB = speedLimitKiB;
            MaxConnections = maxConnections;
            CategoryTags = categoryTags ?? Array.Empty<string>();
        }

        public RuleEvaluationResult Evaluation { get; }
        public string? DestinationFolder { get; }
        public string? QueueId { get; }
        public bool? StartImmediately { get; }
        public int? SpeedLimitKiB { get; }
        public int? MaxConnections { get; }
        public IReadOnlyList<string> CategoryTags { get; }
    }

    public sealed class DownloadRulePolicy
    {
        private readonly RuleEngine ruleEngine;
        private readonly IDownloadRuleProvider ruleProvider;

        public DownloadRulePolicy(RuleEngine ruleEngine, IDownloadRuleProvider ruleProvider)
        {
            this.ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
            this.ruleProvider = ruleProvider ?? throw new ArgumentNullException(nameof(ruleProvider));
        }

        public DownloadCreationRuleOutcome EvaluateCreation(
            IRequestData requestData,
            string fileName,
            string? currentDestinationFolder,
            bool currentStartImmediately,
            string? currentQueueId)
        {
            if (requestData == null) throw new ArgumentNullException(nameof(requestData));

            var evaluation = ruleEngine.Evaluate(ruleProvider.GetRules(), BuildContext(requestData, fileName));
            var destination = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.DestinationFolder);
            var queue = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.Queue);
            var start = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.StartBehavior);
            var speed = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.SpeedLimitKiB);
            var connections = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.MaxConnections);
            var categoryTag = evaluation.EffectiveActions.FirstOrDefault(a => a.Kind == RuleActionKind.CategoryTag);

            var destinationValue = destination?.Value;
            var queueValue = queue?.Value;
            return new DownloadCreationRuleOutcome(
                evaluation,
                string.IsNullOrWhiteSpace(destinationValue) ? currentDestinationFolder : destinationValue,
                string.IsNullOrWhiteSpace(queueValue) ? currentQueueId : queueValue,
                ResolveStartBehavior(start?.Value, currentStartImmediately),
                ResolvePositiveNumeric(speed),
                ResolvePositiveNumeric(connections),
                ResolveCategoryTags(categoryTag));
        }

        private static IReadOnlyList<string> ResolveCategoryTags(RuleAction? action)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Value)) return Array.Empty<string>();
            var tag = action.Value.Trim();
            return tag.Length <= 128 ? new[] { tag } : Array.Empty<string>();
        }

        private static int? ResolvePositiveNumeric(RuleAction? action)
        {
            if (action == null) return null;
            if (action.NumericValue.HasValue && action.NumericValue.Value > 0) return action.NumericValue.Value;
            return int.TryParse(action.Value, out var value) && value > 0 ? value : (int?)null;
        }

        private static bool? ResolveStartBehavior(string? value, bool current)
        {
            if (string.IsNullOrWhiteSpace(value)) return current;
            if (string.Equals(value, "start", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(value, "queue", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(value, "ask", StringComparison.OrdinalIgnoreCase)) return current;
            return current;
        }

        private static DownloadRuleContext BuildContext(IRequestData requestData, string fileName)
        {
            string url = string.Empty;
            string mime = string.Empty;
            string type = string.Empty;
            long? size = null;

            switch (requestData)
            {
                case SingleSourceHTTPDownloadInfo info:
                    url = info.Uri ?? string.Empty;
                    mime = info.ContentType ?? string.Empty;
                    size = info.ContentLength > 0 ? info.ContentLength : (long?)null;
                    type = "Http";
                    break;
                case DualSourceHTTPDownloadInfo info:
                    url = info.Uri1 ?? string.Empty;
                    mime = info.ContentType1 ?? info.ContentType2 ?? string.Empty;
                    size = info.ContentLength > 0 ? info.ContentLength : (long?)null;
                    type = "Dash";
                    break;
                case MultiSourceHLSDownloadInfo info:
                    url = !string.IsNullOrWhiteSpace(info.VideoUri) ? info.VideoUri : info.AudioUri ?? string.Empty;
                    mime = info.ContentType ?? string.Empty;
                    type = "Hls";
                    break;
                case MultiSourceDASHDownloadInfo info:
                    url = info.Url ?? string.Empty;
                    mime = info.VideoMimeType ?? info.AudioMimeType ?? string.Empty;
                    type = "Mpd-Dash";
                    break;
            }

            var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
            return new DownloadRuleContext
            {
                Url = url,
                Host = host,
                MimeType = mime,
                FileExtension = Path.GetExtension(fileName ?? string.Empty),
                Size = size,
                DownloadType = type,
                EvaluationTime = DateTime.Now,
                BrowserContext = string.Empty
            };
        }
    }
}
