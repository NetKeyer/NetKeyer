// ****************************************************************************
///*!	\file PortHelper.cs
// *	\brief Utilities to support lockless FIFO containers
// *
// *	\copyright	Copyright 2012-2017 FlexRadio Systems.  All Rights Reserved.
// *				Unauthorized use, duplication or distribution of this software is
// *				strictly prohibited by law.
// *
// *	\date 2015-10-08
// *	\author Eric Wachsmann, KE5DTO
// */
// ****************************************************************************
// http://www.cheynewallace.com/get-active-ports-and-associated-process-names-in-c/
// ****************************************************************************

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;


namespace Util
{
    public class PortHelper
    {
        public static string GetProcessFromTCPPort(int tcp_port)
        {
            string ret_val = null;

            List<Port> port_list = GetNetStatPorts();

            List<Port> filtered_list = port_list.Where<Port>(i => i.port_number == tcp_port.ToString()).ToList<Port>();

            if (filtered_list.Count > 0)
                ret_val = filtered_list[0].process_name;

            return ret_val;
        }

        // ===============================================
        // The Method That Parses The NetStat Output
        // And Returns A List Of Port Objects
        // ===============================================
        private static List<Port> GetNetStatPorts()
        {
            var Ports = new List<Port>();
  
            try 
            {
                using (Process p = new Process()) 
                {  
                    ProcessStartInfo ps = new ProcessStartInfo();
                    ps.Arguments = "-a -n -o";
                    ps.FileName = "netstat.exe";
                    ps.UseShellExecute = false;
                    ps.WindowStyle = ProcessWindowStyle.Hidden;
                    ps.RedirectStandardInput = true;
                    ps.RedirectStandardOutput = true;
                    ps.RedirectStandardError = true;
  
                    p.StartInfo = ps;
                    p.Start();
  
                    StreamReader stdOutput = p.StandardOutput;
                    StreamReader stdError = p.StandardError;
  
                    string content = stdOutput.ReadToEnd() + stdError.ReadToEnd();
                    string exitStatus = p.ExitCode.ToString();
       
                    if (exitStatus != "0") 
                    {
                        // Command Errored. Handle Here If Need Be
                    }
  
                    //Get The Rows
                    string[] rows = Regex.Split(content, "\r\n");
                    foreach (string row in rows) 
                    {
                        //Split it baby
                        string[] tokens = Regex.Split(row, "\\s+");
                        if (tokens.Length > 4 && (tokens[1].Equals("UDP") || tokens[1].Equals("TCP")))
                        {
                            string localAddress = Regex.Replace(tokens[2], @"\[(.*?)\]", "1.1.1.1");
                            Ports.Add(new Port 
                            {
                                protocol = localAddress.Contains("1.1.1.1") ? String.Format("{0}v6",tokens[1]) : String.Format("{0}v4",tokens[1]),
                                port_number = localAddress.Split(':')[1],
                                process_name = tokens[1] == "UDP" ? LookupProcess(Convert.ToInt16(tokens[4])) : LookupProcess(Convert.ToInt16(tokens[5]))
                            });
                        }
                    }
                }
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex.Message);
            }
            return Ports;
        }

        private static string LookupProcess(int pid)
        {
            string procName;
            try { procName = Process.GetProcessById(pid).ProcessName; }
            catch (Exception) { procName = "-"; }
            return procName;
        }

        // ===============================================
        // The Port Class We're Going To Create A List Of
        // ===============================================
        private class Port
        {
            public string name
            {
                get
                {
                    return string.Format("{0} ({1} port {2})", this.process_name, this.protocol, this.port_number);
                }
                set { }
            }
            public string port_number { get; set; }
            public string process_name { get; set; }
            public string protocol { get; set; }
        }
    }
}
