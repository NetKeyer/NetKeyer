using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NetKeyer.Models;
using NetKeyer.ViewModels;

namespace NetKeyer.Views
{
    public partial class MidiConfigDialog : Window
    {
        private MidiConfigDialogViewModel _viewModel;

        public bool ConfigurationSaved { get; private set; }
        public List<MidiNoteMapping> Mappings { get; private set; }

        public MidiConfigDialog()
        {
            InitializeComponent();
            _viewModel = new MidiConfigDialogViewModel();
            DataContext = _viewModel;
        }

        public void LoadMappings(List<MidiNoteMapping> mappings)
        {
            _viewModel.LoadMappings(mappings);
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Mappings = _viewModel.GetMappings();
            ConfigurationSaved = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            ConfigurationSaved = false;
            Close();
        }
    }
}
