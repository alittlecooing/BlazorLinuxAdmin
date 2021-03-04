namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Extensions.Caching.Memory;

    public class TcpMapServerWorker : TcpMapBaseWorker
    {
        public TcpMapServer Server { get; set; }

        public bool IsListened { get; private set; }

        private TcpListener listener;
        private CancellationTokenSource cts;

        public void StartWork ()
        {
            if (!this.Server.IsValidated || this.Server.IsDisabled)
            {
                return;
            }

            if (this.IsStarted)
            {
                return;
            }

            this.IsStarted = true;
            _ = this.WorkAsync();
        }

        private async Task WorkAsync ()
        {
            this.IsListened = false;
            this.LogMessage("ServerWorker WorkAsync start");
            try
            {
                int againTimeout = 500;
            StartAgain:
                this.listener = new TcpListener(IPAddress.Parse(this.Server.ServerBind), this.Server.ServerPort);
                try
                {
                    this.listener.Start();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                    this.listener = null;
                    this.cts = new CancellationTokenSource();
                    if (!this.IsStarted)
                    {
                        return;
                    }

                    if (againTimeout < 4)
                    {
                        againTimeout *= 2;
                    }

                    if (await this.cts.Token.WaitForSignalSettedAsync(againTimeout))
                    {
                        return;
                    }

                    goto StartAgain;
                }
                this.IsListened = true;
                while (this.IsStarted)
                {
                    var socket = await this.listener.AcceptSocketAsync();

                    this.LogMessage("Warning:accept socket " + socket.LocalEndPoint + "," + socket.RemoteEndPoint + " at " + DateTime.Now.ToString("HH:mm:ss.fff"));

                    _ = Task.Run(async delegate
                    {
                        try
                        {
                            bool allowThisSocket = true;
                            if (!string.IsNullOrEmpty(this.Server.IPServiceUrl))
                            {
                                using HttpClient hc = new HttpClient();
                                string queryurl = this.Server.IPServiceUrl + ((IPEndPoint)socket.RemoteEndPoint).Address;
                                string res = await hc.GetStringAsync(queryurl);
                                this.LogMessage(res + " - " + queryurl);
                                if (res.StartsWith("NO:"))
                                {
                                    allowThisSocket = false;
                                }
                            }

                            if (allowThisSocket)
                            {
                                socket.InitTcp();
                                await this.ProcessSocketAsync(socket);
                            }
                        }
                        catch (Exception x)
                        {
                            this.OnError(x);
                        }
                        finally
                        {
                            this.LogMessage("Warning:close socket " + socket.LocalEndPoint + "," + socket.RemoteEndPoint + " at " + DateTime.Now.ToString("HH:mm:ss.fff"));
                            socket.CloseSocket();
                        }
                    });
                }
            }
            catch (Exception x)
            {
                if (this.IsStarted)
                {
                    this.OnError(x);
                }
            }

            this.LogMessage("WorkAsync end");

            this.IsStarted = false;
            this.IsListened = false;

            if (this.listener != null)
            {
                try
                {
                    this.listener.Stop();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
                this.listener = null;
            }
        }

        private async Task ProcessSocketAsync (Socket socket)
        {
            //LogMessage("new server conn : " + socket.LocalEndPoint + " , " + socket.RemoteEndPoint);

            string sessionid = Guid.NewGuid().ToString();

            int tryagainIndex = 0;

        TryAgain:

            if (!socket.Connected)  //this property is not OK .. actually the client has disconnect 
            {
                return;
            }

            tryagainIndex++;

            if (tryagainIndex > 3)  //only try 3 times.
            {
                return;
            }

            TcpMapServerClient sclient = this.FindClient();

            if (sclient == null)
            {
                if (DateTime.Now - this.lastDisconnectTime < TimeSpan.FromSeconds(8))//TODO:const connect wait sclient timeout
                {
                    await Task.Delay(500);
                    goto TryAgain;
                }
                throw new Exception("no sclient.");
            }

            TcpMapServerClient presession = null;

            try
            {

                lock (this.presessions)
                {
                    if (this.presessions.Count != 0)
                    {
                        presession = this.presessions[0];
                        this.presessions.RemoveAt(0);
                    }
                }

                if (presession != null)
                {
                    try
                    {
                        await presession.UpgradeSessionAsync(sessionid);
                    }
                    catch (Exception x)
                    {
                        this.OnError(x);
                        this.LogMessage("Error:ServerWorker presession upgrade failed @" + tryagainIndex + " , " + sessionid);
                        goto TryAgain;
                    }
                    lock (this.sessions)
                    {
                        this.sessions.Add(presession);
                    }

                    this.LogMessage("ServerWorker session upgraded @" + tryagainIndex + " , " + sessionid);
                }
                else
                {
                    await sclient.StartSessionAsync(sessionid);

                    this.LogMessage("ServerWorker session created @" + tryagainIndex + " , " + sessionid);
                }
            }
            catch (Exception x)
            {
                this.OnError(x);
                await Task.Delay(500);//TODO:const...
                goto TryAgain;
            }

            try
            {
                TcpMapServerSession session = new TcpMapServerSession(socket.CreateStream(), sessionid);
                this.sessionMap.TryAdd(sessionid, session);
                this.LogMessage("Warning: TcpMapServerSession added:" + sessionid);
                try
                {
                    if (presession != null)
                    {
                        presession.AttachToSession(session);
                    }

                    await session.WorkAsync();
                }
                finally
                {
                    this.sessionMap.TryRemove(sessionid, out _);
                    this.LogMessage("Warning: TcpMapServerSession removed:" + sessionid);
                }
            }
            catch (Exception x)
            {
                this.OnError(x);
            }

            await sclient.CloseSessionAsync(sessionid);
            //LogMessage("ServerWorker session closed @" + tryagainIndex);
        }

        internal TcpMapServerClient FindClient ()
        {
        TryAgain:
            TcpMapServerClient sclient = null;
            lock (this.clients)
            {
                if (this.clients.Count == 1)
                {
                    sclient = this.clients[0];
                }
                else if (this.clients.Count != 0)
                {
                    sclient = this.clients[Interlocked.Increment(ref this.nextclientindex) % this.clients.Count];

                    if (DateTime.Now - sclient.lastPingTime > TimeSpan.FromSeconds(90))
                    {
                        //TODO:maybe the client socket has timeout
                        sclient.Stop();
                        //RemoveClientOrSession(sclient);
                        goto TryAgain;
                    }
                }
                else
                {
                    // no client
                }
            }

            return sclient;
        }

        internal ConcurrentDictionary<string, TcpMapServerSession> sessionMap = new ConcurrentDictionary<string, TcpMapServerSession>();

        public void Stop ()
        {
            if (!this.IsStarted)
            {
                return;
            }

            this.IsStarted = false;
            if (this.listener != null)
            {
                var lis = this.listener;
                this.listener = null;
                lis.Stop();
            }
            if (this.cts != null)
            {
                this.cts.Cancel();
            }
            //close all clients/sessions
            lock (this.clients)
            {
                foreach (var item in this.clients.ToArray())
                {
                    item.Stop();
                }
            }

            lock (this.sessions)
            {
                foreach (var item in this.sessions.ToArray())
                {
                    item.Stop();
                }
            }
        }

        private int nextclientindex = 0;
        internal List<TcpMapServerClient> clients = new List<TcpMapServerClient>();
        internal List<TcpMapServerClient> sessions = new List<TcpMapServerClient>();
        internal List<TcpMapServerClient> presessions = new List<TcpMapServerClient>();
        private DateTime lastDisconnectTime;

        internal void AddClientOrSession (TcpMapServerClient client)
        {
            var list = client.is_client ? this.clients : (client.sessionId != null ? this.sessions : this.presessions);
            lock (list)
            {
                list.Add(client);
            }

            if (list.Count > 1)
            {
                //TODO:shall test alive for other clients
                //When client switch IP , the socket will not timeout for read
                List<TcpMapServerClient> others = new List<TcpMapServerClient>();
                lock (list)
                {
                    others = new List<TcpMapServerClient>(list);
                }
                foreach (var other in others)
                {
                    if (other == client)
                    {
                        continue;
                    }

                    other.TestAlive();
                }
            }
        }

        internal void RemoveClientOrSession (TcpMapServerClient client)
        {
            this.lastDisconnectTime = DateTime.Now;
            var list = client.is_client ? this.clients : (client.sessionId != null ? this.sessions : this.presessions);
            lock (list)
            {
                list.Remove(client);
            }
        }
    }

    public class TcpMapServerConnector
    {
        private readonly TcpMapServerWorker worker;
        public TcpMapServerConnector (TcpMapServerWorker sworker) => this.worker = sworker;

        internal async Task AcceptConnectorAndWorkAsync (Socket clientSock, CommandMessage connmsg)
        {
            bool supportEncrypt = this.worker.Server.UseEncrypt;
            if (connmsg.Args[4] == "0")
            {
                supportEncrypt = false;
            }

            byte[] clientKeyIV;
            try
            {
                this.worker.Server.ConnectorLicense.DescriptSourceKey(Convert.FromBase64String(connmsg.Args[2]), Convert.FromBase64String(connmsg.Args[3]), out clientKeyIV);
            }
            catch (Exception x)
            {
                this.worker.OnError(x);
                var failedmsg = new CommandMessage("ConnectFailed", "InvalidSecureKey");
                await clientSock.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            TcpMapServerClient sclient = this.worker.FindClient();
            if (sclient == null)
            {
                var failedmsg = new CommandMessage("ConnectFailed", "NoClient");
                await clientSock.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            string mode = connmsg.Args[5];
            string connArgument = connmsg.Args[6];
            if (mode == "USB")//use server bandwidth
            {
                var resmsg = new CommandMessage("ConnectOK", "ConnectOK", supportEncrypt ? "1" : "0");
                await clientSock.SendAsync(resmsg.Pack(), SocketFlags.None);

                Stream _sread, _swrite;
                if (supportEncrypt)
                {
                    this.worker.Server.ConnectorLicense.OverrideStream(clientSock.CreateStream(), clientKeyIV, out _sread, out _swrite);
                }
                else
                {
                    _sread = _swrite = clientSock.CreateStream();
                }

                using Socket localsock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                string ip = this.worker.Server.ServerBind;
                if (ip == "0.0.0.0")
                {
                    ip = "127.0.0.1";
                }

                localsock.InitTcp();
                await localsock.ConnectAsync(ip, this.worker.Server.ServerPort);

                TcpMapConnectorSession session = new TcpMapConnectorSession(new SimpleSocketStream(localsock));
                await session.DirectWorkAsync(_sread, _swrite);
            }
            else if (mode == "UDP")
            {
                if (_udpcache == null)
                {
                    lock (typeof(TcpMapServerConnector))
                    {
                        if (_udpcache == null)
                        {
                            var opt = new MemoryCacheOptions();
                            Microsoft.Extensions.Options.IOptions<MemoryCacheOptions> iopt = Microsoft.Extensions.Options.Options.Create(opt);
                            _udpcache = new MemoryCache(iopt);
                        }

                    }
                }

                string key = connArgument + ":" + sclient.sessionId;
                if (!_udpcache.TryGetValue(key, out UdpInfoItem natinfo) || natinfo.HasExpired())
                {
                    var udpmsg = await sclient.CreateUDPNatAsync(connArgument);
                    string[] pair = udpmsg.Args[1].Split(':');
                    string addr = pair[0];
                    int port = int.Parse(pair[1]);
                    natinfo = new UdpInfoItem(addr + ":" + port);
                    _udpcache.Set(key, natinfo);
                }

                var resmsg = new CommandMessage("ConnectOK", "ConnectOK", supportEncrypt ? "1" : "0", natinfo.NatInfo);
                await clientSock.SendAsync(resmsg.Pack(), SocketFlags.None);

                resmsg = await CommandMessage.ReadFromSocketAsync(clientSock);
                if (resmsg == null)
                {
                    return;
                }

                throw new NotImplementedException("work for " + resmsg);
            }
            else if (mode == "RCP")
            {

                var resmsg = new CommandMessage("ConnectOK", "ConnectOK", supportEncrypt ? "1" : "0"
                    , ((IPEndPoint)sclient.socket.RemoteEndPoint).Address.ToString(), sclient.optionRouterClientPort.ToString());
                await clientSock.SendAsync(resmsg.Pack(), SocketFlags.None);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        private class UdpInfoItem
        {
            public UdpInfoItem (string natinfo) => this.NatInfo = natinfo;

            public string NatInfo { get; private set; }

            private readonly DateTime dTStart = DateTime.Now;
            public bool HasExpired () => DateTime.Now - this.dTStart > TimeSpan.FromSeconds(9);
        }

        //TODO:use application level global cache..
        private static MemoryCache _udpcache;
    }

    public class TcpMapServerClient
    {
        private TcpMapServerClient ()
        {
        }

        internal int optionRouterClientPort;
        private static long _nextscid = 30001;
        private readonly long scid = Interlocked.Increment(ref _nextscid);
        private TcpMapServerWorker worker = null;
        private Stream sread, swrite;
        internal Socket socket;

        internal bool is_client = true;
        internal bool is_session = false;

        internal DateTime lastPingTime = DateTime.Now;

        public string sessionId = null;
        private CancellationTokenSource cts_wait_upgrade;

        internal async Task UpgradeSessionAsync (string newsid)
        {
            this.sessionId = newsid;
            await this.swrite.WriteAsync(new CommandMessage("UpgradeSession", newsid).Pack());
        ReadAgain:
            var res = await CommandMessage.ReadFromStreamAsync(this.sread);
            if (res == null)
            {
                throw (new Exception("Invalid null message "));
            }

            if (res.Name == "_ping_" || res.Name == "_ping_result_")
            {
                goto ReadAgain;
            }

            if (res.Name != "UpgradeSessionResult")
            {
                throw (new Exception("Invalid message : " + res));
            }

            if (res.Args[0] == "OK")
            {
                return;
            }

            throw (new Exception("Upgrade Session Failed : " + res));
        }

        public static async Task AcceptConnectAndWorkAsync (Socket socket, CommandMessage connmsg)
        {
            System.Diagnostics.Debug.Assert(connmsg.Name == "ClientConnect" || connmsg.Name == "SessionConnect");

            TcpMapServerClient client = new TcpMapServerClient
            {
                socket = socket
            };
            await client.WorkAsync(connmsg);
        }

        private async Task WorkAsync (CommandMessage connmsg)
        {
            if (connmsg.Name == "SessionConnect")
            {
                this.is_client = false;
                this.is_session = true;
            }

            byte[] clientKeyIV;

            this.worker = TcpMapService.FindServerWorkerByKey(connmsg.Args[0], int.Parse(connmsg.Args[1]));

            string failedreason = null;

            if (this.worker == null)
            {
                failedreason = "NotFound";
            }
            else if (!this.worker.Server.IsValidated)
            {
                failedreason = "NotValidated";
            }
            else if (this.worker.Server.IsDisabled)
            {
                failedreason = "NotEnabled";
            }

            if (this.worker == null || !string.IsNullOrEmpty(failedreason))
            {
                var failedmsg = new CommandMessage("ConnectFailed", failedreason);
                await this.socket.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            bool supportEncrypt = this.worker.Server.UseEncrypt;
            if (connmsg.Args[4] == "0")
            {
                supportEncrypt = false;
            }

            try
            {
                this.worker.Server.License.DescriptSourceKey(Convert.FromBase64String(connmsg.Args[2]), Convert.FromBase64String(connmsg.Args[3]), out clientKeyIV);
            }
            catch (Exception x)
            {
                this.worker.OnError(x);
                var failedmsg = new CommandMessage("ConnectFailed", "InvalidSecureKey");
                await this.socket.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            var successMsg = new CommandMessage("ConnectOK", "ConnectOK", supportEncrypt ? "1" : "0");
            await this.socket.SendAsync(successMsg.Pack(), SocketFlags.None);

            if (supportEncrypt)
            {
                this.worker.Server.License.OverrideStream(this.socket.CreateStream(), clientKeyIV, out this.sread, out this.swrite);
            }
            else
            {
                this.sread = this.swrite = this.socket.CreateStream();
            }

            if (this.is_session)
            {
                this.sessionId = connmsg.Args[5];
                if (this.sessionId == null)
                {
                    this.cts_wait_upgrade = new CancellationTokenSource();
                }
            }

            this.worker.AddClientOrSession(this);
            try
            {
                if (this.is_client)
                {

                    _ = this.swrite.WriteAsync(new CommandMessage("SetOption", "ClientEndPoint", this.socket.RemoteEndPoint.ToString()).Pack());

                    while (true)
                    {
                        var msg = await CommandMessage.ReadFromStreamAsync(this.sread);
                        //process it...

                        if (msg == null)
                        {
                            //TcpMapService.LogMessage("no message ? Connected:" + _socket.Connected);
                            throw new SocketException(995);
                        }

                        //_worker.LogMessage("TcpMapServerClient get msg " + msg);

                        switch (msg.Name)
                        {
                            case "SetOption":
                                string optvalue = msg.Args[1];
                                switch (msg.Args[0])
                                {
                                    case "RouterClientPort":
                                        this.optionRouterClientPort = int.Parse(optvalue);
                                        break;
                                    default:
                                        this.worker.LogMessage("Error:Ignore option " + msg);
                                        break;
                                }
                                break;
                            case "StartSessionResult":
                            case "CloseSessionResult":
                            case "CreateUDPNatResult":
                                long reqid = long.Parse(msg.Args[0]);
                                if (this.reqmap.TryGetValue(reqid, out var ritem))
                                {
                                    ritem.response = msg;
                                    ritem.cts.Cancel();
                                }
                                else
                                {
                                    this.worker.LogMessage("Request Expired : " + msg);
                                }
                                break;
                            case "_ping_":
                                this.lastPingTime = DateTime.Now;
                                await this.swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                break;
                            case "_ping_result_":
                                break;
                            default:
                                this.worker.LogMessage("Error: 5 Ignore message " + msg);
                                break;
                        }
                    }
                }
                else if (this.is_session)
                {
                    if (this.sessionId == null)
                    {
                        this.worker.LogMessage($"Warning:ServerClient*{this.scid} Wait for Upgrade To Session ");

                        while (this.sessionId == null)
                        {
                            if (await this.cts_wait_upgrade.Token.WaitForSignalSettedAsync(28000))  //check the presession closed or not every 28 seconds
                            {
                                if (this.sessionId != null)
                                {
                                    break;  //OK, session attached.
                                }

                                throw new SocketException(995); //_cts_wait_upgrade Cancelled , by SessionId is not seted
                            }

                            //if (!_socket.Connected) //NEVER useful..
                            //{
                            //	_worker.LogMessage("Warning:presession exit.");
                            //	throw new SocketException(995);
                            //}

                            if (!this.socket.Poll(0, SelectMode.SelectRead)) //WORKS..
                            {
                                continue;
                            }

                            if (this.socket.Available == 0)
                            {
                                this.worker.LogMessage("Warning:presession exit!");
                                throw new SocketException(995);
                            }

                            //_worker.LogMessage("Warning:presession send message before upgrade ?"+_socket.Available);
                            if (!this.cts_wait_upgrade.IsCancellationRequested)
                            {
                                //TODO:not locked/sync, not so safe it the presession is upgrading
                                var msg = await CommandMessage.ReadFromSocketAsync(this.socket);
                                if (msg.Name == "_ping_")
                                {
                                    byte[] resp = new CommandMessage("_ping_result_").Pack();
                                    await this.socket.SendAsync(resp, SocketFlags.None);
                                }
                                else
                                {
                                    this.worker.LogMessage("Warning:presession unexpected msg : " + msg);
                                }
                            }
                        }

                        this.worker.LogMessage($"Warning:ServerClient*{this.scid} SessionId:" + this.sessionId);
                    }

                    int waitMapTimes = 0;

                TryGetMap:
                    if (this.attachedSession != null)
                    {
                        this.worker.LogMessage($"ServerClient*{this.scid} use attached Session : {this.sessionId} *{waitMapTimes}");
                        await this.attachedSession.UseThisSocketAsync(this.sread, this.swrite);
                    }
                    else if (this.worker.sessionMap.TryGetValue(this.sessionId, out var session))
                    {
                        this.worker.LogMessage($"ServerClient*{this.scid} session server ok : {this.sessionId} *{waitMapTimes}");
                        await session.UseThisSocketAsync(this.sread, this.swrite);
                    }
                    else
                    {
                        if (waitMapTimes < 5)
                        {
                            waitMapTimes++;
                            await Task.Delay(10);//wait sessionMap be added..
                            goto TryGetMap;
                        }

                        this.worker.LogMessage($"Warning:ServerClient*{this.scid} session not found : {this.sessionId}");
                        throw new Exception($"ServerClient*{this.scid} session not found : {this.sessionId}");
                    }
                }
                else
                {
                    throw new InvalidOperationException();
                }
            }
            catch (SocketException)
            {
                //no log
            }
            catch (Exception x)
            {
                this.worker.OnError(x);
            }
            finally
            {
                this.worker.RemoveClientOrSession(this);
            }

            this.worker.LogMessage($"ServerClient*{this.scid} WorkAsync END " + this.sessionId);
        }

        private TcpMapServerSession attachedSession;

        internal void AttachToSession (TcpMapServerSession session)
        {
            this.attachedSession = session;
            this.cts_wait_upgrade.Cancel();
        }

        internal void TestAlive () => _ = Task.Run(async delegate
                                      {
                                          try
                                          {
                                              await this.swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                          }
                                          catch (Exception x)
                                          {
                                              this.worker.OnError(x);
                                              this.Stop();
                                          }
                                      });

        internal void Stop ()
        {
            this.cts_wait_upgrade?.Cancel();
            if (this.socket != null)
            {
                try
                {
                    this.socket.CloseSocket();
                }
                catch (Exception x)
                {
                    TcpMapService.OnError(x);
                }
            }
        }

        private class RequestItem
        {
            internal CommandMessage response;
            internal CancellationTokenSource cts = new CancellationTokenSource();
        }

        private long nextreqid;
        private readonly ConcurrentDictionary<long, RequestItem> reqmap = new ConcurrentDictionary<long, RequestItem>();

        public async Task StartSessionAsync (string sid) => await this.SendCmdAsync("StartSession", sid);

        public async Task CloseSessionAsync (string sid) => await this.SendCmdAsync("CloseSession", sid);

        public async Task<CommandMessage> CreateUDPNatAsync (string natinfo) => await this.SendCmdAsync("CreateUDPNat", natinfo);

        private async Task<CommandMessage> SendCmdAsync (string cmd, string arg)
        {
            int timeout = 18000;
            long reqid = Interlocked.Increment(ref this.nextreqid);
            CommandMessage msg = new CommandMessage
            {
                Name = cmd,
                Args = new string[] { reqid.ToString(), arg, timeout.ToString() }
            };

            RequestItem ritem = new RequestItem();

            this.reqmap.TryAdd(reqid, ritem);
            try
            {
                this.worker.LogMessage("TcpMapServerClient sending #" + reqid + " : " + msg);

                await this.swrite.WriteAsync(msg.Pack());
                await this.swrite.FlushAsync();

                return !await ritem.cts.Token.WaitForSignalSettedAsync(timeout)
                    ? throw new Exception($"request timeout #{reqid} '{msg}'")
                    : ritem.response == null
                    ? throw new Exception($"No Response ? ")
                    : ritem.response.Args[1] == "Error" ? throw new Exception("Command Failed.") : ritem.response;
            }
            finally
            {
                this.reqmap.TryRemove(reqid, out _);
            }
        }
    }
}
