namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    internal class TcpMapClientSession : TcpMapBaseSession
    {
        public TcpMapClient Client { get; set; }
        public string SessionId { get; set; }

        public bool IsConnected { get; set; }

        private Socket _sock_local;   //Client
        private Socket _sock_server; //Server
        private CancellationTokenSource _cts_connect;
        private CancellationTokenSource _cts_upgrade;
        private Stream _sread, _swrite;
        private DateTime _lastwritetime = DateTime.Now;

        public TcpMapClientSession (TcpMapClient client, string sid)
        {
            this.Client = client;
            this.SessionId = sid;
        }

        public async Task StartAsync ()
        {
            this._sock_local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            this._sock_local.InitTcp();
            await this._sock_local.ConnectWithTimeoutAsync(this.Client.ClientHost, this.Client.ClientPort, 12000);
            _ = this.WorkAsync();
        }

        public async Task<string> WaitUpgradeAsync ()
        {
            // _peer is NULL
            _ = this.WorkAsync();

            this._cts_upgrade = new CancellationTokenSource();
            while (this._cts_connect != null && !this._cts_connect.IsCancellationRequested)
            {
                if (!string.IsNullOrEmpty(this.SessionId))
                {
                    break;
                }

                if (await this._cts_upgrade.Token.WaitForSignalSettedAsync(9000))
                {
                    break;
                }
            }
            return this.SessionId;
        }

        public void Close ()
        {
            this._cts_connect?.Cancel();
            this._cts_upgrade?.Cancel();
            this._sock_local?.CloseSocket();
            this._sock_server?.CloseSocket();
        }

        private async Task WorkAsync ()
        {
            this.IsStarted = true;
            this.IsConnected = false;
            //LogMessage("ClientSession WorkAsync start");
            int connectedTimes = 0;
            try
            {
                int againTimeout = 125;
            StartAgain:
                this._sock_server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this._sock_server.InitTcp();
                this._cts_connect = new CancellationTokenSource();
                try
                {
                    await this._sock_server.ConnectAsync(this.Client.ServerHost, 6022);

                    //LogMessage("connected to 6022");
                }
                catch (Exception x)
                {
                    this.OnError(x);
                    this._sock_server.CloseSocket();
                    this._sock_server = null;
                    this._cts_connect = new CancellationTokenSource();
                    if (!this.IsStarted)
                    {
                        return;
                    }

                    if (againTimeout < 4)
                    {
                        againTimeout *= 2;
                    }

                    if (await this._cts_connect.Token.WaitForSignalSettedAsync(againTimeout))
                    {
                        return;
                    }

                    goto StartAgain;
                }

                try
                {
                    bool supportEncrypt = this.Client.UseEncrypt;
                    byte[] clientKeyIV;

                    {
                        CommandMessage connmsg = new CommandMessage
                        {
                            Name = "SessionConnect"
                        };
                        List<string> arglist = new List<string>
                        {
                            this.Client.License.Key,
                            this.Client.ServerPort.ToString()
                        };
                        this.Client.License.GenerateSecureKeyAndHash(out clientKeyIV, out byte[] encryptedKeyIV, out byte[] sourceHash);
                        arglist.Add(Convert.ToBase64String(encryptedKeyIV));
                        arglist.Add(Convert.ToBase64String(sourceHash));
                        arglist.Add(supportEncrypt ? "1" : "0");
                        arglist.Add(this.SessionId);//sessionid at [5]
                        connmsg.Args = arglist.ToArray();

                        await this._sock_server.SendAsync(connmsg.Pack(), SocketFlags.None);

                        //LogMessage("wait for conn msg");

                        connmsg = await CommandMessage.ReadFromSocketAsync(this._sock_server);

                        if (connmsg == null)
                        {
                            TcpMapService.LogMessage("no message ? Connected:" + this._sock_server.Connected);
                            return;
                        }

                        //LogMessage("connmsg : " + connmsg.Name + " : " + string.Join(",", connmsg.Args));

                        if (connmsg.Name != "ConnectOK")
                        {
                            this.IsStarted = false;//don't go until start it again.
                            throw new Exception(connmsg.Name + " : " + string.Join(",", connmsg.Args));
                        }

                        if (supportEncrypt && connmsg.Args[1] == "0")
                        {
                            supportEncrypt = false; this.LogMessage("Warning:server don't support encryption.");
                        }

                    }

                    this.IsConnected = true;

                    connectedTimes++;
                    //LogMessage("ConnectOK #" + connectedTimes);

                    if (supportEncrypt)
                    {
                        this.Client.License.OverrideStream(this._sock_server.CreateStream(), clientKeyIV, out this._sread, out this._swrite);
                    }
                    else
                    {
                        this._sread = this._swrite = this._sock_server.CreateStream();
                    }

                    if (string.IsNullOrEmpty(this.SessionId))
                    {
                        //wait for Upgrade
                        while (this.SessionId == null)
                        {
                            CommandMessage msg;
                            var cts = new CancellationTokenSource();
                            _ = Task.Run(async delegate
                            {
                                if (await cts.Token.WaitForSignalSettedAsync(16000))
                                {
                                    return;
                                }

                                await this._swrite.WriteAsync(new CommandMessage("_ping_", "forread").Pack());
                            });
                            try
                            {
                                msg = await CommandMessage.ReadFromStreamAsync(this._sread);
                            }
                            finally
                            {
                                cts.Cancel();
                            }

                            if (msg == null)
                            {
                                throw (new SocketException(995));
                            }

                            switch (msg.Name)
                            {
                                case "UpgradeSession":

                                    try
                                    {
                                        this._sock_local = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                                        this._sock_local.InitTcp();
                                        await this._sock_local.ConnectWithTimeoutAsync(this.Client.ClientHost, this.Client.ClientPort, 12000);
                                    }
                                    catch (Exception x)
                                    {
                                        TcpMapService.OnError(x);
                                        await this._swrite.WriteAsync(new CommandMessage("UpgradeSessionResult", "Failed").Pack());
                                        continue;
                                    }
                                    this.SessionId = msg.Args[0];
                                    if (this._cts_upgrade != null)
                                    {
                                        this._cts_upgrade.Cancel();
                                    }

                                    await this._swrite.WriteAsync(new CommandMessage("UpgradeSessionResult", "OK").Pack());
                                    break;
                                case "_ping_":
                                    await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                    break;
                                case "_ping_result_":
                                    break;
                                default:
                                    this.LogMessage("Error: 1 Ignore message " + msg);
                                    break;
                            }
                        }
                    }

                    await this.WorkAsync(this._sock_local.CreateStream());

                }
                catch (Exception x)
                {
                    this.OnError(x);
                }

            }
            catch (SocketException)
            {
            }
            catch (Exception x)
            {
                this.OnError(x);
            }

            this.IsStarted = false;
            this.IsConnected = false;
            this._sock_server?.CloseSocket();
            this._sock_local?.CloseSocket();
            this._workend = true;
        }

        private bool _workend = false;

        internal bool ShallRecycle () => this._workend;

        protected override async Task<CommandMessage> ReadMessageAsync ()
        {
        ReadAgain:
            CommandMessage msg;
            CancellationTokenSource cts = null;
            if (DateTime.Now - this._lastwritetime > TimeSpan.FromMilliseconds(12000))
            {
                this._lastwritetime = DateTime.Now;
                await this._swrite.WriteAsync(new CommandMessage("_ping_", "forwrite").Pack());
            }
            else
            {
                cts = new CancellationTokenSource();
                _ = Task.Run(async delegate
                {
                    if (await cts.Token.WaitForSignalSettedAsync(16000))
                    {
                        return;
                    }

                    this._lastwritetime = DateTime.Now;
                    await this._swrite.WriteAsync(new CommandMessage("_ping_", "forread").Pack());
                });
            }
            try
            {
                msg = await CommandMessage.ReadFromStreamAsync(this._sread);
            }
            finally
            {
                if (cts != null)
                {
                    cts.Cancel();
                }
            }

            if (msg == null || msg.Name == "data")
            {
                return msg;
            }

            //TcpMapService.LogMessage("ClientSession:get message " + msg);

            switch (msg.Name)
            {
                case "_ping_":
                    await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                    break;
                case "_ping_result_":
                    break;
                default:
                    TcpMapService.LogMessage("Error: 2 Ignore message " + msg);
                    break;
            }
            goto ReadAgain;
        }

        protected override async Task WriteMessageAsync (CommandMessage msg)
        {
            this._lastwritetime = DateTime.Now;
            await this._swrite.WriteAsync(msg.Pack());
        }
    }
}
