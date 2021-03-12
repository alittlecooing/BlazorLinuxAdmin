namespace BlazorLinuxAdmin.TcpMaps.UDP
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public enum UDPPackageType : long
    {
        Unknown = 0
            ,
        SessionConnect, SessionPrepair, SessionConfirm, SessionReady, SessionClose, SessionIdle, SessionError, DataPost, DataRead, DataMiss, DataPing
    }
    public class UDPConnectJson
    {
        public string Token { get; set; }
        public string Code { get; set; }

        public static UDPConnectJson Deserialize (string expr)
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<UDPConnectJson>(expr);
            }
            catch (Exception)
            {
                //Console.WriteLine("Error: UDPConnectJson Deserialize " + expr);
                throw;
            }
        }
    }
    public static class UDPMeta
    {
        public const int Best_UDP_Size = 1350;
        private const byte _pt0 = 31;
        private const byte _pt1 = 41;
        private const byte _pt2 = 59;
        private const byte _pt3 = 26;
        private const byte _pt4 = 53;
        private const byte _pt5 = 58;
        private const byte _pt6 = 97;

        public static byte[] CreateSessionConnect (long sessionid, string token)
        {

            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { Token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionConnect);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionPrepair (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { Token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionPrepair);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionConfirm (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { Token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionConfirm);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionReady (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { Token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionReady);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionError (long sessionid, string code)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { Code = code }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionError);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionIdle (long sessionid)
        {
            byte[] allbuff = new byte[16];
            SetPackageType(allbuff, UDPPackageType.SessionIdle);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            return allbuff;
        }

        public static byte[] CreateDataPost (long sessionid, long readminindex, long dataindex, byte[] data, int offset, int count)
        {
            byte[] allbuff = new byte[32 + count];
            SetPackageType(allbuff, UDPPackageType.DataPost);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(dataindex), 0, allbuff, 24, 8);
            Buffer.BlockCopy(data, offset, allbuff, 32, count);
            return allbuff;
        }

        public static void UpdateDataRead (byte[] allbuff, long readminindex) => Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);

        public static byte[] CreateDataRead (long sessionid, long readminindex, long dataindex)
        {
            byte[] allbuff = new byte[32];
            SetPackageType(allbuff, UDPPackageType.DataRead);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(dataindex), 0, allbuff, 24, 8); ;
            return allbuff;
        }

        public static byte[] CreateDataMiss (long sessionid, long readminindex, long[] dataindexes)
        {
            byte[] allbuff = new byte[24 + dataindexes.Length * 8];
            SetPackageType(allbuff, UDPPackageType.DataMiss);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);
            for (int i = 0; i < dataindexes.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(dataindexes[i]), 0, allbuff, 24 + i * 8, 8);
            }
            return allbuff;
        }

        public static byte[] CreateDataPing (long sessionid, long readminindex, long maxdataindex)
        {
            byte[] allbuff = new byte[32];
            SetPackageType(allbuff, UDPPackageType.DataPing);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(maxdataindex), 0, allbuff, 24, 8); ;
            return allbuff;
        }

        public static UDPPackageType GetPackageType (byte[] buff)
        {
            if (buff[0] == _pt0 && buff[1] == _pt1 && buff[2] == _pt2 && buff[3] == _pt3
                && buff[4] == _pt4 && buff[5] == _pt5 && buff[6] == _pt6)
            {
                long v = buff[7];
                return (UDPPackageType)v;
            }
            return UDPPackageType.Unknown;
        }

        public static void SetPackageType (byte[] buff, UDPPackageType type)
        {
            buff[0] = _pt0; buff[1] = _pt1; buff[2] = _pt2; buff[3] = _pt3; buff[4] = _pt4; buff[5] = _pt5; buff[6] = _pt6;
            buff[7] = (byte)type;
        }
    }

    public abstract class UDPBaseStream : Stream
    {
        private bool closed = false;
        private Timer timer;

        public abstract long SessionId { get; internal set; }
        protected abstract void SendToPeer (byte[] data);
        protected abstract void OnPost (byte[] data);
        protected abstract void OnPost (byte[] data, int offset, int count);

        protected void ProcessUDPPackage (UDPPackageType pt, byte[] data)
        {
            //UDPPackageType pt = UDPMeta.GetPackageType(data);
            long sid = BitConverter.ToInt64(data, 8);

            //Console.WriteLine("ProcessUDPPackage:" + sid + ":" + pt);

            if (pt == UDPPackageType.SessionError)
            {
                this.Close();
            }
            else if (pt == UDPPackageType.SessionClose)
            {
                this.Close();
            }

            if (pt == UDPPackageType.DataPost)
            {
                if (this.closed)
                {
                    this.SendToPeer(UDPMeta.CreateSessionError(this.SessionId, "closed"));
                    return;
                }

                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this.postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                long dataindex = BitConverter.ToInt64(data, 24);

                //Console.WriteLine("DataPost:" + dataindex);

                if (dataindex == this.readminindex + 1)
                {
                    this.OnPost(data, 32, data.Length - 32);
                    this.readminindex = dataindex;
                    while (this.buffmap.TryGetValue(this.readminindex + 1, out byte[] buff))
                    {
                        this.readminindex++;
                        this.buffmap.Remove(this.readminindex);
                        this.OnPost(buff);
                    }
                    this.SendToPeer(UDPMeta.CreateDataRead(this.SessionId, this.readminindex, dataindex));
                }
                else
                {
                    if (dataindex > this.readmaxindex)
                    {
                        this.readmaxindex = dataindex;
                    }

                    byte[] buff = new byte[data.Length - 32];
                    Buffer.BlockCopy(data, 32, buff, 0, buff.Length);
                    this.buffmap[dataindex] = buff;
                }
            }
            if (pt == UDPPackageType.DataRead)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                long dataindex = BitConverter.ToInt64(data, 24);
                lock (this.postmap)
                {
                    this.PostMapRemove(dataindex);
                    this.OnGetPeerMinRead(peerminreadindex);
                }
            }
            if (pt == UDPPackageType.DataMiss)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this.postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                int misscount = (data.Length - 24) / 8;
                List<DataItem> list = null;
                for (int missid = 0; missid < misscount; missid++)
                {
                    long dataindex = BitConverter.ToInt64(data, 24 + missid * 8);
                    DataItem item = null;
                    lock (this.postmap)
                    {
                        this.postmap.TryGetValue(dataindex, out item);
                    }
                    if (item != null)
                    {
                        if (list == null)
                        {
                            list = new List<DataItem>();
                        }

                        list.Add(item);
                    }
                }
                if (list != null)
                {
                    list.Sort(delegate (DataItem d1, DataItem d2)
                    {
                        return d1.sendTime.CompareTo(d2.sendTime);
                    });
                    int maxsendagain = 65536;

                    foreach (DataItem item in list)
                    {
                        if (DateTime.Now - item.sendTime < TimeSpan.FromMilliseconds(this.GetPackageLostMS()))
                        {
                            break;
                        }

                        if (maxsendagain < item.udpData.Length)
                        {
                            break;
                        }

                        maxsendagain -= item.udpData.Length;
                        UDPMeta.UpdateDataRead(item.udpData, this.readminindex);
                        item.sendTime = DateTime.Now;
                        item.retryCount++;
                        this.SendToPeer(item.udpData);
                    }
                }
            }

            if (pt == UDPPackageType.DataPing)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this.postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                long maxdataindex = BitConverter.ToInt64(data, 24);
                List<long> misslist = null;
                for (long index = this.readminindex + 1; index <= maxdataindex; index++)
                {
                    if (this.buffmap.ContainsKey(index))
                    {
                        continue;
                    }

                    if (misslist == null)
                    {
                        misslist = new List<long>();
                    }

                    misslist.Add(index);
                    if (misslist.Count * 8 + 40 > UDPMeta.Best_UDP_Size)
                    {
                        break;
                    }
                }
                if (misslist != null)
                {
                    this.SendToPeer(UDPMeta.CreateDataMiss(this.SessionId, this.readminindex, misslist.ToArray()));
                }
                else
                {
                    this.SendToPeer(UDPMeta.CreateDataRead(this.SessionId, this.readminindex, this.readminindex));
                }
            }
        }

        private long peerminreadindex = 0;
        private void OnGetPeerMinRead (long peerminreadindex)
        {
            if (peerminreadindex < this.peerminreadindex)
            {
                return;
            }

            if (this.postmap.Count == 0)
            {
                return;
            }

            List<long> list = null;
            foreach (DataItem item in this.postmap.Values)
            {
                if (item.dataIndex > peerminreadindex)
                {
                    continue;
                }

                if (list == null)
                {
                    list = new List<long>();
                }

                list.Add(item.dataIndex);
            }
            if (list != null)
            {
                foreach (long index in list)
                {
                    this.PostMapRemove(index);
                }
            }
            this.peerminreadindex = peerminreadindex;
        }

        private long readminindex = 0;
        private long readmaxindex = 0;
        private readonly Dictionary<long, byte[]> buffmap = new Dictionary<long, byte[]>();

        public string GetDebugString () => this.buffmap.Count + ":" + this.postmap.Count;

        private long nextdataindex = 0;
        public class DataItem
        {
            public long dataIndex;
            public byte[] udpData;
            public DateTime sendTime;
            public int retryCount;
            public int pingCount;

            public override string ToString () => this.dataIndex + ":" + this.udpData.Length;
        }

        private readonly Dictionary<long, DataItem> postmap = new Dictionary<long, DataItem>();
        private int mindelay = 0;

        private int GetPackageLostMS ()
        {
            int md = this.mindelay == 0 ? 800 : this.mindelay;
            return (int)(md * 1.3);
        }

        private void PostMapRemove (long index)
        {
            if (!this.postmap.TryGetValue(index, out DataItem item))
            {
                return;
            }

            this.postmap.Remove(index);
            if (item.retryCount == 0)
            {
                TimeSpan ts = DateTime.Now - item.sendTime;
                this.mindelay = this.mindelay == 0 ? (int)ts.TotalMilliseconds : Math.Min(this.mindelay, (int)ts.TotalMilliseconds);
            }
        }

        protected static void RunInNewThread (Action handler)
        {
            bool added = ThreadPool.QueueUserWorkItem(delegate
              {
                  handler();
              });
            if (added)
            {
                return;
            }

            Thread t = new Thread(delegate ()
              {
                  handler();
              });
            t.Start();
        }

        public override async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Task<int> t = null;
            Exception error = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            RunInNewThread(delegate
            {
                try
                {
                    t = base.ReadAsync(buffer, offset, count, cancellationToken);
                }
                catch (Exception x)
                {
                    error = x;
                }
                cts.Cancel();
            });
            await cts.Token.WaitForSignalSettedAsync(-1);
            return error != null ? throw new Exception(error.Message, error) : await t;
        }

        //public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        //{
        //	return base.ReadAsync(buffer, cancellationToken);
        //}
        public override async Task WriteAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            //NOTE: new patch for the Async call , Must do this way , don't block caller thread.

            //Console.WriteLine("UDPBaseStream WriteAsync " + count);
            Exception error = null;
            CancellationTokenSource cts = new CancellationTokenSource();
            RunInNewThread(delegate
             {
                 try
                 {
                     //Console.WriteLine("RunInNewThread WriteAsync " + count);
                     this.Write(buffer, offset, count);
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
        }
        //public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        //{
        //	return base.WriteAsync(buffer, cancellationToken);
        //}

        public override void Write (byte[] buffer, int offset, int count)
        {
            if (this.closed)
            {
                throw (new Exception("stream closed"));
            }

            //Console.WriteLine("UDPBaseStream write " + count);

            while (count > UDPMeta.Best_UDP_Size)
            {
                this.Write(buffer, offset, UDPMeta.Best_UDP_Size);
                offset += UDPMeta.Best_UDP_Size;
                count -= UDPMeta.Best_UDP_Size;
            }


            DataItem item = new DataItem
            {
                dataIndex = ++this.nextdataindex
            };

            item.udpData = UDPMeta.CreateDataPost(this.SessionId, this.readminindex, item.dataIndex, buffer, offset, count);

            lock (this.postmap)
            {
                this.postmap[item.dataIndex] = item;
            }

            item.sendTime = DateTime.Now;
            this.SendToPeer(item.udpData);

            this.timerlastitem = item;
            if (this.timer == null)
            {
                this.timer = new Timer(this.OnTimer, null, 100, 100);
            }
        }

        private DataItem timerlastitem;

        private void OnTimer (object argstate)
        {
            if (this.closed)
            {
                this.timer.Dispose();
                return;
            }

            if (this.postmap.Count == 0)
            {
                return;
            }

            if (DateTime.Now - this.timerlastitem.sendTime < TimeSpan.FromMilliseconds(this.GetPackageLostMS()))
            {
                return;
            }

            if (this.timerlastitem.pingCount > 10)
            {
                return;
            }

            this.timerlastitem.pingCount++;

            this.SendToPeer(UDPMeta.CreateDataPing(this.SessionId, this.readminindex, this.timerlastitem.dataIndex));
        }

        protected void OnWorkerThreadExit ()
        {
            if (this.timer != null)
            {
                this.timer.Dispose();
            }

            this.timer = null;
        }

        public override void Flush ()
        {
        }

        public override void Close ()
        {
            this.closed = true;
            if (this.timer != null)
            {
                this.timer.Dispose();
            }

            base.Close();
            if (!this.closesend)
            {
                this.closesend = true;
                this.SendToPeer(UDPMeta.CreateSessionError(this.SessionId, "Close"));
            }
        }

        private bool closesend = false;

        #region Stream
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;


        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek (long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength (long value) => throw new NotSupportedException();
        #endregion
    }

    public class AsyncUtil
    {
        public static CancellationTokenSource CreateAnyCancelSource (CancellationToken ct1)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            Task t1 = Task.Delay(Timeout.InfiniteTimeSpan, ct1);
            t1.ContinueWith(delegate
            {
                cts.Cancel();
            });
            return cts;
        }

        public static CancellationTokenSource CreateAnyCancelSource (params CancellationToken[] ctarr)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            Task[] ts = new Task[ctarr.Length];
            for (int i = 0; i < ctarr.Length; i++)
            {
                ts[i] = Task.Delay(Timeout.InfiniteTimeSpan, ctarr[i]);
            }

            Task.WhenAny(ts).ContinueWith(delegate
            {
                cts.Cancel();
            });
            return cts;
        }

        public static CancellationToken CreateAnyCancelToken (CancellationToken ct1, CancellationToken ct2)
        {
            CancellationTokenSource cts = new CancellationTokenSource();
            Task t1 = Task.Delay(Timeout.InfiniteTimeSpan, ct1);
            Task t2 = Task.Delay(Timeout.InfiniteTimeSpan, ct2);
            Task.WhenAny(t1, t2).ContinueWith(delegate
            {
                cts.Cancel();
            });
            return cts.Token;
        }
    }

    public class AsyncAwaiter
    {
        private readonly CancellationTokenSource cts;

        public AsyncAwaiter () => this.cts = new CancellationTokenSource();
        public AsyncAwaiter (params CancellationToken[] tokens) => this.cts = AsyncUtil.CreateAnyCancelSource(tokens);

        public void Complete () => this.cts.Cancel();

        public bool IsCompleted => this.cts.IsCancellationRequested;

        public async Task WaitAsync ()
        {
            if (this.cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, this.cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }

        public async Task WaitAsync (CancellationToken token)
        {
            if (this.cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                token = AsyncUtil.CreateAnyCancelToken(this.cts.Token, token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                if (this.cts.IsCancellationRequested)
                {
                    return;
                }

                throw;
            }
        }
        public async Task<bool> WaitAsync (TimeSpan timeout)
        {
            if (this.cts.IsCancellationRequested)
            {
                return true;
            }

            try
            {
                await Task.Delay(timeout, this.cts.Token);
            }
            catch (OperationCanceledException)
            {
                return true;
            }
            return false;
        }
        public async Task<bool> WaitAsync (TimeSpan timeout, CancellationToken token)
        {
            if (this.cts.IsCancellationRequested)
            {
                return true;
            }

            try
            {
                token = AsyncUtil.CreateAnyCancelToken(this.cts.Token, token);
                await Task.Delay(timeout, token);
            }
            catch (OperationCanceledException)
            {
                if (this.cts.IsCancellationRequested)
                {
                    return true;
                }

                throw;
            }
            return false;
        }
    }

    public class BufferedReader : IDisposable
    {
        public BufferedReader (CancellationToken token)
        {
            this.cts = AsyncUtil.CreateAnyCancelSource(token);
            this.Timeout = TimeSpan.FromSeconds(55);
        }
        public BufferedReader (CancellationToken token, int capacity)
        {
            this.cts = AsyncUtil.CreateAnyCancelSource(token);
            this.Timeout = TimeSpan.FromSeconds(55);
            this.Capacity = capacity;
        }

        public TimeSpan Timeout { get; set; }
        public int Capacity { get; set; }

        private int bufflen = 0;

        private bool HasCapacity (int count)
        {
            return this.Capacity < 1 || this.bufflen <= this.Capacity;
        }

        private byte[] buff;
        private int bidx;
        private Queue<byte[]> packs = new Queue<byte[]>();

        public bool DataAvailable => this.buff != null || this.packs.Count > 0;

        private readonly CancellationTokenSource cts;
        private AsyncAwaiter waitfor_data;
        //AsyncAwaiter _waitfor_capacity;
        //int _nextcapacitylen = 0;

        public async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
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

                lock (this.packs)
                {
                    this.bufflen -= rl;
                    //if (_nextcapacitylen > 0 && _waitfor_capacity != null && HasCapacity(_nextcapacitylen))
                    //{
                    //	_nextcapacitylen = 0;
                    //	_waitfor_capacity.Complete();
                    //}
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

                if (this.cts.IsCancellationRequested)
                {
                    return rc;
                }

                this.cts.Token.ThrowIfCancellationRequested();
                this.waitfor_data = new AsyncAwaiter();
            }

            await this.waitfor_data.WaitAsync(AsyncUtil.CreateAnyCancelToken(this.cts.Token, cancellationToken));

            goto READBUFF;
        }

        public void PushBuffer (byte[] buff)
        {
            this.cts.Token.ThrowIfCancellationRequested();

            lock (this.packs)
            {
                this.packs.Enqueue(buff);
                this.bufflen += buff.Length;
                if (this.waitfor_data != null)
                {
                    this.waitfor_data.Complete();
                }
            }
        }

        public void PushBuffer (byte[] buffer, int offset, int count)
        {
            byte[] buff = new byte[count];
            Buffer.BlockCopy(buffer, offset, buff, 0, count);
            this.PushBuffer(buff);
        }

        public void Dispose ()
        {
            if (this.cts.IsCancellationRequested)
            {
                return;
            }

            lock (this.packs)
            {
                if (this.cts.IsCancellationRequested)
                {
                    return;
                }

                this.cts.Cancel();
                if (this.waitfor_data != null)
                {
                    this.waitfor_data.Complete();
                }
                //if (_waitfor_capacity != null) _waitfor_capacity.Complete();
            }
        }
    }
}