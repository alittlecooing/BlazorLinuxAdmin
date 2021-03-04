using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorLinuxAdmin.TcpMaps.UDP
{
    internal class ClientBufferReader
    {
        private ManualResetEvent _mre = new ManualResetEvent(false);

        public int Read (byte[] buffer, int offset, int count, TimeSpan timeout)
        {
            //if (_removed) throw (new InvalidOperationException());

            int rc = 0;

        READBUFF:

            if (this._buff != null)
            {
                int rl = Math.Min(count - rc, this._buff.Length - this._bidx);
                Buffer.BlockCopy(this._buff, this._bidx, buffer, offset + rc, rl);
                rc += rl;
                this._bidx += rl;
                if (this._bidx == this._buff.Length)
                {
                    this._buff = null;
                }
            }

            if (rc == count)
            {
                return rc;
            }

            lock (this._packs)
            {
                if (this._packs.Count > 0)
                {
                    this._buff = this._packs.Dequeue();
                    this._bidx = 0;
                    goto READBUFF;
                }

                if (rc > 0)
                {
                    return rc;//don't wait
                }

                if (this._removed)
                {
                    return rc;
                }

                this._mre.Reset();
            }

            this._mre.WaitOne(timeout);
            goto READBUFF;
        }

        private byte[] _buff;
        private int _bidx;
        private Queue<byte[]> _packs = new Queue<byte[]>();

        public bool DataAvailable
        {
            get
            {
                if (this._buff != null)
                {
                    return true;
                }

                if (this._packs.Count > 0)
                {
                    return true;
                }

                return false;
            }
        }

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
            lock (this._packs)
            {
                this._packs.Enqueue(pack);
                this._mre.Set();
            }
        }

        internal bool _removed = false;
        public void OnRemoved ()
        {
            lock (this._packs)
            {
                this._removed = true;
                this._mre.Set();
            }
        }
    }

    internal class UDPBufferReader
    {
        private ManualResetEvent _mre = new ManualResetEvent(false);

        public byte[] Read (TimeSpan timeout)
        {
        //if (_removed) throw (new InvalidOperationException());

        READBUFF:

            lock (this._packs)
            {
                if (this._packs.Count > 0)
                {
                    return this._packs.Dequeue();
                }

                if (this._removed)
                {
                    return null;
                }

                this._mre.Reset();
            }

            bool waited = this._mre.WaitOne(timeout);
            if (!waited)
            {
                return null;// throw (new TimeoutException());
            }

            goto READBUFF;
        }

        private Queue<byte[]> _packs = new Queue<byte[]>();

        public void OnPost (byte[] pack)
        {
            lock (this._packs)
            {
                this._packs.Enqueue(pack);
                this._mre.Set();
            }
        }

        private bool _removed = false;
        public void OnRemoved ()
        {
            lock (this._packs)
            {
                this._removed = true;
                this._mre.Set();
            }
        }
    }


    public class UDPClientListener
    {
        private UdpClient _uc;
        public UDPClientListener (UdpClient uc)
        {
            this._uc = uc;
            this._uc.Client.ReceiveTimeout = 45000;

            _ = this.ReadLoopAsync();
        }

        public DateTime LastReceiveTime = DateTime.Now;


        public async Task<byte[]> ReceiveAsync ()
        {
            var r = await this._uc.ReceiveAsync();
            return r.Buffer;
        }


        public IPEndPoint GetLocalEndpoint ()
        {
            return (IPEndPoint)this._uc.Client.LocalEndPoint;
        }

        private bool _closed;

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
            this._closed = true;
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
                if (this._closed)
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

        private Dictionary<long, UDPBufferReader> buffmap = new Dictionary<long, UDPBufferReader>();

        public byte[] Receive (long sid, TimeSpan timeout)
        {
            byte[] buff = new byte[65536];
            UDPBufferReader reader = this.GetBufferReader(sid);
            return reader.Read(timeout);
        }
        public void SendToServer (byte[] data, IPEndPoint _server)
        {
            this._uc.Client.SendTo(data, _server);
        }

    }


    public class UDPClientStream : UDPBaseStream
    {
        private static long _nextsessionid = 1;
        public override long SessionId { get; internal set; }

        private UDPClientListener _udp;
        private IPEndPoint _server;
        private DateTime _dts = DateTime.Now;

        private void CheckTimeout (TimeSpan timeout)
        {
            if (DateTime.Now - this._dts > timeout)
            {
                throw (new Exception("timeout"));
            }
        }

        protected override void SendToPeer (byte[] data)
        {
            //Console.WriteLine("SendToPeer:" + UDPMeta.GetPackageType(data));
            this._udp.SendToServer(data, this._server);
        }
        protected override void OnPost (byte[] data)
        {
            this._reader.OnPost(data);
        }
        protected override void OnPost (byte[] data, int offset, int count)
        {
            this._reader.OnPost(data, offset, count);
        }

        private UDPClientStream ()
        {

        }

        public static async Task<UDPClientStream> ConnectAsync (UDPClientListener udp, string ip, int port, TimeSpan timeout)
        {
            var s = new UDPClientStream();
            s._server = new IPEndPoint(IPAddress.Parse(ip), port);

            Exception error = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            RunInNewThread(delegate
            {
                try
                {
                    s._Connect(udp, ip, port, timeout);
                }
                catch (Exception x)
                {
                    error = x;
                }
                cts.Cancel();
            });
            await cts.Token.WaitForSignalSettedAsync(-1);
            if (error != null)
            {
                throw new Exception(error.Message, error);
            }

            return s;
        }

        private void _Connect (UDPClientListener udp, string ip, int port, TimeSpan timeout)
        {
            this._udp = udp;

        StartConnect:

            this.SessionId = Interlocked.Increment(ref _nextsessionid);

            string guid = Guid.NewGuid().ToString();
            byte[] buffconnect = UDPMeta.CreateSessionConnect(this.SessionId, guid);
            this.SendToPeer(buffconnect); this.SendToPeer(buffconnect);
            while (true)
            {
                byte[] buffprepair = this._udp.Receive(this.SessionId, TimeSpan.FromMilliseconds(500));
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
                if (udpc.token != guid)
                {
                    goto StartConnect;
                }

                break;
            }
            byte[] buffconfirm = UDPMeta.CreateSessionConfirm(this.SessionId, guid);
            this.SendToPeer(buffconfirm); this.SendToPeer(buffconfirm);
            while (true)
            {
                byte[] buffready = this._udp.Receive(this.SessionId, TimeSpan.FromMilliseconds(500));
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
                if (udpc.token != guid)
                {
                    goto StartConnect;
                }

                break;
            }

            this._reader = new ClientBufferReader();
            Thread t = new Thread(this.ClientWorkThread);//TODO:Convert to async
            t.IsBackground = true;
            t.Start();
        }

        private ClientBufferReader _reader;

        private void ClientWorkThread ()
        {
            try
            {
                while (true)
                {
                    //TODO:when timeout , try to send idle message
                    byte[] data = this._udp.Receive(this.SessionId, TimeSpan.FromSeconds(40));
                    if (data == null)
                    {
                        if (!this._reader._removed)
                        {
                            Console.WriteLine("UDPClientStream.ClientWorkThread.Timeout-40");
                        }

                        this._reader.OnRemoved();

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
            int rc = this._reader.Read(buffer, offset, count, TimeSpan.FromSeconds(55));
            //Console.WriteLine("UDPClientStream read " + rc);
            return rc;
        }

        public override void Flush ()
        {

        }
        public override void Close ()
        {
            if (this._reader != null)
            {
                this._reader.OnRemoved();
            }

            base.Close();
        }



    }



}