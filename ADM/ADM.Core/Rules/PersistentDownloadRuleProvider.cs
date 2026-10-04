using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ADM.Core.Rules
{
    public interface IMutableDownloadRuleProvider : IDownloadRuleProvider
    {
        string? LastLoadError { get; }
        void SaveRules(IReadOnlyList<DownloadRule> rules);
    }

    public sealed class PersistentDownloadRuleProvider : IMutableDownloadRuleProvider
    {
        private const int SchemaVersion = 1;
        private const int MaxRules = 256;
        private const int MaxItemsPerRule = 32;
        private readonly string filePath;
        private IReadOnlyList<DownloadRule> rules = Array.Empty<DownloadRule>();

        public PersistentDownloadRuleProvider(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("Rule file path is required.", nameof(filePath));
            this.filePath = Path.GetFullPath(filePath);
            Reload();
        }

        public string? LastLoadError { get; private set; }

        public IReadOnlyList<DownloadRule> GetRules() => rules;

        public void Reload()
        {
            LastLoadError = null;
            if (!File.Exists(filePath))
            {
                rules = Array.Empty<DownloadRule>();
                return;
            }

            try
            {
                var info = new FileInfo(filePath);
                if (info.Length > 1024 * 1024) throw new InvalidDataException("Rule file exceeds the 1 MiB limit.");
                var envelope = JsonConvert.DeserializeObject<RuleFileEnvelope>(File.ReadAllText(filePath));
                if (envelope == null || envelope.SchemaVersion != SchemaVersion) throw new InvalidDataException("Unsupported rule file schema.");
                rules = Validate(envelope.Rules ?? new List<DownloadRule>()).ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException || ex is ArgumentException)
            {
                rules = Array.Empty<DownloadRule>();
                LastLoadError = ex.GetType().Name;
            }
        }

        public void SaveRules(IReadOnlyList<DownloadRule> newRules)
        {
            if (newRules == null) throw new ArgumentNullException(nameof(newRules));
            var validated = Validate(newRules).ToArray();
            var envelope = new RuleFileEnvelope { SchemaVersion = SchemaVersion, Rules = validated.ToList() };
            var json = JsonConvert.SerializeObject(envelope, Formatting.Indented);
            var directory = Path.GetDirectoryName(filePath);
            if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Rule file directory is unavailable.");
            Directory.CreateDirectory(directory);
            var temporary = filePath + ".tmp";
            var backup = filePath + ".bak";
            File.WriteAllText(temporary, json);
            try
            {
                if (File.Exists(filePath)) File.Replace(temporary, filePath, backup, true);
                else File.Move(temporary, filePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            rules = validated;
            LastLoadError = null;
        }

        private static IEnumerable<DownloadRule> Validate(IEnumerable<DownloadRule> input)
        {
            var items = input.ToList();
            if (items.Count > MaxRules) throw new InvalidDataException("Too many download rules.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var rule in items)
            {
                if (rule == null) throw new InvalidDataException("Null download rule.");
                if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 128 || !ids.Add(rule.Id)) throw new InvalidDataException("Rule IDs must be unique and bounded.");
                if ((rule.Name ?? string.Empty).Length > 256) throw new InvalidDataException("Rule name is too long.");
                var conditions = rule.Conditions ?? Array.Empty<RuleCondition>();
                var actions = rule.Actions ?? Array.Empty<RuleAction>();
                if (conditions.Count > MaxItemsPerRule || actions.Count > MaxItemsPerRule) throw new InvalidDataException("Rule condition/action count exceeds the limit.");
                foreach (var condition in conditions)
                {
                    if (condition == null || !Enum.IsDefined(typeof(RuleConditionKind), condition.Kind)) throw new InvalidDataException("Invalid rule condition.");
                    if ((condition.Value ?? string.Empty).Length > 2048) throw new InvalidDataException("Rule condition value is too long.");
                }
                foreach (var action in actions)
                {
                    if (action == null || !Enum.IsDefined(typeof(RuleActionKind), action.Kind)) throw new InvalidDataException("Invalid rule action.");
                    if ((action.Value ?? string.Empty).Length > 4096) throw new InvalidDataException("Rule action value is too long.");
                }
            }
            return items;
        }

        private sealed class RuleFileEnvelope
        {
            public int SchemaVersion { get; set; }
            public List<DownloadRule>? Rules { get; set; }
        }
    }

    public sealed class DownloadRulePreviewService
    {
        private readonly RuleEngine engine;
        private readonly IDownloadRuleProvider provider;

        public DownloadRulePreviewService(RuleEngine engine, IDownloadRuleProvider provider)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        public RuleEvaluationResult Preview(DownloadRuleContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return engine.Evaluate(provider.GetRules(), context);
        }

        public RuleEvaluationResult Preview(IReadOnlyList<DownloadRule> draftRules, DownloadRuleContext context)
        {
            if (draftRules == null) throw new ArgumentNullException(nameof(draftRules));
            if (context == null) throw new ArgumentNullException(nameof(context));
            return engine.Evaluate(draftRules, context);
        }
    }
}
