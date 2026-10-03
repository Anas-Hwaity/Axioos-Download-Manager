using System.Windows;
using System.Windows.Controls;
using ADM.Wpf.UI.Rules;

namespace ADM.Wpf.UI.Dialogs.Settings
{
    public partial class RulesView : UserControl
    {
        public RulesView() { InitializeComponent(); }
        public void AttachViewModel(RuleEditorViewModel viewModel) => DataContext = viewModel;
        private RuleEditorViewModel? ViewModel => DataContext as RuleEditorViewModel;
        private void Add_Click(object sender, RoutedEventArgs e) => ViewModel?.AddRule();
        private void Delete_Click(object sender, RoutedEventArgs e) => ViewModel?.DeleteSelected();
        private void AddCondition_Click(object sender, RoutedEventArgs e) => ViewModel?.AddCondition();
        private void DeleteCondition_Click(object sender, RoutedEventArgs e) => ViewModel?.DeleteSelectedCondition();
        private void AddAction_Click(object sender, RoutedEventArgs e) => ViewModel?.AddAction();
        private void DeleteAction_Click(object sender, RoutedEventArgs e) => ViewModel?.DeleteSelectedAction();
        private void Preview_Click(object sender, RoutedEventArgs e) => ViewModel?.RunPreview();
    }
}
