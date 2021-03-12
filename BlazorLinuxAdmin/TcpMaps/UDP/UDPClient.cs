namespace BlazorLinuxAdmin.TcpMaps.UDP
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    internal class ClientBufferReader
    {
        private readonly ManualResetEvent mre = new ManualResetEvent(false);

        public int Read (byte[] buffer, int offset, int count, TimeSpan timeout)
        {
            //if (_removed) throw (new InvalidOperationException());

            int rc = 0;

        READBUFF:

            if (this.buff != null)
            {
                int rl = Math.Min(count - rc, this.buff.Length - this.bidx);
                Buffer.BlockCopy(this.buff, this.bidx, buffer, offset + rc, rl);
                rc += rl;
                this.bidx += rl;
                if (this.bidx == this.buff.Length)
                {
                    this.buff = null;
                }
            }

            if (rc == count)
            {
                return rc;
            }

            lock (this.packs)
            {
                if (this.packs.Count > 0)
                {
                    this.buff = this.packs.Dequeue();
                    this.bidx = 0;
                    goto READBUFF;
                }

                if (rc > 0)
                {
                    return rc;//don't wait
                }

                if (this.removed)
                {
                    return rc;
                }

                this.mre.Reset();
            }

            this.mre.WaitOne(timeout);
            goto READBUFF;
        }

        private byte[] buff;
        private int bidx;
        private readonly Queue<byte[]> packs = new Queue<byte[]>();

        public bool DataAvailable => this.buff != null || this.packs.Count > 0;

        public void OnPost (byte[] buffer, int offset, int count)
        {
            if (offset == 0 && count == buffer.Length)
            {
                this.OnPost((byte[])buffer.Clone());
            }
            else
            {
                byte[] pack = new byte[count];
                Buffer.BlockCopy(buffer, offset, pack, 0, count);
                this.OnPost(pack);
            }
        }
        public void OnPost (byte[] pack)
        {
            lock (this.packs)
            {
                this.packs.Enqueue(pack);
                this.mre.Set();
            }
        }

        internal bool removed = false;
        public void OnRemoved ()
        {
            lock (this.packs)
            {
                this.removed = true;
                this.mre.Set();
            }
        }
    }

    internal class UDPBufferReader
    {
        private readonly ManualResetEvent mre = new ManualResetEvent(false);

        public byte[] Read (TimeSpan timeout)
        {
        //if (_removed) throw (new InvalidOperationException());

        READBUFF:

            lock (this.packs)
            {
                if (this.packs.Count > 0)
                {
                    return this.packs.Dequeue();
                }

                if (this.removed)
                {
                    return null;
                }

                this.mre.Reset();
            }

            bool waited = this.mre.WaitOne(timeout);
            if (!waited)
            {
                return null;// throw (new TimeoutException());
            }

            goto READBUFF;
        }

        private readonly Queue<byte[]> packs = new Queue<byte[]>();

        public void OnPost (byte[] pack)
        {
            lock (this.packs)
            {
                this.packs.Enqueue(pack);
                this.mre.Set();
            }
        }

        private bool removed = false;
        public void OnRemoved ()
        {
            lock (this.packs)
            {
                this.removed = true;
                this.mre.Set();
            }
        }
    }


    public class UDPClientListener
    {
        private readonly UdpClient uc;
        public UDPClientListener (UdpClient uc)
        {
            this.uc = uc;
            this.uc.Client.ReceiveTimeout = 45000;

            _ = this.ReadLoopAsync();
        }

        public DateTime lastReceiveTime = DateTime.Now;


        public async Task<byte[]> ReceiveAsync ()
        {
            var r = await this.uc.ReceiveAsync();
            return r.Buffer;
        }


        public IPEndPoint GetLocalEndpoint () => (IPEndPoint)this.uc.Client.LocalEndPoint;

        private bool closed;

        private async Task ReadLoopAsync ()
        {
            await Task.Yield();
            while (true)
            {
                //TODO: if not cached , and all stream closed , shutdown directly
                var data = await this.ReceiveAsync();
                if (data == null)
                {
                    Console.WriteLine("_udp.ReceiveAsync() return null ,Close()");
                    this.Close();
                    return;
                }
                long sid = BitConverter.ToInt64(data, 8);
                if (sid == -1)
                {
                    Console.WriteLine("_udp.ReceiveAsync() return " + sid + ", " + UDPMeta.GetPackageType(data));
                }

                UDPBufferReader reader = this.GetBufferReader(sid);
                reader.OnPost(data);
            }
        }

        private void Close ()
        {
            this.closed = true;
            lock (this.buffmap)
            {
                foreach (UDPBufferReader reader in this.buffmap.Values)
                {
                    reader.OnRemoved();
                }
                this.buffmap.Clear();
            }
        }

        private UDPBufferReader GetBufferReader (long sid)
        {
            UDPBufferReader reader;
            lock (this.buffmap)
            {
                if (this.closed)
                {
                    throw (new Exception("closed"));
                }

                if (!this.buffmap.TryGetValue(sid, out reader))
                {
                    this.buffmap[sid] = reader = new UDPBufferReader();
                }
            }
            return reader;
        }

        private readonly Dictionary<long, UDPBufferReader> buffmap = new Dictionary<long, UDPBufferReader>();

        public byte[] Receive (long sid, TimeSpan timeout)
        {
            _ = new byte[65536];
            UDPBufferReader reader = this.GetBufferReader(sid);
            return reader.Read(timeout);
        }
        public void SendToServer (byte[] data, IPEndPoint _server) => this.uc.Client.SendTo(data, _server);

    }


    public class UDPClientStream : UDPBaseStream
    {
        private static long _nextsessionid = 1;
        public override long SessionId { get; internal set; }

        private UDPClientListener udp;
        private IPEndPoint server;
        private readonly DateTime dts = DateTime.Now;

        private void CheckTimeout (TimeSpan timeout)
        {
            if (DateTime.Now - this.dts > timeout)
            {
                throw (new Exception("timeout"));
            }
        }

        protected override void SendToPeer (byte[] data) => this.udp.SendToServer(data, this.server);
        protected override void OnPost (byte[] data) => this.reader.OnPost(data);
        protected override void OnPost (byte[] data, int offset, int count) => this.reader.OnPost(data, offset, count);

        private UDPClientStream ()
        {
        }

        public static async Task<UDPClientStream> ConnectAsync (UDPClientListener udp, string ip, int port, TimeSpan timeout)
        {
            var s = new UDPClientStream
            {
                server = new IPEndPoint(IPAddress.Parse(ip), port)
            };

            Exception error = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            RunInNewThread(delegate
            {
                try
                {
                    s.Connect(udp, ip, port, timeout);
                }
                catch (Exception x)
                {
                    error = x;
                }
                cts.Cancel();
            });
            await cts.Token.WaitForSignalSettedAsync(-1);
            return error != null ? throw new Exception(error.Message, error) : s;
        }

        private void Connect (UDPClientListener udp, string ip, int port, TimeSpan timeout)
        {
            this.udp = udp;

        StartConnect:

            this.SessionId = Interlocked.Increment(ref _nextsessionid);

            string guid = Guid.NewGuid().ToString();
            byte[] buffconnect = UDPMeta.CreateSessionConnect(this.SessionId, guid);
            this.SendToPeer(buffconnect); this.SendToPeer(buffconnect);
            while (true)
            {
                byte[] buffprepair = this.udp.Receive(this.SessionId, TimeSpan.FromMilliseconds(500));
                if (buffprepair == null)
                {
                    this.CheckTimeout(timeout);
                    Console.WriteLine("Client StartConnect Again");
                    //SendToPeer(buffconnect); SendToPeer(buffconnect);
                    //continue;
                    goto StartConnect;
                }
                UDPPackageType ptprepair = UDPMeta.GetPackageType(buffprepair);
                if (ptprepair != UDPPackageType.SessionPrepair)
                {
                    if (ptprepair > UDPPackageType.SessionPrepair)
                    {
                        goto StartConnect;
                    }

                    continue;
                }
                long sidprepair = BitConverter.ToInt64(buffprepair, 8);
                if (sidprepair != this.SessionId)
                {
                    goto StartConnect;
                }

                UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(buffprepair, 16, buffprepair.Length - 16));
                if (udpc.Token != guid)
                {
                    goto StartConnect;
                }

                break;
            }
            byte[] buffconfirm = UDPMeta.CreateSessionConfirm(this.SessionId, guid);
            this.SendToPeer(buffconfirm); this.SendToPeer(buffconfirm);
            while (true)
            {
                byte[] buffready = this.udp.Receive(this.SessionId, TimeSpan.FromMilliseconds(500));
                if (buffready == null)
                {
                    this.CheckTimeout(timeout);
                    this.SendToPeer(buffconfirm); this.SendToPeer(buffconfirm);
                    continue;
                }
                UDPPackageType ptprepair = UDPMeta.GetPackageType(buffready);
                if (ptprepair != UDPPackageType.SessionReady)
                {
                    if (ptprepair > UDPPackageType.SessionReady)
                    {
                        goto StartConnect;
                    }

                    continue;
                }
                long sidprepair = BitConverter.ToInt64(buffready, 8);
                if (sidprepair != this.SessionId)
                {
                    goto StartConnect;
                }

                UDPConnectJson udpc = UDPConnectJson.Deserialize(Encoding.UTF8.GetString(buffready, 16, buffready.Length - 16));
                if (udpc.Token != guid)
                {
                    goto StartConnect;
                }

                break;
            }

            this.reader = new ClientBufferReader();
            Thread t = new Thread(this.ClientWorkThread)
            {
                IsBackground = true
            };
            t.Start();
        }

        private ClientBufferReader reader;

        private void ClientWorkThread ()
        {
            try
            {
                while (true)
                {
                    //TODO:when timeout , try to send idle message
                    byte[] data = this.udp.Receive(this.SessionId, TimeSpan.FromSeconds(40));
                    if (data == null)
                    {
                        if (!this.reader.removed)
                        {
                            Console.WriteLine("UDPClientStream.ClientWorkThread.Timeout-40");
                        }

                        this.reader.OnRemoved();

                        base.OnWorkerThreadExit();

                        return;
                    }

                    try
                    {
                        UDPPackageType pt = UDPMeta.GetPackageType(data);
                        this.ProcessUDPPackage(pt, data);
                    }
                    catch (Exception x)
                    {
                        TcpMapService.OnError(x);
                    }
                }
            }
            catch (Exception x)
            {
                TcpMapService.OnError(x);
            }

        }

        public override int Read (byte[] buffer, int offset, int count)
        {
            int rc = this.reader.Read(buffer, offset, count, TimeSpan.FromSeconds(55));
            //Console.WriteLine("UDPClientStream read " + rc);
            return rc;
        }

        public override void Flush ()
        {
        }

        public override void Close ()
        {
            if (this.reader != null)
            {
                this.reader.OnRemoved();
            }

            base.Close();
        }
    }
}