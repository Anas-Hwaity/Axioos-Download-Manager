using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using ADM.Core.Rules;

namespace ADM.Wpf.UI.Rules
{
    public sealed class RuleEditorViewModel : INotifyPropertyChanged
    {
        private readonly IMutableDownloadRuleProvider provider;
        private readonly DownloadRulePreviewService previewService;
        private DownloadRule? selectedRule;
        private RuleCondition? selectedCondition;
        private RuleAction? selectedAction;
        private string previewUrl = string.Empty;
        private string previewMimeType = string.Empty;
        private string previewFileExtension = string.Empty;
        private string previewDownloadType = "Http";
        private string previewResult = string.Empty;

        public RuleEditorViewModel(IMutableDownloadRuleProvider provider, RuleEngine engine)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            previewService = new DownloadRulePreviewService(engine ?? throw new ArgumentNullException(nameof(engine)), provider);
            Rules = new ObservableCollection<DownloadRule>(CopyRules(provider.GetRules()));
        }

        private static List<DownloadRule> CopyRules(IReadOnlyList<DownloadRule> source)
        {
            return JsonConvert.DeserializeObject<List<DownloadRule>>(JsonConvert.SerializeObject(source)) ?? new List<DownloadRule>();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        public ObservableCollection<DownloadRule> Rules { get; }
        public Array ConditionKinds { get; } = Enum.GetValues(typeof(RuleConditionKind));
        public Array ActionKinds { get; } = Enum.GetValues(typeof(RuleActionKind));
        public string? PersistenceError => provider.LastLoadError;

        public DownloadRule? SelectedRule
        {
            get => selectedRule;
            set
            {
                if (ReferenceEquals(selectedRule, value)) return;
                selectedRule = value;
                SelectedCondition = null;
                SelectedAction = null;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanDelete));
                OnPropertyChanged(nameof(SelectedConditions));
                OnPropertyChanged(nameof(SelectedActions));
            }
        }

        public bool CanDelete => SelectedRule != null;
        public IReadOnlyList<RuleCondition> SelectedConditions => SelectedRule?.Conditions ?? Array.Empty<RuleCondition>();
        public IReadOnlyList<RuleAction> SelectedActions => SelectedRule?.Actions ?? Array.Empty<RuleAction>();

        public RuleCondition? SelectedCondition
        {
            get => selectedCondition;
            set { if (ReferenceEquals(selectedCondition, value)) return; selectedCondition = value; OnPropertyChanged(); }
        }

        public RuleAction? SelectedAction
        {
            get => selectedAction;
            set { if (ReferenceEquals(selectedAction, value)) return; selectedAction = value; OnPropertyChanged(); }
        }

        public string PreviewUrl { get => previewUrl; set { if (previewUrl == (value ?? string.Empty)) return; previewUrl = value ?? string.Empty; OnPropertyChanged(); } }
        public string PreviewMimeType { get => previewMimeType; set { if (previewMimeType == (value ?? string.Empty)) return; previewMimeType = value ?? string.Empty; OnPropertyChanged(); } }
        public string PreviewFileExtension { get => previewFileExtension; set { if (previewFileExtension == (value ?? string.Empty)) return; previewFileExtension = value ?? string.Empty; OnPropertyChanged(); } }
        public string PreviewDownloadType { get => previewDownloadType; set { if (previewDownloadType == (value ?? string.Empty)) return; previewDownloadType = value ?? string.Empty; OnPropertyChanged(); } }
        public string PreviewResult { get => previewResult; private set { if (previewResult == value) return; previewResult = value; OnPropertyChanged(); } }

        public DownloadRule AddRule()
        {
            var rule = new DownloadRule
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "New rule",
                Enabled = true,
                Priority = Rules.Count == 0 ? 0 : Rules.Max(r => r.Priority) + 10
            };
            Rules.Add(rule);
            SelectedRule = rule;
            return rule;
        }

        public bool DeleteSelected()
        {
            var current = SelectedRule;
            if (current == null) return false;
            var removed = Rules.Remove(current);
            SelectedRule = Rules.FirstOrDefault();
            return removed;
        }

        public RuleCondition? AddCondition()
        {
            if (SelectedRule == null) return null;
            var items = (SelectedRule.Conditions ?? Array.Empty<RuleCondition>()).ToList();
            var condition = new RuleCondition { Kind = RuleConditionKind.HostEquals };
            items.Add(condition);
            SelectedRule.Conditions = items;
            SelectedCondition = condition;
            OnPropertyChanged(nameof(SelectedConditions));
            return condition;
        }

        public bool DeleteSelectedCondition()
        {
            if (SelectedRule == null || SelectedCondition == null) return false;
            var items = (SelectedRule.Conditions ?? Array.Empty<RuleCondition>()).ToList();
            var removed = items.Remove(SelectedCondition);
            SelectedRule.Conditions = items;
            SelectedCondition = items.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedConditions));
            return removed;
        }

        public RuleAction? AddAction()
        {
            if (SelectedRule == null) return null;
            var items = (SelectedRule.Actions ?? Array.Empty<RuleAction>()).ToList();
            var action = new RuleAction { Kind = RuleActionKind.DestinationFolder };
            items.Add(action);
            SelectedRule.Actions = items;
            SelectedAction = action;
            OnPropertyChanged(nameof(SelectedActions));
            return action;
        }

        public bool DeleteSelectedAction()
        {
            if (SelectedRule == null || SelectedAction == null) return false;
            var items = (SelectedRule.Actions ?? Array.Empty<RuleAction>()).ToList();
            var removed = items.Remove(SelectedAction);
            SelectedRule.Actions = items;
            SelectedAction = items.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedActions));
            return removed;
        }

        public void Save()
        {
            provider.SaveRules(Rules.ToArray());
            OnPropertyChanged(nameof(PersistenceError));
        }

        public RuleEvaluationResult Preview(DownloadRuleContext context)
        {
            return previewService.Preview(Rules.ToArray(), context);
        }

        public RuleEvaluationResult RunPreview()
        {
            var host = Uri.TryCreate(PreviewUrl, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
            var result = Preview(new DownloadRuleContext
            {
                Url = PreviewUrl,
                Host = host,
                MimeType = PreviewMimeType,
                FileExtension = PreviewFileExtension,
                DownloadType = PreviewDownloadType,
                EvaluationTime = DateTime.Now
            });
            var matched = result.Explanations.Where(item => item.Matched).Select(item => item.RuleName).ToArray();
            PreviewResult = "Matched: " + (matched.Length == 0 ? "none" : string.Join(", ", matched)) +
                            " | Effective actions: " + result.EffectiveActions.Count +
                            " | Conflicts: " + result.Conflicts.Count;
            return result;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
