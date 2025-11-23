using System;
using System.Collections.Generic;
using System.Linq;

namespace NetKeyer.Models
{
    [Flags]
    public enum MidiNoteFunction
    {
        None = 0,
        LeftPaddle = 1,
        RightPaddle = 2,
        StraightKey = 4,
        PTT = 8
    }

    public class MidiNoteMapping
    {
        public int NoteNumber { get; set; }
        public MidiNoteFunction Functions { get; set; }

        public MidiNoteMapping()
        {
        }

        public MidiNoteMapping(int noteNumber, MidiNoteFunction functions)
        {
            NoteNumber = noteNumber;
            Functions = functions;
        }

        public bool HasFunction(MidiNoteFunction function)
        {
            return (Functions & function) != 0;
        }

        public static List<MidiNoteMapping> GetDefaultMappings()
        {
            return new List<MidiNoteMapping>
            {
                new MidiNoteMapping(20, MidiNoteFunction.LeftPaddle | MidiNoteFunction.StraightKey | MidiNoteFunction.PTT),
                new MidiNoteMapping(21, MidiNoteFunction.RightPaddle | MidiNoteFunction.StraightKey | MidiNoteFunction.PTT),
                new MidiNoteMapping(30, MidiNoteFunction.StraightKey),
                new MidiNoteMapping(31, MidiNoteFunction.PTT)
            };
        }
    }
}
