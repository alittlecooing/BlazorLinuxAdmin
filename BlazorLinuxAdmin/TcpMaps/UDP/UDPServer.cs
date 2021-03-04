using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorLinuxAdmin.TcpMaps.UDP
{

    public interface IUDPServer
    {
        void SendToClient (IPEndPoint remote, byte[] buff);
        byte[] Receive (TimeSpan timeout, out IPEndPoint remote);
        IPEndPoint LocalEndPoint { get; }
    }


    public class UDPServerListener : IDisposable
    {
        private IUDPServer _udp;
        private Action<Stream, IPEndPoint> _onstream;
        public UDPServerListener (IUDPServer udp, Action<Stream, IPEndPoint> onstream)
        {
            this._udp = udp;
            this._onstream = onstream;

            this._workthread = new Thread(this.ListenerWorkThread);//TODO:Convert to async
            this._workthread.IsBackground = true;
            this._workthread.Start();
            //_timer = new Timer(delegate
            //{
            //	OnTimer();
            //}, null, 20, 20);
        }

        private Thread _workthread;

        //Timer _timer;
        //void OnTimer()
        //{
        //}

        private Dictionary<long, ServerStream> strmap = new Dictionary<long, ServerStream>();

        private void ListenerWorkThread ()
        {
            while (this._workthread != null)
            {
                try
                {
                    //TODO: if not cached , and all stream closed , shutdown directly

                    IPEndPoint ep;
                    byte[] data = this._udp.Receive(TimeSpan.FromSeconds(90), out ep);
                    if (data == null)
                    {
                        Console.WriteLine("_udp.Receive return null for a long time...; Shutdown..." + this._udp.LocalEndPoint);
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
                        if (ss.ConnectToken != udpc.token)
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
                            this._udp.SendToClient(ep, UDPMeta.CreateSessionError(sid, "NotFound"));
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
                ServerStream ss;
                if (this.strmap.TryGetValue(sid, out ss))
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
                this._udp.SendToClient(stream.RemoteEndPoint, UDPMeta.CreateSessionError(sid, "Reject"));
            }
        }

        public class ServerStream : UDPBaseStream
        {
            public override long SessionId { get; internal set; }
            public IPEndPoint RemoteEndPoint { get; private set; }

            private UDPServerListener _sl;
            private CancellationTokenSource cts = new CancellationTokenSource();
            private BufferedReader _reader;

            public ServerStream (UDPServerListener sl, long sid, IPEndPoint ep, UDPConnectJson cjson)
            {
                this._sl = sl;
                this.SessionId = sid;
                this.RemoteEndPoint = ep;
                this.ConnectToken = cjson.token;
            }

            private UDPPackageType _waitfor = UDPPackageType.SessionConnect;
            public string ConnectToken { get; private set; }

            protected override void SendToPeer (byte[] data)
            {
                //Console.WriteLine("SendToPeer:" + UDPMeta.GetPackageType(data));
                this._sl._udp.SendToClient(this.RemoteEndPoint, data);
            }
            protected override void OnPost (byte[] data)
            {
                this._reader.PushBuffer(data);
            }
            protected override void OnPost (byte[] data, int offset, int count)
            {
                this._reader.PushBuffer(data, offset, count);
            }

            public void Process (UDPPackageType pt, byte[] data)
            {
                if (this._waitfor == pt)
                {
                    TcpMapService.LogMessage("UDPServer:" + pt);

                    if (pt == UDPPackageType.SessionConnect)
                    {
                        UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(data, 16, data.Length - 16));
                        if (udpc.token != this.ConnectToken)
                        {
                            this._sl.RejectConnect(this);
                            return;
                        }
                        byte[] buffprepair = UDPMeta.CreateSessionPrepair(this.SessionId, this.ConnectToken);
                        this.SendToPeer(buffprepair); this.SendToPeer(buffprepair);
                        this._waitfor = UDPPackageType.SessionConfirm;
                        return;
                    }
                    if (pt == UDPPackageType.SessionConfirm)
                    {
                        UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(data, 16, data.Length - 16));
                        if (udpc.token != this.ConnectToken)
                        {
                            this._sl.RejectConnect(this);
                            return;
                        }
                        byte[] buffready = UDPMeta.CreateSessionReady(this.SessionId, this.ConnectToken);
                        this.SendToPeer(buffready); this.SendToPeer(buffready);
                        this._waitfor = UDPPackageType.SessionIdle;
                        this._reader = new BufferedReader(this.cts.Token);
                        ThreadPool.QueueUserWorkItem(delegate
                        {
                            this._sl._onstream(this, this.RemoteEndPoint);
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
                int rc = await this._reader.ReadAsync(buffer, offset, count, cancellationToken);
                //Console.WriteLine("UDPServerStream read " + rc);
                return rc;

            }
            public override int Read (byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }



            public override void Close ()
            {
                this.cts.Cancel();
                if (this._reader != null)
                {
                    this._reader.Dispose();
                }

                base.Close();
            }

            public void ForceClose ()
            {
                this.Close();
            }

        }


        public void Close ()
        {
            if (this._workthread != null)
            {
                //NOT SUPPORT ON THIS PLATFORM
                //workthread.Abort();
                this._workthread = null;
            }
        }

        public void Dispose ()
        {
            this.Close();
        }
    }

}