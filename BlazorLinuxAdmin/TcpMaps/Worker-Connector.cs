namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;
    using BlazorLinuxAdmin.TcpMaps.UDP;

    public class TcpMapConnectorWorker : TcpMapBaseWorker
    {
        public TcpMapConnector Connector { get; set; }

        public bool IsListened { get; private set; }

        private TcpListener listener;
        private CancellationTokenSource cts;

        public void StartWork ()
        {
            if (this.Connector.IsDisabled)
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
                if (this.Connector.License == null)
                {
                    throw new Exception("Miss License Key");
                }

                int againTimeout = 500;
            StartAgain:
                this.listener = new TcpListener(IPAddress.Any, this.Connector.LocalPort);
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

                            socket.InitTcp();
                            await this.ProcessSocketAsync(socket);

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

        private DateTime stopUseRouterClientPortUntil;

        private async Task ProcessSocketAsync (Socket tcpSocket)
        {
        TryAgain:
            string connmode = null;
            if (this.Connector.UseRouterClientPort && DateTime.Now > this.stopUseRouterClientPortUntil)
            {
                connmode = "RCP";
            }
            else if (this.Connector.UseUDPPunching && DateTime.Now > this.stopUseRouterClientPortUntil)
            {
                connmode = "UDP";
            }
            else if (this.Connector.UseServerBandwidth)
            {
                connmode = "USB";
            }
            else
            {
                //TODO:  try ..
                connmode = this.Connector.UseUDPPunching
                    ? "UDP"
                    : this.Connector.UseRouterClientPort ? "RCP" : throw new Exception("No connection mode.");
            }

            Task<KeyValuePair<string, UDPClientListener>> task2 = null;

            if (connmode == "UDP")
            {
                //TODO: shall cache the UDPClientListener ...
                task2 = Task.Run(this.GetUdpClientAsync);
            }

            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            serverSocket.InitTcp();
            await serverSocket.ConnectAsync(this.Connector.ServerHost, 6022);

            string connArgument = null;
            UDPClientListener udp = null;
            if (task2 != null)
            {
                try
                {
                    var kvp = await task2;
                    connArgument = kvp.Key;
                    udp = kvp.Value;

                }
                catch (Exception x)
                {
                    this.OnError(x);
                    this.LogMessage("UDP Failed , switch ...");
                    connmode = this.Connector.UseServerBandwidth ? "USB" : throw new Exception(x.Message, x);
                }
            }

            bool supportEncrypt = this.Connector.UseEncrypt;

            CommandMessage connmsg = new CommandMessage
            {
                Name = "ConnectorConnect"
            };
            List<string> arglist = new List<string>
            {
                this.Connector.License.Key,
                this.Connector.ServerPort.ToString()
            };
            this.Connector.License.GenerateSecureKeyAndHash(out byte[] clientKeyIV, out byte[] encryptedKeyIV, out byte[] sourceHash);
            arglist.Add(Convert.ToBase64String(encryptedKeyIV));
            arglist.Add(Convert.ToBase64String(sourceHash));
            arglist.Add(supportEncrypt ? "1" : "0");
            arglist.Add(connmode);
            arglist.Add(connArgument);
            connmsg.Args = arglist.ToArray();

            await serverSocket.SendAsync(connmsg.Pack(), SocketFlags.None);

            connmsg = await CommandMessage.ReadFromSocketAsync(serverSocket);

            if (connmsg == null)
            {
                this.LogMessage("Warning:ConnectorWorker : remote closed connection.");
                return;
            }

            //LogMessage("Warning:connmsg : " + connmsg);

            if (connmsg.Name != "ConnectOK")
            {
                return;
            }

            //TODO: add to session list

            if (supportEncrypt && connmsg.Args[1] == "0")
            {
                supportEncrypt = false; this.LogMessage("Warning:server don't support encryption : " + this.Connector.ServerHost);
            }

            Stream _sread, _swrite;

            if (connmode == "RCP")
            {
                serverSocket.CloseSocket();

                string ip = connmsg.Args[2];
                int port = int.Parse(connmsg.Args[3]);
                if (port < 1)
                {
                    this.LogMessage("Error:Invalid configuration , remote-client-side don't provide RouterClientPort , stop use RCP for 1min");
                    this.stopUseRouterClientPortUntil = DateTime.Now.AddSeconds(60);
                    goto TryAgain;//TODO: reuse the serverSocket and switch to another mode 
                }

                this.LogMessage("Warning:" + tcpSocket.LocalEndPoint + " forward to " + ip + ":" + port);
                await tcpSocket.ForwardToAndWorkAsync(ip, port);

                return;
            }

            if (connmode == "UDP")
            {
                this.LogMessage("MY UDP..." + connArgument + " REMOTE..." + connmsg.Args[2]);

                string mynat = connArgument;
                string[] pair = connmsg.Args[2].Split(':');

                serverSocket.CloseSocket();

                try
                {
                    UDPClientStream stream = await UDPClientStream.ConnectAsync(udp, pair[0], int.Parse(pair[1]), TimeSpan.FromSeconds(6));

                    _sread = stream;
                    _swrite = stream;

                    this.LogMessage("UDP Connected #" + stream.SessionId + " " + connArgument + " .. " + connmsg.Args[2]);
                }
                catch (Exception x)
                {
                    this.LogMessage("UDP ERROR " + connArgument + " .. " + connmsg.Args[2] + " " + x.Message);
                    throw;
                }
            }
            else
            {
                if (supportEncrypt)
                {
                    this.Connector.License.OverrideStream(serverSocket.CreateStream(), clientKeyIV, out _sread, out _swrite);
                }
                else
                {
                    _sread = _swrite = serverSocket.CreateStream();
                }
            }

            TcpMapConnectorSession session = new TcpMapConnectorSession(new SimpleSocketStream(tcpSocket));
            await session.DirectWorkAsync(_sread, _swrite);
        }

        private string lastnat;
        private UDPClientListener lastudp;
        private DateTime timeudp;
        private CancellationTokenSource ctsudpnew;

        private async Task<KeyValuePair<string, UDPClientListener>> GetUdpClientAsync ()
        {
            if (!this.Connector.UDPCachePort)
            {
                return await this.CreteUdpClientAsync();
            }

        TryAgain:
            if (this.lastudp != null && DateTime.Now - this.timeudp < TimeSpan.FromSeconds(8))
            {
                lock (this)
                {
                    return new KeyValuePair<string, UDPClientListener>(this.lastnat, this.lastudp);
                }
            }

            bool ctsCreated = false;
            CancellationTokenSource cts = this.ctsudpnew;
            if (cts == null)
            {
                lock (this)
                {
                    if (this.ctsudpnew == null)
                    {
                        this.ctsudpnew = new CancellationTokenSource();
                        ctsCreated = true;
                    }
                    cts = this.ctsudpnew;
                }
            }

            if (!ctsCreated)
            {
                await cts.Token.WaitForSignalSettedAsync(3000);
                goto TryAgain;
            }

            try
            {
                //TODO: cache the port and reuse.

                var kvp = await this.CreteUdpClientAsync();

                lock (this)
                {
                    this.lastnat = kvp.Key;
                    this.lastudp = kvp.Value;
                    this.timeudp = DateTime.Now;
                }

                return kvp;
            }
            finally
            {
                lock (this)
                {
                    cts.Cancel();
                    this.ctsudpnew = null;
                }
            }
        }

        private async Task<KeyValuePair<string, UDPClientListener>> CreteUdpClientAsync ()
        {
            UdpClient udp = new UdpClient();

            udp.Client.ReceiveTimeout = 4321;
            udp.Client.SendTimeout = 4321;
            udp.Send(System.Text.Encoding.ASCII.GetBytes("whoami"), 6, this.Connector.ServerHost, 6023);

            this.LogMessage("Warning:udp.ReceiveAsync");

            var rr = await udp.ReceiveAsync();
            string exp = System.Text.Encoding.ASCII.GetString(rr.Buffer);

            this.LogMessage("Warning:udp get " + exp);

            if (!exp.StartsWith("UDP="))
            {
                throw (new Exception("failed"));
            }

            string natinfo = exp.Remove(0, 4);
            UDPClientListener udpc = new UDPClientListener(udp);

            return new KeyValuePair<string, UDPClientListener>(natinfo, udpc);
        }

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
        }
    }
}
