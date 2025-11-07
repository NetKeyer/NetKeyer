// ****************************************************************************
///*!	\file Meter.cs
// *	\brief Represents a single meter
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-03-05
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Flex.Util;


namespace Flex.Smoothlake.FlexLib
{
    public enum MeterUnits
    {
        None,
        Volts,
        Amps,
        Db,
        Dbfs,
        Dbm,
        RPM,        
        DegreesF,
        DegreesC,
        SWR,
        Watts,
        Percent
    }

    public class Meter
    {
        public const string SOURCE_SLICE = "SLC";
        public const string SOURCE_AMPLIFIER = "AMP";

        internal Meter(Radio radio, int index)
        {
            this._radio = radio;
            this._index = index;

            //var major = (radio.Version >> 48) & 0xFF;
            //var minor = (radio.Version >> 40) & 0xFF;
            //if (major == 1 && minor <= 10)
            //    volt_denom = 1024.0f;

            if(radio.Version < FlexVersion.Parse("1.11.0.0"))
                volt_denom = 1024.0f;
        }

        private float volt_denom = 256.0f;
        private Radio _radio;

        private int _index;
        public int Index
        {
            get { return _index; }
        }

        private string _source = null;
        public string Source
        {
            get { return _source; }
            set
            {
                if (_source == null)
                    _source = value;
            }
        }

        private int _source_index = -1;
        public int SourceIndex
        {
            get { return _source_index; }
            set
            {
                if (_source_index == -1)
                    _source_index = value;
            }
        }

        private string _name = null;
        public string Name
        {
            get { return _name; }
            set
            {
                if(_name == null)
                    _name = value; 
            }
        }

        private string _description = null;
        public string Description
        {
            get { return _description; }
            set
            {
                if (_description == null)
                    _description = value;
            }
        }

        private double _low = double.MaxValue;
        public double Low
        {
            get { return _low; }
            set
            {
                if (_low == double.MaxValue)
                    _low = value;
            }
        }
        
        private double _high = double.MinValue;
        public double High
        {
            get { return _high; }
            set
            {
                if (_high == double.MinValue)
                    _high = value;
            }
        }

        private double _fps = double.MinValue;
        public double FPS
        {
            get { return _fps; }
            set
            {
                if (_fps == double.MinValue)
                    _fps = value;
            }
        }

        private bool _peak;
        public bool Peak
        {
            get { return _peak; }
            set
            {
                _peak = value;
            }
        }

        private MeterUnits _units = MeterUnits.None;
        public MeterUnits Units
        {
            get { return _units; }
            set
            {
                if (_units == MeterUnits.None)
                    _units = value;
            }
        }

        public void UpdateValue(short new_raw_value)
        {
            
            short raw_value = new_raw_value;
            float new_value;

            switch (_units)
            {
                case MeterUnits.Db:
                case MeterUnits.Dbm:
                case MeterUnits.Dbfs:
                case MeterUnits.SWR:
                    new_value = raw_value / 128.0f;
                    break;
                case MeterUnits.Volts:
                case MeterUnits.Amps:
                    new_value = raw_value / volt_denom;
                    break;
                case MeterUnits.DegreesF:
                case MeterUnits.DegreesC:
                    new_value = raw_value / 64.0f;
                    break;
                default:
                    new_value = raw_value;
                    break;
            }

            OnDataReady(this, new_value);
        }

        public delegate void DataReadyEventHandler(Meter meter, float data);
        public event DataReadyEventHandler DataReady;
        private void OnDataReady(Meter meter, float data)
        {
            if (DataReady != null)
                DataReady(meter, data);
        }

        public override string ToString()
        {
            if (this._name != null)
                return _index.ToString()+": "+_source+"-"+_source_index+" "+_name;
            else return base.ToString();
        }
    }
}