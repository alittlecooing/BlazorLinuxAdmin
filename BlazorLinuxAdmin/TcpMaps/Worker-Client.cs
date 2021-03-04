namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using UDP;

    // Intranet ClientWorker <-websocket-> TcpMapService ServerClient <-> ServerWorker <-tcp-> PublicInternet
    public class TcpMapClientWorker : TcpMapBaseWorker
    {
        public TcpMapClient Client { get; set; }

        public bool IsConnected { get; private set; }

        //System.Net.WebSockets.ClientWebSocket ws;
        private Socket _socket;
        private CancellationTokenSource _cts_connect;
        private Stream _sread, _swrite;

        public void StartWork ()
        {
            if (this.Client.IsDisabled)
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
            this.IsConnected = false;
            this.LogMessage("ClientWorker WorkAsync start");
            int connectedTimes = 0;
            try
            {
                int againTimeout = 125;
            StartAgain:

                this._socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                this._socket.InitTcp();
                this._cts_connect = new CancellationTokenSource();
                try
                {
                    await this._socket.ConnectAsync(this.Client.ServerHost, 6022);

                    this.LogMessage("connected to 6022");
                }
                catch (Exception x)
                {
                    this.OnError(x);
                    this._socket.CloseSocket();
                    this._socket = null;
                    this._cts_connect = new CancellationTokenSource();
                    if (!this.IsStarted)
                    {
                        return;
                    }

                    if (againTimeout < 4)
                    {
                        againTimeout = againTimeout * 2;
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
                        CommandMessage connmsg = new CommandMessage();
                        connmsg.Name = "ClientConnect";
                        List<string> arglist = new List<string>();
                        arglist.Add(this.Client.License.Key);
                        arglist.Add(this.Client.ServerPort.ToString());
                        byte[] encryptedKeyIV, sourceHash;
                        this.Client.License.GenerateSecureKeyAndHash(out clientKeyIV, out encryptedKeyIV, out sourceHash);
                        arglist.Add(Convert.ToBase64String(encryptedKeyIV));
                        arglist.Add(Convert.ToBase64String(sourceHash));
                        arglist.Add(supportEncrypt ? "1" : "0");
                        connmsg.Args = arglist.ToArray();

                        await this._socket.SendAsync(connmsg.Pack(), SocketFlags.None);

                        //LogMessage("wait for conn msg");

                        connmsg = await CommandMessage.ReadFromSocketAsync(this._socket);

                        if (connmsg == null)
                        {
                            TcpMapService.LogMessage("no message ? Connected:" + this._socket.Connected);
                            return;
                        }

                        this.LogMessage("connmsg : " + connmsg.Name + " : " + string.Join(",", connmsg.Args));

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
                    this.LogMessage("ConnectOK #" + connectedTimes);

                    if (supportEncrypt)
                    {
                        this.Client.License.OverrideStream(this._socket.CreateStream(), clientKeyIV, out this._sread, out this._swrite);
                    }
                    else
                    {
                        this._sread = this._swrite = this._socket.CreateStream();
                    }

                    for (int i = 0; i < Math.Min(5, this.Client.PreSessionCount); i++)//TODO:const of 5
                    {
                        _ = Task.Run(this.ProvidePreSessionAsync);
                    }

                    _ = Task.Run(this.MaintainSessionsAsync);

                    if (this.Client.RouterClientPort > 0)
                    {
                        _ = this._swrite.WriteAsync(new CommandMessage("SetOption", "RouterClientPort", this.Client.RouterClientPort.ToString()).Pack());
                    }

                    byte[] readbuff = new byte[TcpMapService.DefaultBufferSize];
                    while (this.IsStarted)
                    {
                        CommandMessage msg;
                        var cts = new CancellationTokenSource();
                        _ = Task.Run(async delegate
                          {
                              if (await cts.Token.WaitForSignalSettedAsync(16000))
                              {
                                  return;
                              }

                              try
                              {
                                  await this._swrite.WriteAsync(new CommandMessage("_ping_", "forread").Pack());
                              }
                              catch (Exception x)
                              {
                                  this.OnError(x);
                              }
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
                            TcpMapService.LogMessage("no message ? Connected:" + this._socket.Connected);
                            return;
                        }

                        //this.LogMessage("TcpMapClientWorker get msg " + msg);

                        switch (msg.Name)
                        {
                            case "StartSession":
                                Task.Run(async delegate
                                {
                                    try
                                    {
                                        await this.DoStartSessionAsync(msg);
                                    }
                                    catch (Exception x)
                                    {
                                        this.OnError(x);
                                    }
                                }).ToString();
                                break;
                            case "CloseSession":
                                Task.Run(async delegate
                                {
                                    try
                                    {
                                        await this.DoCloseSessionAsync(msg);
                                    }
                                    catch (Exception x)
                                    {
                                        this.OnError(x);
                                    }
                                }).ToString();
                                break;
                            case "CreateUDPNat":
                                Task.Run(async delegate
                                {
                                    try
                                    {
                                        await this.DoCreateUDPNatAsync(msg);
                                    }
                                    catch (Exception x)
                                    {
                                        this.OnError(x);
                                    }
                                }).ToString();
                                break;
                            case "_ping_":
                                await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                                break;
                            case "_ping_result_":
                                break;
                            default:
                                this.LogMessage("Error: 4 Ignore message " + msg);
                                break;
                        }
                    }
                }
                catch (SocketException)
                {
                    //no log
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
                if (this.IsStarted)
                {
                    this._socket.CloseSocket();//logic failed..
                    againTimeout = 125;
                    goto StartAgain;
                }
            }
            catch (Exception x)
            {
                this.OnError(x);
            }

            this.IsStarted = false;
            this.IsConnected = false;

            if (this._socket != null)
            {
                try
                {
                    this._socket.CloseSocket();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
                this._socket = null;
            }
        }

        private async Task DoCreateUDPNatAsync (CommandMessage msg)
        {
            try
            {
                string[] peerinfo = msg.Args[1].Split(":");
                string peeraddr = peerinfo[0];
                int peerport = int.Parse(peerinfo[1]);

                this.LogMessage("Warning:send whoami to " + this.Client.ServerHost + ":6023");

                UdpClient udp = new UdpClient();
                udp.Client.ReceiveTimeout = 4321;
                udp.Client.SendTimeout = 4321;
                udp.Send(Encoding.ASCII.GetBytes("whoami"), 6, this.Client.ServerHost, 6023);

                this.LogMessage("Warning:udp.ReceiveAsync");

            ReadAgain:
                var rr = await udp.ReceiveAsync();  //TODO: add timeout..
                if (rr.RemoteEndPoint.Port != 6023)
                {
                    goto ReadAgain;
                }

                string exp = Encoding.ASCII.GetString(rr.Buffer);

                this.LogMessage("Warning:udp get " + exp);

                if (!exp.StartsWith("UDP="))
                {
                    throw (new Exception("failed"));
                }

                exp = exp.Remove(0, 4);
                msg.Args[1] = exp;

                //TODO: shall cache and reuse this address ? but always send "hello" to new peer again..
                ClientWorkerUDPConnector udpconn = new ClientWorkerUDPConnector();
                udpconn.Start(this, udp, exp);

                IPEndPoint pperep = new IPEndPoint(IPAddress.Parse(peeraddr), peerport);
                _ = Task.Run(async delegate
                  {
                      byte[] msgdata = UDPMeta.CreateSessionIdle(-1);
                      for (int i = 0; i < 10; i++)
                      {
                          if (udpconn.IsEverConnected(pperep))
                          {
                              return;
                          }

                          udp.Send(msgdata, msgdata.Length, pperep);
                          Console.WriteLine("SENT " + pperep + "  via  " + exp);
                          await Task.Delay(100);
                      }
                  });
            }
            catch (Exception x)
            {
                this.OnError(x);
                msg.Args[1] = "Error";
            }
            msg.Name = "CreateUDPNatResult";
            this.LogMessage("TcpMapClientWorker sending " + msg);
            await this._swrite.WriteAsync(msg.Pack());
        }

        private async Task ProvidePreSessionAsync ()
        {
            while (this.IsConnected)
            {
                try
                {
                    var session = new TcpMapClientSession(this.Client, null);
                    lock (this._presessions)
                    {
                        this._presessions.Add(session);
                    }

                    try
                    {
                        await session.WaitUpgradeAsync();
                    }
                    finally
                    {
                        lock (this._presessions)
                        {
                            this._presessions.Remove(session);
                        }
                    }
                    if (session.SessionId != null)
                    {
                        this.sessionMap.TryAdd(session.SessionId, session);
                        this.LogMessage("Warning:ClientWorker Session Upgraded  " + session.SessionId);
                    }
                    else
                    {
                        this.LogMessage("Warning:ClientWorker Session Closed ?  " + this.IsConnected + " , " + session.SessionId);
                    }
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }

                await Task.Yield();
                //await Task.Delay(2000);
            }
        }

        private async Task DoStartSessionAsync (CommandMessage msg)
        {
            string sid = msg.Args[1];
            if (this.sessionMap.TryRemove(sid, out var session))
            {
                session.Close();
            }

            session = new TcpMapClientSession(this.Client, sid);
            this.sessionMap.TryAdd(sid, session);
            try
            {
                await session.StartAsync();
                msg.Args[1] = "OK";
            }
            catch (Exception x)
            {
                this.OnError(x);
                msg.Args[1] = "Error";
            }
            msg.Name = "StartSessionResult";
            this.LogMessage("TcpMapClientWorker sending " + msg);
            await this._swrite.WriteAsync(msg.Pack());
        }

        private async Task DoCloseSessionAsync (CommandMessage msg)
        {
            string sid = msg.Args[1];
            if (this.sessionMap.TryGetValue(sid, out var session))
            {
                session.Close();
            }
            msg.Name = "CloseSessionResult";
            await this._swrite.WriteAsync(msg.Pack());
        }

        public void Stop ()
        {
            if (!this.IsStarted)
            {
                return;
            }

            this.IsStarted = false;
            this.IsConnected = false;
            //LogMessage("Warning:Stop at " + Environment.StackTrace);
            if (this._socket != null)
            {
                try
                {
                    this._socket.CloseSocket();
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
                this._socket = null;
            }
            this._cts_connect?.Cancel();
            this.LogMessage("ClientWorker Close :_presessions:" + this._presessions.Count + " , sessionMap:" + this.sessionMap.Count);

            foreach (TcpMapClientSession session in this._presessions.LockToArray())
            {
                session.Close();
            }

            foreach (TcpMapClientSession session in this.sessionMap.LockToArray().Select(v => v.Value))
            {
                session.Close();
            }
        }

        private async Task MaintainSessionsAsync ()
        {
            while (this.IsConnected)
            {
                await Task.Delay(5000);
                try
                {
                    if (this.sessionMap.Count == 0)
                    {
                        continue;
                    }

                    foreach (var kvp in this.sessionMap.LockToArray())
                    {
                        if (kvp.Value.ShallRecycle())
                        {
                            lock (this.sessionMap)
                            {
                                this.sessionMap.TryRemove(kvp.Key, out _);
                            }
                        }
                    }
                }
                catch (Exception x)
                {
                    this.OnError(x);
                }
            }
        }

        internal List<TcpMapClientSession> _presessions = new List<TcpMapClientSession>();
        internal ConcurrentDictionary<string, TcpMapClientSession> sessionMap = new ConcurrentDictionary<string, TcpMapClientSession>();
    }

    internal class ClientWorkerUDPConnector
    {
        private class UDPS : IUDPServer
        {
            private UdpClient _udp;

            public IPEndPoint LocalEndPoint => (IPEndPoint)this._udp.Client.LocalEndPoint;

            public UDPS (UdpClient udp) => this._udp = udp;

            public void SendToClient (IPEndPoint remote, byte[] buff) => this._udp.Send(buff, buff.Length, remote);

            public byte[] Receive (TimeSpan timeout, out IPEndPoint remote)
            {
                remote = null;
                try
                {
                    this._udp.Client.ReceiveTimeout = (int)timeout.TotalMilliseconds;
                    return this._udp.Receive(ref remote);
                }
                catch (Exception)
                {
                    return null;
                }
            }

        }

        private UdpClient _udp;
        private TcpMapClientWorker _worker;
        private string _localnat;
        private IPEndPoint _lastconnect;
        public bool IsEverConnected (IPEndPoint ipep) => this._lastconnect != null && ipep.Equals(this._lastconnect);

        public void Start (TcpMapClientWorker worker, UdpClient udp, string localnat)
        {
            this._udp = udp;
            this._worker = worker;
            this._localnat = localnat;
            UDPServerListener listener = new UDPServerListener(new UDPS(udp), delegate (Stream stream, IPEndPoint remote)
            {
                this._lastconnect = remote;
                _ = this.HandleStreamAsync(stream, remote);
            });
        }

        private async Task HandleStreamAsync (Stream stream, IPEndPoint remote)
        {
            this._worker.LogMessage("UDP Session Start : " + this._udp.Client.LocalEndPoint + " , " + remote);
            try
            {
                using Socket localsock = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                localsock.InitTcp();
                await localsock.ConnectAsync(this._worker.Client.ClientHost, this._worker.Client.ClientPort);

                TcpMapConnectorSession session = new TcpMapConnectorSession(new SimpleSocketStream(localsock));
                await session.DirectWorkAsync(stream, stream);
            }
            catch (Exception x)
            {
                this._worker.OnError(x);
            }
            finally
            {
                stream.Close();
            }
        }
    }
}
