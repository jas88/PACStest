using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace PacsTest
{
    class Options
    {
        [Option('g',"goodid",Required = true,HelpText = "Patient ID of patient with data present")]
        internal string GoodID { get; set; }

        [Option('e',"emptyid",Required = true,HelpText = "Patient ID of patient with NO data present")]
        internal string EmptyID { get; set; }

        [Option('b',"badid",Required = true,HelpText = "Invalid Patient ID")]
        internal string BadID { get; set; }

        [Option('d',"daterange",Required = true,HelpText = "Date range to search for in DICOM format ()")]
        internal string DateRange { get; set; }

        [Option('h',"host",Required = true,HelpText = "Hostname or IP of PACS to test against")]
        internal string PacsHost { get; set; }

        [Option('p',"port",Required = false,HelpText = "Port number of PACS to test against",Default = 104)]
        internal int PacsPort { get; set; }

        [Option('o',"port",Required = false,HelpText = "Port number to listen on",Default = 104)]
        internal int SelfPort { get; set; }

        [Option('n',"pacsname",Required = true,HelpText = "AET of PACS to test against")]
        internal string PacsName { get; set; }

        [Option('s',"selfname",Required = true,HelpText = "Port number of PACS to test against")]
        internal string SelfName { get; set; }

        [Option('m',"movename",Required = true,HelpText = "Name of PACS to send data to")]
        public string MoveName { get; set; }
    }

    class ProcResult
    {
        readonly public int Exitcode;
        readonly public string Stdout, Stderr;

        internal ProcResult(string cmd, string args)
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    FileName = cmd,
                    Arguments = args,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true
                }
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            var outbuff=new StringBuilder();
            var errbuff=new StringBuilder();
            p.OutputDataReceived += (p, l) => outbuff.Append(l.Data);
            p.ErrorDataReceived += (p, l) => errbuff.Append(l.Data);
            p.WaitForExit();
            Exitcode = p.ExitCode;
            Stdout = outbuff.ToString();
            Stderr = errbuff.ToString();
        }
    }
    
    class Program
    {
        static void BreakerSocket(int port, int bytes, string farip, int farport)
        {
            var server = new TcpListener(IPAddress.Any,port);
            server.Start();
            var client = server.AcceptTcpClient();
            var remote = new TcpClient(farip, farport);
            Task.Run(() =>
            {
                int b;
                var i = client.GetStream();
                var o = remote.GetStream();
                while ((b = i.ReadByte()) != -1 && bytes-->0)
                {
                    o.WriteByte((byte) b);
                }
                client.Close();
                remote.Close();
            });
            Task.Run(() =>
            {
                int b;
                var i = remote.GetStream();
                var o = client.GetStream();
                while ((b = i.ReadByte()) != -1 && bytes-->0)
                {
                    o.WriteByte((byte) b);
                }
                client.Close();
                remote.Close();
            });
        }
        
        static bool Test1(Options o)
        {
            var p=new ProcResult("echoscu", $"{o.PacsHost} {o.PacsPort.ToString()}");
            return p.Exitcode == 0;
        }

        /*
         * FINDSCU with patient who DOES have data
         */
        static bool Test234(int n,bool fetch,Options o)
        {
            var pid = n switch
            {
                3 => o.EmptyID,
                4 => o.BadID,
                6 => o.EmptyID,
                7 => o.BadID,
                _ => o.GoodID
            };
            var selfname = (n == 9) ? "HICtestBadName" : o.SelfName;
            var pacsname = (n == 10) ? "HICtestBadName" : o.PacsName;
            var movename = (n==11)?"HICtestBadName":o.MoveName;
            string pacshost;
            string pacsport;

            if (n == 7)
            {
                // Cut off outbound connection mid-stream
                pacsport = (o.SelfPort + 1).ToString();
                pacshost = "127.0.0.1";
                BreakerSocket(o.SelfPort+1,1024,o.PacsHost,o.PacsPort);
            }
            else
            {
                pacsport = o.PacsPort.ToString();
                pacshost = o.PacsHost;
            }

            int selfport;
            if (n == 8)
            {
                // Cut off incoming connection mid-stream
                selfport = o.SelfPort + 1;
                BreakerSocket(o.SelfPort,10240,"127.0.0.1",selfport);
            }
            else
            {
                selfport = o.SelfPort;
            }
            
            var args =
                $"-P -k \"(0010,0020)={pid}\" -k \"0008,0020={o.DateRange}\" {pacshost} {pacsport} -aet {selfname} -aec {pacsname}";
            if (fetch)
            {
                // ReSharper disable once HeapView.BoxingAllocation
                args=$"{args} -aem {movename} --port {selfport}";
            }
            var p = new ProcResult(fetch?"movescu":"findscu", args);
            return p.Exitcode == 0;
        }

        static bool Test(int n,Options o)
        {
            return n == 1 ? Test1(o) : Test234(n, n > 4, o);
        }
        
        static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args).WithParsed(o => Test(1, o));
        }
    }
}
