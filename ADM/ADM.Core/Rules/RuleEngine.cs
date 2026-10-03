using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ADM.Core.Rules
{
    public enum RuleConditionKind
    {
        HostEquals,
        UrlContains,
        UrlRegex,
        MimeEquals,
        FileExtensionEquals,
        SizeRange,
        DownloadTypeEquals,
        DayOfWeek,
        TimeWindow,
        BrowserContextEquals
    }

    public enum RuleActionKind
    {
        DestinationFolder,
        CategoryTag,
        Queue,
        SpeedLimitKiB,
        MaxConnections,
        StartBehavior
    }

    public sealed class DownloadRuleContext
    {
        public string Url { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string MimeType { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long? Size { get; set; }
        public string DownloadType { get; set; } = string.Empty;
        public DateTime EvaluationTime { get; set; } = DateTime.Now;
        public string BrowserContext { get; set; } = string.Empty;
    }

    public sealed class RuleCondition
    {
        public RuleConditionKind Kind { get; set; }
        public string Value { get; set; } = string.Empty;
        public long? Minimum { get; set; }
        public long? Maximum { get; set; }
        public TimeSpan? StartTime { get; set; }
        public TimeSpan? EndTime { get; set; }
    }

    public sealed class RuleAction
    {
        public RuleActionKind Kind { get; set; }
        public string Value { get; set; } = string.Empty;
        public int? NumericValue { get; set; }
    }

    public sealed class DownloadRule
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public int Priority { get; set; }
        public IReadOnlyList<RuleCondition> Conditions { get; set; } = Array.Empty<RuleCondition>();
        public IReadOnlyList<RuleAction> Actions { get; set; } = Array.Empty<RuleAction>();
        public bool StopProcessing { get; set; }
    }

    public sealed class RuleMatchExplanation
    {
        public RuleMatchExplanation(string ruleId, string ruleName, bool matched, IReadOnlyList<string> conditionResults)
        {
            RuleId = ruleId;
            RuleName = ruleName;
            Matched = matched;
            ConditionResults = conditionResults;
        }

        public string RuleId { get; }
        public string RuleName { get; }
        public bool Matched { get; }
        public IReadOnlyList<string> ConditionResults { get; }
    }

    public sealed class RuleActionConflict
    {
        public RuleActionConflict(RuleActionKind kind, RuleAction effectiveAction, RuleAction ignoredAction)
        {
            Kind = kind;
            EffectiveAction = effectiveAction;
            IgnoredAction = ignoredAction;
        }

        public RuleActionKind Kind { get; }
        public RuleAction EffectiveAction { get; }
        public RuleAction IgnoredAction { get; }
    }

    public sealed class RuleEvaluationResult
    {
        public RuleEvaluationResult(
            IReadOnlyList<RuleMatchExplanation> explanations,
            IReadOnlyList<RuleAction> orderedActions,
            IReadOnlyList<RuleAction> effectiveActions,
            IReadOnlyList<RuleActionConflict> conflicts)
        {
            Explanations = explanations;
            OrderedActions = orderedActions;
            EffectiveActions = effectiveActions;
            Conflicts = conflicts;
        }

        public IReadOnlyList<RuleMatchExplanation> Explanations { get; }
        public IReadOnlyList<RuleAction> OrderedActions { get; }
        public IReadOnlyList<RuleAction> EffectiveActions { get; }
        public IReadOnlyList<RuleActionConflict> Conflicts { get; }
    }

    public sealed class RuleEngine
    {
        private static TimeSpan RegexTimeout => TimeSpan.FromMilliseconds(100);

        public RuleEvaluationResult Evaluate(IEnumerable<DownloadRule> rules, DownloadRuleContext context)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (context == null) throw new ArgumentNullException(nameof(context));

            var explanations = new List<RuleMatchExplanation>();
            var orderedActions = new List<RuleAction>();
            var orderedRules = rules
                .Where(r => r != null && r.Enabled)
                .OrderBy(r => r.Priority)
                .ThenBy(r => r.Id, StringComparer.Ordinal)
                .ToList();

            foreach (var rule in orderedRules)
            {
                var conditionResults = new List<string>();
                var matched = true;
                foreach (var condition in rule.Conditions ?? Array.Empty<RuleCondition>())
                {
                    var conditionMatched = Matches(condition, context, out var reason);
                    conditionResults.Add(reason);
                    if (!conditionMatched) matched = false;
                }

                explanations.Add(new RuleMatchExplanation(rule.Id, rule.Name, matched, conditionResults));
                if (!matched) continue;

                foreach (var action in rule.Actions ?? Array.Empty<RuleAction>())
                {
                    orderedActions.Add(action);
                }

                if (rule.StopProcessing) break;
            }

            ResolveEffectiveActions(orderedActions, out var effectiveActions, out var conflicts);
            return new RuleEvaluationResult(explanations, orderedActions, effectiveActions, conflicts);
        }

        private static void ResolveEffectiveActions(
            IReadOnlyList<RuleAction> orderedActions,
            out IReadOnlyList<RuleAction> effectiveActions,
            out IReadOnlyList<RuleActionConflict> conflicts)
        {
            var effectiveByKind = new Dictionary<RuleActionKind, RuleAction>();
            var effective = new List<RuleAction>();
            var conflictList = new List<RuleActionConflict>();

            foreach (var action in orderedActions)
            {
                if (action == null) continue;
                if (!effectiveByKind.TryGetValue(action.Kind, out var current))
                {
                    effectiveByKind[action.Kind] = action;
                    effective.Add(action);
                    continue;
                }

                if (!Equivalent(current, action))
                {
                    conflictList.Add(new RuleActionConflict(action.Kind, current, action));
                }
            }

            effectiveActions = effective;
            conflicts = conflictList;
        }

        private static bool Equivalent(RuleAction left, RuleAction right)
        {
            return left.Kind == right.Kind &&
                   string.Equals(left.Value ?? string.Empty, right.Value ?? string.Empty, StringComparison.Ordinal) &&
                   left.NumericValue == right.NumericValue;
        }

        private static bool Matches(RuleCondition condition, DownloadRuleContext context, out string reason)
        {
            if (condition == null)
            {
                reason = "condition:null:false";
                return false;
            }

            bool matched;
            switch (condition.Kind)
            {
                case RuleConditionKind.HostEquals:
                    matched = EqualsIgnoreCase(context.Host, condition.Value);
                    break;
                case RuleConditionKind.UrlContains:
                    matched = ContainsIgnoreCase(context.Url, condition.Value);
                    break;
                case RuleConditionKind.UrlRegex:
                    matched = RegexMatches(context.Url, condition.Value);
                    break;
                case RuleConditionKind.MimeEquals:
                    matched = EqualsIgnoreCase(context.MimeType, condition.Value);
                    break;
                case RuleConditionKind.FileExtensionEquals:
                    matched = EqualsIgnoreCase(NormalizeExtension(context.FileExtension), NormalizeExtension(condition.Value));
                    break;
                case RuleConditionKind.SizeRange:
                    matched = context.Size.HasValue &&
                              (!condition.Minimum.HasValue || context.Size.Value >= condition.Minimum.Value) &&
                              (!condition.Maximum.HasValue || context.Size.Value <= condition.Maximum.Value);
                    break;
                case RuleConditionKind.DownloadTypeEquals:
                    matched = EqualsIgnoreCase(context.DownloadType, condition.Value);
                    break;
                case RuleConditionKind.DayOfWeek:
                    matched = MatchesDay(context.EvaluationTime.DayOfWeek, condition.Value);
                    break;
                case RuleConditionKind.TimeWindow:
                    matched = MatchesTimeWindow(context.EvaluationTime.TimeOfDay, condition.StartTime, condition.EndTime);
                    break;
                case RuleConditionKind.BrowserContextEquals:
                    matched = EqualsIgnoreCase(context.BrowserContext, condition.Value);
                    break;
                default:
                    matched = false;
                    break;
            }

            reason = condition.Kind + ":" + (matched ? "matched" : "not-matched");
            return matched;
        }

        private static bool RegexMatches(string input, string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern)) return false;
            try
            {
                return Regex.IsMatch(input ?? string.Empty, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        private static bool MatchesDay(DayOfWeek day, string configuredDays)
        {
            if (string.IsNullOrWhiteSpace(configuredDays)) return false;
            return configuredDays
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(item => string.Equals(item, day.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        private static bool MatchesTimeWindow(TimeSpan current, TimeSpan? start, TimeSpan? end)
        {
            if (!start.HasValue || !end.HasValue) return false;
            if (start.Value <= end.Value)
            {
                return current >= start.Value && current <= end.Value;
            }
            return current >= start.Value || current <= end.Value;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string value, string fragment)
        {
            if (string.IsNullOrEmpty(fragment)) return false;
            return (value ?? string.Empty).IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeExtension(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.StartsWith(".", StringComparison.Ordinal) ? value : "." + value;
        }
    }
}
