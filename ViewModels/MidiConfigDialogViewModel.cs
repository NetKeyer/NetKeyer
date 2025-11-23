using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetKeyer.Models;

namespace NetKeyer.ViewModels
{
    public partial class MidiNoteConfigRow : ObservableObject
    {
        [ObservableProperty]
        private int _noteNumber;

        [ObservableProperty]
        private bool _isLeftPaddle;

        [ObservableProperty]
        private bool _isRightPaddle;

        [ObservableProperty]
        private bool _isStraightKey;

        [ObservableProperty]
        private bool _isPtt;

        public MidiNoteConfigRow(int noteNumber)
        {
            NoteNumber = noteNumber;
        }

        public MidiNoteFunction GetFunctions()
        {
            var functions = MidiNoteFunction.None;
            if (IsLeftPaddle) functions |= MidiNoteFunction.LeftPaddle;
            if (IsRightPaddle) functions |= MidiNoteFunction.RightPaddle;
            if (IsStraightKey) functions |= MidiNoteFunction.StraightKey;
            if (IsPtt) functions |= MidiNoteFunction.PTT;
            return functions;
        }

        public void SetFunctions(MidiNoteFunction functions)
        {
            IsLeftPaddle = (functions & MidiNoteFunction.LeftPaddle) != 0;
            IsRightPaddle = (functions & MidiNoteFunction.RightPaddle) != 0;
            IsStraightKey = (functions & MidiNoteFunction.StraightKey) != 0;
            IsPtt = (functions & MidiNoteFunction.PTT) != 0;
        }
    }

    public partial class MidiConfigDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<MidiNoteConfigRow> _noteConfigurations = new();

        [ObservableProperty]
        private int? _selectedNoteNumber = 20;

        [ObservableProperty]
        private string _addNoteError = "";

        public MidiConfigDialogViewModel()
        {
        }

        public void LoadMappings(List<MidiNoteMapping> mappings)
        {
            NoteConfigurations.Clear();

            // Create a lookup of existing mappings
            var mappingDict = mappings.ToDictionary(m => m.NoteNumber, m => m.Functions);

            // Add all configured notes
            foreach (var mapping in mappings.OrderBy(m => m.NoteNumber))
            {
                var row = new MidiNoteConfigRow(mapping.NoteNumber);
                row.SetFunctions(mapping.Functions);
                NoteConfigurations.Add(row);
            }
        }

        public List<MidiNoteMapping> GetMappings()
        {
            var mappings = new List<MidiNoteMapping>();

            foreach (var row in NoteConfigurations)
            {
                var functions = row.GetFunctions();
                if (functions != MidiNoteFunction.None)
                {
                    mappings.Add(new MidiNoteMapping(row.NoteNumber, functions));
                }
            }

            return mappings;
        }

        [RelayCommand]
        private void AddNote()
        {
            // Clear previous error
            AddNoteError = "";

            // Validate that a note number was entered
            if (!SelectedNoteNumber.HasValue)
            {
                AddNoteError = "Please enter a note number";
                return;
            }

            int noteNumber = SelectedNoteNumber.Value;

            // Validate note number range
            if (noteNumber < 0 || noteNumber > 127)
            {
                AddNoteError = "Note number must be between 0 and 127";
                return;
            }

            // Check if note already exists
            if (NoteConfigurations.Any(n => n.NoteNumber == noteNumber))
            {
                AddNoteError = $"Note {noteNumber} is already in the list";
                return;
            }

            var newRow = new MidiNoteConfigRow(noteNumber);

            // Insert in sorted order
            int insertIndex = 0;
            for (int i = 0; i < NoteConfigurations.Count; i++)
            {
                if (NoteConfigurations[i].NoteNumber > noteNumber)
                {
                    break;
                }
                insertIndex = i + 1;
            }

            NoteConfigurations.Insert(insertIndex, newRow);

            // Clear the input and error after successful add
            SelectedNoteNumber = null;
        }

        [RelayCommand]
        private void RemoveNote(MidiNoteConfigRow row)
        {
            if (row != null)
            {
                NoteConfigurations.Remove(row);
            }
        }

        [RelayCommand]
        private void ResetToDefaults()
        {
            LoadMappings(MidiNoteMapping.GetDefaultMappings());
        }
    }
}
