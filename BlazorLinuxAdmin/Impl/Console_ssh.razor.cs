namespace BlazorLinuxAdmin.Impl
{
    using System;
    using System.Collections.Generic;
    using BlazorPlus;
    using Renci.SshNet;

    public partial class Console_ssh : IDisposable
    {
        private string server;
        private string username;
        private string password;
        private string viewmode;
        private bool isdisposed = false;

        private SshClient client;
        private ShellStream shellstream;

        private readonly Queue<string> linequeue = new Queue<string>();
        private Action notifylineready;

        protected override void OnInitialized ()
        {
            this.Init();
            base.OnInitialized();
        }

        private void Init ()
        {
            if (this.viewmode == null)
            {
                if (BlazorSession.Current.Browser == null)
                {
                    BlazorSession.Current.SetTimeout(10, delegate
                    {
                        this.StateHasChanged();
                    });
                    return;
                }

                this.server = BlazorSession.Current.Browser.GetMemoryItem("console_server") as string ?? "localhost";
                this.username = BlazorSession.Current.Browser.GetMemoryItem("console_username") as string ?? "";//pi
                this.password = BlazorSession.Current.Browser.GetMemoryItem("console_password") as string ?? "";//raspberry

                this.viewmode = "login";
            }
        }

        void IDisposable.Dispose ()
        {
            this.isdisposed = true;
            if (this.client != null)
            {
                this.client.Dispose();
            }

            this.client = null;
        }

        private string client_guid = Guid.NewGuid().ToString();

        private void DoLogin ()
        {
            if (string.IsNullOrWhiteSpace(this.username))
            {
                BlazorSession.Current.Toast("require username");
                return;
            }
            if (string.IsNullOrWhiteSpace(this.password))
            {
                BlazorSession.Current.Toast("require password");
                return;
            }

            this.client_guid = Guid.NewGuid().ToString();
            string guid = this.client_guid;

            var bses = BlazorSession.Current;

            this.viewmode = "connecting";
            this.client = new SshClient(this.server, this.username, this.password);
            bool threadok = System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    this.client.Connect();
                }
                catch (Exception x)
                {
                    if (guid != this.client_guid)
                    {
                        return;
                    }

                    this.viewmode = "login";

                    bses.InvokeInRenderThread(delegate
                    {
                        bses.Alert("Error", x.ToString());
                        this.StateHasChanged();
                    });
                    return;
                }

                try
                {
                    if (guid != this.client_guid)
                    {
                        return;
                    }

                    this.shellstream = this.client.CreateShellStream("BlazorPlusShell", 80, 24, 800, 600, 4096);
                }
                catch (Exception x)
                {
                    if (guid != this.client_guid)
                    {
                        return;
                    }

                    bses.InvokeInRenderThread(delegate
                    {
                        bses.Alert("Error", x.ToString());
                    });
                    this.client.Dispose();
                    return;
                }

                if (guid != this.client_guid)
                {
                    return;
                }

                this.notifylineready = null;
                this.linequeue.Clear();

                this.viewmode = "connected";
                bses.InvokeInRenderThread(delegate
                {
                    bses.Toast("Connected");

                    BlazorSession.Current.Browser.SetMemoryItem("console_server", this.server);
                    BlazorSession.Current.Browser.SetMemoryItem("console_username", this.username);
                    BlazorSession.Current.Browser.SetMemoryItem("console_password", this.password);

                    this.StateHasChanged();
                });

                try
                {
                    this.WorkLoop(bses, guid);

                    bses.InvokeInRenderThread(delegate
                    {
                        bses.ConsoleWarn("WorkLoop END");
                    });
                }
                catch (Exception x)
                {
                    this.viewmode = "login";
                    bses.InvokeInRenderThread(delegate
                    {
                        BlazorSession.Current.Alert("Error", x.ToString());
                        this.StateHasChanged();
                    });
                }
                try
                {
                    if (this.client != null)
                    {
                        this.client.Dispose();
                    }
                }
                catch (Exception)
                {

                }
            });
            if (!threadok)
            {
                this.viewmode = "login";
                BlazorSession.Current.Alert("Error", "Try again later.");
                return;
            }
        }

        private void WorkLoop (BlazorSession bses, string guid)
        {
            this.client.ErrorOccurred += (sender, args) =>
            {
                this.viewmode = "login";
                bses.InvokeInRenderThread(delegate
                {
                    BlazorSession.Current.Alert("Error", args.Exception.ToString());
                    this.StateHasChanged();
                });
                this.client.Dispose();
            };

            using System.IO.StreamReader sr = new System.IO.StreamReader(this.shellstream);

            while (!this.isdisposed && this.client.IsConnected)
            {
                System.Threading.Thread.Sleep(10);
                //TODO:???

                if (guid != this.client_guid)
                {
                    return;
                }

                string line = sr.ReadLine();
                if (line == null)
                {
                    continue;
                }

                if (guid != this.client_guid)
                {
                    return;
                }

                lock (this.linequeue)
                {
                    this.linequeue.Enqueue(line);
                }

                this.notifylineready?.Invoke();
            }
        }

        private void ConnectingCancel ()
        {
            this.viewmode = "login";
            this.client_guid = Guid.NewGuid().ToString();
            this.client.Dispose();
        }

        private void BDTReady (BlazorDomTree bdt)
        {
            BlazorSession ses = BlazorSession.Current;

            PlusControl resultdiv = bdt.Root.Create("div style='margin:8px 0;max-width:100%;height:400px;background-color:black;color:white;overflow-y:scroll;padding:5px 5px 15px;'");

            PlusControl div1 = bdt.Root.Create("div style='display:flex;'");
            // div1.Create("label style='width:100px").InnerText("Command:");
            PlusControl inpword = div1.Create("input type='text' style='flex:99;padding:0 5px'");
            PlusControl button = div1.Create("button").InnerText("Send");
            div1.Create("button").InnerText("^C").OnClick(delegate
            {
                this.shellstream.WriteByte(3);
                this.shellstream.Flush();
            });

            string _lastcolor = null;
            PlusControl SetLastColor (PlusControl span)
            {
                if (_lastcolor != null)
                {
                    span.Style("color", _lastcolor);
                    if (_lastcolor == "black")
                    {
                        span.Style("background-color", "white");
                    }
                }
                return span;
            }

            void SendLine (string line, string color)
            {
                ses.ConsoleLog("line", System.Text.Json.JsonSerializer.Serialize(line));

                var d = resultdiv.Create("div style='color:" + color + "'");
                if (string.IsNullOrEmpty(line))
                {
                    d.InnerHTML("<br/>");
                    return;
                }

                if (line.IndexOf("\x1b[") == -1)
                {
                    if (color == null)
                    {
                        SetLastColor(d);
                    }

                    d.InnerText(line);
                    return;
                }

                //parse ssh color:
                int pos = 0;
                while (pos < line.Length)
                {
                    int p33 = line.SafeIndexOf("\x1b[", pos);
                    if (p33 == -1)
                    {
                        string laststr = line[pos..];
                        if (!string.IsNullOrEmpty(laststr))
                        {
                            //ses.ConsoleLog("last", System.Text.Json.JsonSerializer.Serialize(laststr));
                            SetLastColor(d.Create("span")).InnerText(laststr);
                        }
                        break;
                    }

                    ses.ConsoleLog("p33-a", p33);

                    if (line[p33] != '\x1b')    //a bug for raspbian - dotnet-linux-arm
                    {
                        ses.ConsoleWarn("invalid IndexOf implementation");
                        if (line[p33 - 1] == '\x1b')
                        {
                            p33--;
                        }
                    }

                    if (p33 > pos)
                    {
                        //ses.ConsoleLog("normal", System.Text.Json.JsonSerializer.Serialize(line.Substring(pos, p33 - pos)));
                        SetLastColor(d.Create("span")).InnerText(line[pos..p33]);
                    }

                    pos = p33 + 2;
                    if (pos >= line.Length)
                    {
                        break;
                    }

                    p33 = line.SafeIndexOf("\x1b[K", pos);    //?
                    if (p33 == -1)
                    {
                        //invalid
                        _lastcolor = "red";
                        SetLastColor(d.Create("span")).InnerText(line[pos..]);
                        break;
                    }

                    ses.ConsoleLog("p33-b", p33);

                    if (line[p33] != '\x1b')    //a bug for raspbian - dotnet-linux-arm
                    {
                        ses.ConsoleWarn("invalid IndexOf implementation");
                        if (line[p33 - 1] == '\x1b')
                        {
                            p33--;
                        }
                    }

                    string controlcode = line[pos..p33];
                    //d.Create("b").Style("color", "cyan").InnerText(controlcode);
                    //ses.ConsoleLog("control", System.Text.Json.JsonSerializer.Serialize(controlcode));

                    if (controlcode == "m")
                    {
                        _lastcolor = null;
                    }
                    else if (controlcode.StartsWith("01;"))
                    {
                        _lastcolor = GetColor(controlcode[3..]);
                    }

                    pos = p33 + 3;
                }
            }

            void ScrollBottom () => resultdiv.Eval("this.scrollTop=this.scrollHeight");

            inpword.SetFocus(99);

            this.notifylineready = delegate ()
            {
                ses.InvokeInRenderThread(delegate
                {
                    List<string> list = new List<string>();
                    lock (this.linequeue)
                    {
                        list.AddRange(this.linequeue);
                        this.linequeue.Clear();
                    }
                    foreach (string line in list)
                    {
                        SendLine(line, null);
                    }
                    ScrollBottom();
                });
            };

            void SendCommand ()
            {
                if (inpword.Attribute_Disabled)
                {
                    return;
                }

                string cmdline = inpword.Value.Trim();

                inpword.Value = "";

                resultdiv.Create("div style='margin:1px;border-top:solid 1px white'");
                //SendLine(cmdline, "yellow");

                ScrollBottom();

                inpword.Attribute_Disabled(true);
                button.Attribute_Disabled(true);

                System.Threading.Thread t = new System.Threading.Thread(delegate ()
                {
                    try
                    {
                        this.shellstream.WriteLine(cmdline);
                    }
                    catch (Exception x)
                    {
                        ses.InvokeInRenderThread(delegate
                        {
                            SendLine(x.ToString(), "red");
                        });
                    }
                    ses.InvokeInRenderThread(delegate
                    {
                        inpword.Attribute_Disabled(false);
                        button.Attribute_Disabled(false);
                        ScrollBottom();
                        inpword.SetFocus();

                        this.StateHasChanged();
                    });
                });
                t.Start();
            }

            inpword.OnEnterKey(delegate { SendCommand(); });
            button.OnClick(delegate { SendCommand(); });

            PlusControl div2 = bdt.Root.Create("div style='display:flex;'");

            List<string> cmds = new List<string>
            {
                "ls",
                "ps -ef|grep dotnet"
            };

            foreach (string cmd in cmds)
            {
                div2.Create("button").InnerText(cmd).OnClick(delegate
                {
                    resultdiv.Create("div style='margin:1px;border-top:solid 1px white'");
                    this.shellstream.WriteLine(cmd);
                    this.shellstream.Flush();
                });
            }
        }

        private static string GetColor (string code) => code switch
        {
            "30m" => "black",
            "31m" => "red",
            "32m" => "green",
            "33m" => "yellow",
            "34m" => "blue",
            "35m" => "purple",
            "36m" => "skyblue",
            "37m" => "white",
            _ => null,
        };
    }
}
