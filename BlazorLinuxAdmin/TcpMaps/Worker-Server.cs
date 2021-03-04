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

        private TcpListener _listener;
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
                this._listener = new TcpListener(IPAddress.Parse(this.Server.ServerBind), this.Server.ServerPort);
                try
                {
                    this._listener.Start();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                    this._listener = null;
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
                    var socket = await this._listener.AcceptSocketAsync();

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

            if (this._listener != null)
            {
                try
                {
                    this._listener.Stop();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
                this._listener = null;
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
                if (DateTime.Now - this._lastDisconnectTime < TimeSpan.FromSeconds(8))//TODO:const connect wait sclient timeout
                {
                    await Task.Delay(500);
                    goto TryAgain;
                }
                throw new Exception("no sclient.");
            }

            TcpMapServerClient presession = null;

            try
            {

                lock (this._presessions)
                {
                    if (this._presessions.Count != 0)
                    {
                        presession = this._presessions[0];
                        this._presessions.RemoveAt(0);
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
                    lock (this._sessions)
                    {
                        this._sessions.Add(presession);
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
            lock (this._clients)
            {
                if (this._clients.Count == 1)
                {
                    sclient = this._clients[0];
                }
                else if (this._clients.Count != 0)
                {
                    sclient = this._clients[Interlocked.Increment(ref this.nextclientindex) % this._clients.Count];

                    if (DateTime.Now - sclient._lastPingTime > TimeSpan.FromSeconds(90))
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
            if (this._listener != null)
            {
                var lis = this._listener;
                this._listener = null;
                lis.Stop();
            }
            if (this.cts != null)
            {
                this.cts.Cancel();
            }
            //close all clients/sessions
            lock (this._clients)
            {
                foreach (var item in this._clients.ToArray())
                {
                    item.Stop();
                }
            }

            lock (this._sessions)
            {
                foreach (var item in this._sessions.ToArray())
                {
                    item.Stop();
                }
            }
        }

        private int nextclientindex = 0;
        internal List<TcpMapServerClient> _clients = new List<TcpMapServerClient>();
        internal List<TcpMapServerClient> _sessions = new List<TcpMapServerClient>();
        internal List<TcpMapServerClient> _presessions = new List<TcpMapServerClient>();
        private DateTime _lastDisconnectTime;

        internal void AddClientOrSession (TcpMapServerClient client)
        {
            var list = client._is_client ? this._clients : (client.SessionId != null ? this._sessions : this._presessions);
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
            this._lastDisconnectTime = DateTime.Now;
            var list = client._is_client ? this._clients : (client.SessionId != null ? this._sessions : this._presessions);
            lock (list)
            {
                list.Remove(client);
            }
        }
    }

    public class TcpMapServerConnector
    {
        private readonly TcpMapServerWorker _worker;
        public TcpMapServerConnector (TcpMapServerWorker sworker) => this._worker = sworker;

        internal async Task AcceptConnectorAndWorkAsync (Socket clientSock, CommandMessage connmsg)
        {
            bool supportEncrypt = this._worker.Server.UseEncrypt;
            if (connmsg.Args[4] == "0")
            {
                supportEncrypt = false;
            }

            byte[] clientKeyIV;
            try
            {
                this._worker.Server.ConnectorLicense.DescriptSourceKey(Convert.FromBase64String(connmsg.Args[2]), Convert.FromBase64String(connmsg.Args[3]), out clientKeyIV);
            }
            catch (Exception x)
            {
                this._worker.OnError(x);
                var failedmsg = new CommandMessage("ConnectFailed", "InvalidSecureKey");
                await clientSock.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            TcpMapServerClient sclient = this._worker.FindClient();
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
                    this._worker.Server.ConnectorLicense.OverrideStream(clientSock.CreateStream(), clientKeyIV, out _sread, out _swrite);
                }
                else
                {
                    _sread = _swrite = clientSock.CreateStream();
                }

                using Socket localsock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                string ip = this._worker.Server.ServerBind;
                if (ip == "0.0.0.0")
                {
                    ip = "127.0.0.1";
                }

                localsock.InitTcp();
                await localsock.ConnectAsync(ip, this._worker.Server.ServerPort);

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

                string key = connArgument + ":" + sclient.SessionId;
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
                    , ((IPEndPoint)sclient._socket.RemoteEndPoint).Address.ToString(), sclient.OptionRouterClientPort.ToString());
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

            private readonly DateTime DTStart = DateTime.Now;
            public bool HasExpired () => DateTime.Now - this.DTStart > TimeSpan.FromSeconds(9);
        }

        //TODO:use application level global cache..
        private static MemoryCache _udpcache;
    }

    public class TcpMapServerClient
    {
        private TcpMapServerClient ()
        {
        }

        internal int OptionRouterClientPort;
        private static long _nextscid = 30001;
        private readonly long _scid = Interlocked.Increment(ref _nextscid);
        private TcpMapServerWorker _worker = null;
        private Stream _sread, _swrite;
        internal Socket _socket;

        internal bool _is_client = true;
        internal bool _is_session = false;

        internal DateTime _lastPingTime = DateTime.Now;

        public string SessionId = null;
        private CancellationTokenSource _cts_wait_upgrade;

        internal async Task UpgradeSessionAsync (string newsid)
        {
            this.SessionId = newsid;
            await this._swrite.WriteAsync(new CommandMessage("UpgradeSession", newsid).Pack());
        ReadAgain:
            var res = await CommandMessage.ReadFromStreamAsync(this._sread);
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
                _socket = socket
            };
            await client.WorkAsync(connmsg);
        }

        private async Task WorkAsync (CommandMessage connmsg)
        {
            if (connmsg.Name == "SessionConnect")
            {
                this._is_client = false;
                this._is_session = true;
            }

            byte[] clientKeyIV;

            this._worker = TcpMapService.FindServerWorkerByKey(connmsg.Args[0], int.Parse(connmsg.Args[1]));

            string failedreason = null;

            if (this._worker == null)
            {
                failedreason = "NotFound";
            }
            else if (!this._worker.Server.IsValidated)
            {
                failedreason = "NotValidated";
            }
            else if (this._worker.Server.IsDisabled)
            {
                failedreason = "NotEnabled";
            }

            if (this._worker == null || !string.IsNullOrEmpty(failedreason))
            {
                var failedmsg = new CommandMessage("ConnectFailed", failedreason);
                await this._socket.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            bool supportEncrypt = this._worker.Server.UseEncrypt;
            if (connmsg.Args[4] == "0")
            {
                supportEncrypt = false;
            }

            try
            {
                this._worker.Server.License.DescriptSourceKey(Convert.FromBase64String(connmsg.Args[2]), Convert.FromBase64String(connmsg.Args[3]), out clientKeyIV);
            }
            catch (Exception x)
            {
                this._worker.OnError(x);
                var failedmsg = new CommandMessage("ConnectFailed", "InvalidSecureKey");
                await this._socket.SendAsync(failedmsg.Pack(), SocketFlags.None);
                return;
            }

            var successMsg = new CommandMessage("ConnectOK", "ConnectOK", supportEncrypt ? "1" : "0");
            await this._socket.SendAsync(successMsg.Pack(), SocketFlags.None);

            if (supportEncrypt)
            {
                this._worker.Server.License.OverrideStream(this._socket.CreateStream(), clientKeyIV, out this._sread, out this._swrite);
            }
            else
            {
                this._sread = this._swrite = this._socket.CreateStream();
            }

            if (this._is_session)
            {
                this.SessionId = connmsg.Args[5];
                if (this.SessionId == null)
                {
                    this._cts_wait_upgrade = new CancellationTokenSource();
                }
            }

            this._worker.AddClientOrSession(this);
            try
            {
                if (this._is_client)
                {

                    _ = this._swrite.WriteAsync(new CommandMessage("SetOption", "ClientEndPoint", this._socket.RemoteEndPoint.ToString()).Pack());

                    while (true)
                    {
                        var msg = await CommandMessage.ReadFromStreamAsync(this._sread);
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
                                        this.OptionRouterClientPort = int.Parse(optvalue);
                                        break;
                                    default:
                                        this._worker.LogMessage("Error:Ignore option " + msg);
                                        break;
                                }
                                break;
                            case "StartSessionResult":
                            case "CloseSessionResult":
                            case "CreateUDPNatResult":
                                long reqid = long.Parse(msg.Args[0]);
                                if (this.reqmap.TryGetValue(reqid, out var ritem))
                                {
                                    ritem.Response = msg;
                                    ritem.cts.Cancel();
                                }
                                else
                                {
                                    this._worker.LogMessage("Request Expired : " + msg);
                                }
                                break;
                            case "_ping_":
                                this._lastPingTime = DateTime.Now;
                                await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                break;
                            case "_ping_result_":
                                break;
                            default:
                                this._worker.LogMessage("Error: 5 Ignore message " + msg);
                                break;
                        }
                    }
                }
                else if (this._is_session)
                {
                    if (this.SessionId == null)
                    {
                        this._worker.LogMessage($"Warning:ServerClient*{this._scid} Wait for Upgrade To Session ");

                        while (this.SessionId == null)
                        {
                            if (await this._cts_wait_upgrade.Token.WaitForSignalSettedAsync(28000))  //check the presession closed or not every 28 seconds
                            {
                                if (this.SessionId != null)
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

                            if (!this._socket.Poll(0, SelectMode.SelectRead)) //WORKS..
                            {
                                continue;
                            }

                            if (this._socket.Available == 0)
                            {
                                this._worker.LogMessage("Warning:presession exit!");
                                throw new SocketException(995);
                            }

                            //_worker.LogMessage("Warning:presession send message before upgrade ?"+_socket.Available);
                            if (!this._cts_wait_upgrade.IsCancellationRequested)
                            {
                                //TODO:not locked/sync, not so safe it the presession is upgrading
                                var msg = await CommandMessage.ReadFromSocketAsync(this._socket);
                                if (msg.Name == "_ping_")
                                {
                                    byte[] resp = new CommandMessage("_ping_result_").Pack();
                                    await this._socket.SendAsync(resp, SocketFlags.None);
                                }
                                else
                                {
                                    this._worker.LogMessage("Warning:presession unexpected msg : " + msg);
                                }
                            }
                        }

                        this._worker.LogMessage($"Warning:ServerClient*{this._scid} SessionId:" + this.SessionId);
                    }

                    int waitMapTimes = 0;

                TryGetMap:
                    if (this._attachedSession != null)
                    {
                        this._worker.LogMessage($"ServerClient*{this._scid} use attached Session : {this.SessionId} *{waitMapTimes}");
                        await this._attachedSession.UseThisSocketAsync(this._sread, this._swrite);
                    }
                    else if (this._worker.sessionMap.TryGetValue(this.SessionId, out var session))
                    {
                        this._worker.LogMessage($"ServerClient*{this._scid} session server ok : {this.SessionId} *{waitMapTimes}");
                        await session.UseThisSocketAsync(this._sread, this._swrite);
                    }
                    else
                    {
                        if (waitMapTimes < 5)
                        {
                            waitMapTimes++;
                            await Task.Delay(10);//wait sessionMap be added..
                            goto TryGetMap;
                        }

                        this._worker.LogMessage($"Warning:ServerClient*{this._scid} session not found : {this.SessionId}");
                        throw new Exception($"ServerClient*{this._scid} session not found : {this.SessionId}");
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
                this._worker.OnError(x);
            }
            finally
            {
                this._worker.RemoveClientOrSession(this);
            }

            this._worker.LogMessage($"ServerClient*{this._scid} WorkAsync END " + this.SessionId);
        }

        private TcpMapServerSession _attachedSession;

        internal void AttachToSession (TcpMapServerSession session)
        {
            this._attachedSession = session;
            this._cts_wait_upgrade.Cancel();
        }

        internal void TestAlive () => _ = Task.Run(async delegate
                                      {
                                          try
                                          {
                                              await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                          }
                                          catch (Exception x)
                                          {
                                              this._worker.OnError(x);
                                              this.Stop();
                                          }
                                      });

        internal void Stop ()
        {
            this._cts_wait_upgrade?.Cancel();
            if (this._socket != null)
            {
                try
                {
                    this._socket.CloseSocket();
                }
                catch (Exception x)
                {
                    TcpMapService.OnError(x);
                }
            }
        }

        private class RequestItem
        {
            internal CommandMessage Response;
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
                this._worker.LogMessage("TcpMapServerClient sending #" + reqid + " : " + msg);

                await this._swrite.WriteAsync(msg.Pack());
                await this._swrite.FlushAsync();

                if (!await ritem.cts.Token.WaitForSignalSettedAsync(timeout))//TODO:const
                {
                    throw new Exception($"request timeout #{reqid} '{msg}'");
                }

                if (ritem.Response == null)
                {
                    throw new Exception($"No Response ? ");
                }

                return ritem.Response.Args[1] == "Error" ? throw new Exception("Command Failed.") : ritem.Response;
            }
            finally
            {
                this.reqmap.TryRemove(reqid, out _);
            }
        }
    }
}
