// ****************************************************************************
///*!	\file WindowHelper.cs
// *	\brief Helper functions for finding Windows based on Text
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2015-10-12
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;


namespace Flex.Util
{
    public class WindowHelper
    {
        /// <summary>
        /// Finds a Window based on a string.  Supports partial matches.
        /// </summary>
        /// <param name="wndclass">The wndclass to use in the search</param>
        /// <param name="title">The search string to match</param>
        /// <returns>An IntPtr to the window if a match is found, otherwise null</returns>
        public static IntPtr SearchForWindow(string wndclass, string title)
        {
            SearchData sd = new SearchData { Wndclass = wndclass, Title = title };
            EnumWindows(new EnumWindowsProc(EnumProc), ref sd);
            return sd.hWnd;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, ref SearchData data);
        private static bool EnumProc(IntPtr hWnd, ref SearchData data)
        {
            // Check classname and title
            // This is different from FindWindow() in that the code below allows partial matches
            StringBuilder sb = new StringBuilder(1024);
            GetClassName(hWnd, sb, sb.Capacity);
            if (sb.ToString().StartsWith(data.Wndclass))
            {
                sb = new StringBuilder(1024);
                string title = data.Title.ToLower();
                GetWindowText(hWnd, sb, sb.Capacity);
                if (sb.ToString().ToLower().Contains(title)) // .StartsWith(data.Title))
                {
                    data.hWnd = hWnd;
                    return false;    // Found the wnd, halt enumeration
                }
            }
            return true;
        }

        public class SearchData
        {
            public string Wndclass;
            public string Title;
            public IntPtr hWnd;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, ref SearchData data);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
    }
}
