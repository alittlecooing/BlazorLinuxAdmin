namespace BlazorLinuxAdmin.TcpMaps.UDP
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public interface IUDPServer
    {
        void SendToClient (IPEndPoint remote, byte[] buff);
        byte[] Receive (TimeSpan timeout, out IPEndPoint remote);
        IPEndPoint LocalEndPoint { get; }
    }


    public class UDPServerListener : IDisposable
    {
        private readonly IUDPServer udp;
        private readonly Action<Stream, IPEndPoint> onstream;
        public UDPServerListener (IUDPServer udp, Action<Stream, IPEndPoint> onstream)
        {
            this.udp = udp;
            this.onstream = onstream;

            this.workthread = new Thread(this.ListenerWorkThread)
            {
                IsBackground = true
            };//TODO:Convert to async
            this.workthread.Start();
            //_timer = new Timer(delegate
            //{
            //	OnTimer();
            //}, null, 20, 20);
        }

        private Thread workthread;

        //Timer _timer;
        //void OnTimer()
        //{
        //}

        private readonly Dictionary<long, ServerStream> strmap = new Dictionary<long, ServerStream>();

        private void ListenerWorkThread ()
        {
            while (this.workthread != null)
            {
                try
                {
                    //TODO: if not cached , and all stream closed , shutdown directly

                    byte[] data = this.udp.Receive(TimeSpan.FromSeconds(90), out IPEndPoint ep);
                    if (data == null)
                    {
                        Console.WriteLine("_udp.Receive return null for a long time...; Shutdown..." + this.udp.LocalEndPoint);
                        this.Close();
                        return;
                    }

                    UDPPackageType pt = UDPMeta.GetPackageType(data);
                    if (pt == UDPPackageType.Unknown)
                    {
                        //Console.WriteLine("_udp.Receive return Unknown;");
                        return;
                    }

                    long sid = BitConverter.ToInt64(data, 8);
                    ServerStream ss;

                    //Console.WriteLine("_udp.Receive " + sid + ":" + pt);

                    if (pt == UDPPackageType.SessionConnect)
                    {
                        UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(data, 16, data.Length - 16));
                        lock (this.strmap)
                        {
                            if (!this.strmap.TryGetValue(sid, out ss))
                            {
                                ss = new ServerStream(this, sid, ep, udpc);
                                this.strmap[sid] = ss;
                            }
                        }
                        if (ss.ConnectToken != udpc.Token)
                        {
                            ss.ForceClose();
                            lock (this.strmap)
                            {
                                ss = new ServerStream(this, sid, ep, udpc);
                                this.strmap[sid] = ss;
                            }
                        }
                        else
                        {
                            ss.Process(pt, data);
                        }
                    }
                    else
                    {
                        lock (this.strmap)
                        {
                            this.strmap.TryGetValue(sid, out ss);
                        }
                        if (ss != null)
                        {
                            ss.Process(pt, data);
                        }
                        else
                        {
                            this.udp.SendToClient(ep, UDPMeta.CreateSessionError(sid, "NotFound"));
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (Exception x)
                {
                    TcpMapService.OnError(x);
                }
            }
        }

        private void RejectConnect (ServerStream stream)
        {
            long sid = stream.SessionId;
            bool removed = false;
            lock (this.strmap)
            {
                if (this.strmap.TryGetValue(sid, out ServerStream ss))
                {
                    if (ss == stream)
                    {
                        this.strmap.Remove(sid);
                        removed = true;
                    }
                }
            }
            if (removed)
            {
                this.udp.SendToClient(stream.RemoteEndPoint, UDPMeta.CreateSessionError(sid, "Reject"));
            }
        }

        public class ServerStream : UDPBaseStream
        {
            public override long SessionId { get; internal set; }
            public IPEndPoint RemoteEndPoint { get; private set; }

            private readonly UDPServerListener sl;
            private readonly CancellationTokenSource cts = new CancellationTokenSource();
            private BufferedReader reader;

            public ServerStream (UDPServerListener sl, long sid, IPEndPoint ep, UDPConnectJson cjson)
            {
                this.sl = sl;
                this.SessionId = sid;
                this.RemoteEndPoint = ep;
                this.ConnectToken = cjson.Token;
            }

            private UDPPackageType waitfor = UDPPackageType.SessionConnect;
            public string ConnectToken { get; private set; }

            protected override void SendToPeer (byte[] data) => this.sl.udp.SendToClient(this.RemoteEndPoint, data);

            protected override void OnPost (byte[] data) => this.reader.PushBuffer(data);

            protected override void OnPost (byte[] data, int offset, int count) => this.reader.PushBuffer(data, offset, count);

            public void Process (UDPPackageType pt, byte[] data)
            {
                if (this.waitfor == pt)
                {
                    TcpMapService.LogMessage("UDPServer:" + pt);

                    if (pt == UDPPackageType.SessionConnect)
                    {
                        UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(data, 16, data.Length - 16));
                        if (udpc.Token != this.ConnectToken)
                        {
                            this.sl.RejectConnect(this);
                            return;
                        }
                        byte[] buffprepair = UDPMeta.CreateSessionPrepair(this.SessionId, this.ConnectToken);
                        this.SendToPeer(buffprepair); this.SendToPeer(buffprepair);
                        this.waitfor = UDPPackageType.SessionConfirm;
                        return;
                    }
                    if (pt == UDPPackageType.SessionConfirm)
                    {
                        UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(data, 16, data.Length - 16));
                        if (udpc.Token != this.ConnectToken)
                        {
                            this.sl.RejectConnect(this);
                            return;
                        }
                        byte[] buffready = UDPMeta.CreateSessionReady(this.SessionId, this.ConnectToken);
                        this.SendToPeer(buffready); this.SendToPeer(buffready);
                        this.waitfor = UDPPackageType.SessionIdle;
                        this.reader = new BufferedReader(this.cts.Token);
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            this.sl.onstream(this, this.RemoteEndPoint);
                        });
                        return;
                    }
                    if (pt == UDPPackageType.SessionIdle)
                    {
                        this.SendToPeer(UDPMeta.CreateSessionIdle(this.SessionId));
                        return;
                    }
                }
                else
                {
                    this.ProcessUDPPackage(pt, data);
                }
            }

            public override async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                int rc = await this.reader.ReadAsync(buffer, offset, count, cancellationToken);
                //Console.WriteLine("UDPServerStream read " + rc);
                return rc;

            }
            public override int Read (byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override void Close ()
            {
                this.cts.Cancel();
                if (this.reader != null)
                {
                    this.reader.Dispose();
                }

                base.Close();
            }

            public void ForceClose () => this.Close();
        }

        public void Close ()
        {
            if (this.workthread != null)
            {
                //NOT SUPPORT ON THIS PLATFORM
                //workthread.Abort();
                this.workthread = null;
            }
        }

        public void Dispose () => this.Close();
    }
}