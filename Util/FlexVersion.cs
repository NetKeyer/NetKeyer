// ****************************************************************************
///*!	\file FlexVersion.cs
// *	\brief Version functions
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2012-01-01
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;

namespace Flex.Util
{
    public static class FlexVersion
    {
        public static ulong Parse(string s)
        {
            ulong ver = 0;
            string[] tokens = s.Split('.');
            bool b;
            ulong temp;


            if (tokens.Length < 4)
            {
                Debug.WriteLine("Util::FlexVersion::TryParse: Error - Version requires 4 digits (" + s + ")");
                return 0;
            }

            for (int i = 0; i < 3; i++)
            {
                b = ulong.TryParse(tokens[i], out temp);
                if (!b)
                {
                    Debug.WriteLine("Util::FlexVersion::TryParse: Error - Invalid digit (" + tokens[i] + ")");
                    return 0;
                }

                //each version field is one byte, except for the build version field, which is 4 bytes
                // <Maj>.<Min>.<Iteration>.<Build>
                ver += temp << ((6 - i) * 8);
            }


            b = ulong.TryParse(tokens[3], out temp);
            if (!b)
            {
                // Build Version is a callsign from a developer's machine
                ver += uint.MaxValue;     //4294967295
            }
            else
            {
                // Build Version is a number from the Jenkins build server
                ver += temp;
            }

            return ver;
        }

        public static bool TryParse(string s, out ulong ver)
        {
            ver = 0;
            string[] tokens = s.Split('.');
            bool b;
            ulong temp;
            
        
            if (tokens.Length < 4)
            {
                Debug.WriteLine("Util::FlexVersion::TryParse: Error - Version requires 4 digits (" + s + ")");
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                b = ulong.TryParse(tokens[i], out temp);
                if (!b)
                {
                    Debug.WriteLine("Util::FlexVersion::TryParse: Error - Invalid digit (" + tokens[i] + ")");
                    ver = 0;
                    return false;
                }

                //each version field is one byte, except for the build version field, which is 4 bytes
                // <Maj>.<Min>.<Iteration>.<Build>
                ver += temp << ((6 - i) * 8);
            }

            
            b = ulong.TryParse(tokens[3], out temp);
            if (!b)
            {
                // Build Version is a callsign from a developer's machine
                ver += uint.MaxValue;     //4294967295
            }
            else
            {
                // Build Version is a number from the Jenkins build server
                ver += temp;
            }

            return true;
        }

        public static string ToString(ulong ver)
        {
            return ((ver >> 48) & 0xFF) + "." + ((ver >> 40) & 0xFF) + "." + ((ver >> 32) & 0xFF) + "." + (ver & 0xFFFF);
        }

        public static string VersionStringWithoutBuildNumber(ulong ver)
        {
            return ((ver >> 48) & 0xFF) + "." + ((ver >> 40) & 0xFF) + "." + ((ver >> 32) & 0xFF);
        }
    }
}
