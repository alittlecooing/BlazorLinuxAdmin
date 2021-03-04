using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BlazorLinuxAdmin.TcpMaps.UDP
{
    public enum UDPPackageType : long
    {
        Unknown = 0
            ,
        SessionConnect, SessionPrepair, SessionConfirm, SessionReady, SessionClose, SessionIdle, SessionError, DataPost, DataRead, DataMiss, DataPing
    }
    public class UDPConnectJson
    {
        public string token { get; set; }
        public string code { get; set; }

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
        public const int BestUDPSize = 1350;
        private const byte pt0 = 31;
        private const byte pt1 = 41;
        private const byte pt2 = 59;
        private const byte pt3 = 26;
        private const byte pt4 = 53;
        private const byte pt5 = 58;
        private const byte pt6 = 97;

        public static byte[] CreateSessionConnect (long sessionid, string token)
        {

            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionConnect);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }

        public static byte[] CreateSessionPrepair (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionPrepair);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }
        public static byte[] CreateSessionConfirm (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionConfirm);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }
        public static byte[] CreateSessionReady (long sessionid, string token)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { token = token }));
            byte[] allbuff = new byte[16 + jsonbuff.Length];
            SetPackageType(allbuff, UDPPackageType.SessionReady);
            Buffer.BlockCopy(BitConverter.GetBytes(sessionid), 0, allbuff, 8, 8);
            Buffer.BlockCopy(jsonbuff, 0, allbuff, 16, jsonbuff.Length);
            return allbuff;
        }
        public static byte[] CreateSessionError (long sessionid, string code)
        {
            byte[] jsonbuff = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new UDPConnectJson() { code = code }));
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
        public static void UpdateDataRead (byte[] allbuff, long readminindex)
        {
            Buffer.BlockCopy(BitConverter.GetBytes(readminindex), 0, allbuff, 16, 8);
        }
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
            if (buff[0] == pt0 && buff[1] == pt1 && buff[2] == pt2 && buff[3] == pt3
                && buff[4] == pt4 && buff[5] == pt5 && buff[6] == pt6)
            {
                long v = buff[7];
                return (UDPPackageType)v;
            }
            return UDPPackageType.Unknown;
        }
        public static void SetPackageType (byte[] buff, UDPPackageType type)
        {
            buff[0] = pt0; buff[1] = pt1; buff[2] = pt2; buff[3] = pt3; buff[4] = pt4; buff[5] = pt5; buff[6] = pt6;
            buff[7] = (byte)type;
        }
    }



    public abstract class UDPBaseStream : Stream
    {
        private DateTime _dts = DateTime.Now;
        private bool _closed = false;
        private Timer _timer;

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
                if (this._closed)
                {
                    this.SendToPeer(UDPMeta.CreateSessionError(this.SessionId, "closed"));
                    return;
                }

                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this._postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                long dataindex = BitConverter.ToInt64(data, 24);

                //Console.WriteLine("DataPost:" + dataindex);

                if (dataindex == this._readminindex + 1)
                {
                    this.OnPost(data, 32, data.Length - 32);
                    this._readminindex = dataindex;
                    byte[] buff;
                    while (this._buffmap.TryGetValue(this._readminindex + 1, out buff))
                    {
                        this._readminindex++;
                        this._buffmap.Remove(this._readminindex);
                        this.OnPost(buff);
                    }
                    this.SendToPeer(UDPMeta.CreateDataRead(this.SessionId, this._readminindex, dataindex));
                }
                else
                {
                    if (dataindex > this._readmaxindex)
                    {
                        this._readmaxindex = dataindex;
                    }

                    byte[] buff = new byte[data.Length - 32];
                    Buffer.BlockCopy(data, 32, buff, 0, buff.Length);
                    this._buffmap[dataindex] = buff;
                }

            }
            if (pt == UDPPackageType.DataRead)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                long dataindex = BitConverter.ToInt64(data, 24);
                lock (this._postmap)
                {
                    this.PostMapRemove(dataindex);
                    this.OnGetPeerMinRead(peerminreadindex);
                }
            }
            if (pt == UDPPackageType.DataMiss)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this._postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                int misscount = (data.Length - 24) / 8;
                List<DataItem> list = null;
                for (int missid = 0; missid < misscount; missid++)
                {
                    long dataindex = BitConverter.ToInt64(data, 24 + missid * 8);
                    DataItem item = null;
                    lock (this._postmap)
                    {
                        this._postmap.TryGetValue(dataindex, out item);
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
                        return d1.SendTime.CompareTo(d2.SendTime);
                    });
                    int maxsendagain = 65536;

                    foreach (DataItem item in list)
                    {
                        if (DateTime.Now - item.SendTime < TimeSpan.FromMilliseconds(this.GetPackageLostMS()))
                        {
                            break;
                        }

                        if (maxsendagain < item.UDPData.Length)
                        {
                            break;
                        }

                        maxsendagain -= item.UDPData.Length;
                        UDPMeta.UpdateDataRead(item.UDPData, this._readminindex);
                        item.SendTime = DateTime.Now;
                        item.RetryCount++;
                        this.SendToPeer(item.UDPData);
                    }
                }
            }
            if (pt == UDPPackageType.DataPing)
            {
                long peerminreadindex = BitConverter.ToInt64(data, 16);
                lock (this._postmap)
                {
                    this.OnGetPeerMinRead(peerminreadindex);
                }

                long maxdataindex = BitConverter.ToInt64(data, 24);
                List<long> misslist = null;
                for (long index = this._readminindex + 1; index <= maxdataindex; index++)
                {
                    if (this._buffmap.ContainsKey(index))
                    {
                        continue;
                    }

                    if (misslist == null)
                    {
                        misslist = new List<long>();
                    }

                    misslist.Add(index);
                    if (misslist.Count * 8 + 40 > UDPMeta.BestUDPSize)
                    {
                        break;
                    }
                }
                if (misslist != null)
                {
                    this.SendToPeer(UDPMeta.CreateDataMiss(this.SessionId, this._readminindex, misslist.ToArray()));
                }
                else
                {
                    this.SendToPeer(UDPMeta.CreateDataRead(this.SessionId, this._readminindex, this._readminindex));
                }
            }
        }

        private long _peerminreadindex = 0;
        private void OnGetPeerMinRead (long peerminreadindex)
        {
            if (peerminreadindex < this._peerminreadindex)
            {
                return;
            }

            if (this._postmap.Count == 0)
            {
                return;
            }

            List<long> list = null;
            foreach (DataItem item in this._postmap.Values)
            {
                if (item.DataIndex > peerminreadindex)
                {
                    continue;
                }

                if (list == null)
                {
                    list = new List<long>();
                }

                list.Add(item.DataIndex);
            }
            if (list != null)
            {
                foreach (long index in list)
                {
                    this.PostMapRemove(index);
                }
            }
            this._peerminreadindex = peerminreadindex;
        }

        private long _readminindex = 0;
        private long _readmaxindex = 0;
        private Dictionary<long, byte[]> _buffmap = new Dictionary<long, byte[]>();

        public string GetDebugString ()
        {
            return this._buffmap.Count + ":" + this._postmap.Count;
        }

        private long nextdataindex = 0;
        public class DataItem
        {
            public long DataIndex;
            public byte[] UDPData;
            public DateTime SendTime;
            public int RetryCount;
            public int PingCount;

            public override string ToString ()
            {
                return this.DataIndex + ":" + this.UDPData.Length;
            }
        }

        private Dictionary<long, DataItem> _postmap = new Dictionary<long, DataItem>();
        private int _mindelay = 0;

        private int GetPackageLostMS ()
        {
            int md = this._mindelay == 0 ? 800 : this._mindelay;
            return (int)(md * 1.3);
        }

        private void PostMapRemove (long index)
        {
            DataItem item;
            if (!this._postmap.TryGetValue(index, out item))
            {
                return;
            }

            this._postmap.Remove(index);
            if (item.RetryCount == 0)
            {
                TimeSpan ts = DateTime.Now - item.SendTime;
                if (this._mindelay == 0)
                {
                    this._mindelay = (int)ts.TotalMilliseconds;
                }
                else
                {
                    this._mindelay = Math.Min(this._mindelay, (int)ts.TotalMilliseconds);
                }
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
            if (error != null)
            {
                throw new Exception(error.Message, error);
            }

            return await t;
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
            if (this._closed)
            {
                throw (new Exception("stream closed"));
            }

            //Console.WriteLine("UDPBaseStream write " + count);

            while (count > UDPMeta.BestUDPSize)
            {
                this.Write(buffer, offset, UDPMeta.BestUDPSize);
                offset += UDPMeta.BestUDPSize;
                count -= UDPMeta.BestUDPSize;
            }


            DataItem item = new DataItem();
            item.DataIndex = ++this.nextdataindex;

            item.UDPData = UDPMeta.CreateDataPost(this.SessionId, this._readminindex, item.DataIndex, buffer, offset, count);

            lock (this._postmap)
            {
                this._postmap[item.DataIndex] = item;
            }

            item.SendTime = DateTime.Now;
            this.SendToPeer(item.UDPData);

            this._timerlastitem = item;
            if (this._timer == null)
            {
                this._timer = new Timer(this.OnTimer, null, 100, 100);
            }
        }

        private DataItem _timerlastitem;

        private void OnTimer (object argstate)
        {
            if (this._closed)
            {
                this._timer.Dispose();
                return;
            }

            if (this._postmap.Count == 0)
            {
                return;
            }

            if (DateTime.Now - this._timerlastitem.SendTime < TimeSpan.FromMilliseconds(this.GetPackageLostMS()))
            {
                return;
            }

            if (this._timerlastitem.PingCount > 10)
            {
                return;
            }

            this._timerlastitem.PingCount++;

            this.SendToPeer(UDPMeta.CreateDataPing(this.SessionId, this._readminindex, this._timerlastitem.DataIndex));
        }

        protected void OnWorkerThreadExit ()
        {
            if (this._timer != null)
            {
                this._timer.Dispose();
            }

            this._timer = null;
        }

        public override void Flush ()
        {

        }


        public override void Close ()
        {
            this._closed = true;
            if (this._timer != null)
            {
                this._timer.Dispose();
            }

            base.Close();
            if (!this._closesend)
            {
                this._closesend = true;
                this.SendToPeer(UDPMeta.CreateSessionError(this.SessionId, "Close"));
            }
        }

        private bool _closesend = false;

        #region Stream
        public override bool CanRead
        {
            get { return true; }
        }

        public override bool CanSeek
        {
            get { return false; }
        }

        public override bool CanWrite
        {
            get { return true; }
        }


        public override long Length
        {
            get { throw new NotSupportedException(); }
        }

        public override long Position
        {
            get
            {
                throw new NotSupportedException();
            }
            set
            {
                throw new NotSupportedException();
            }
        }

        public override long Seek (long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength (long value)
        {
            throw new NotSupportedException();
        }
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
        private CancellationTokenSource _cts;

        public AsyncAwaiter ()
        {
            this._cts = new CancellationTokenSource();
        }
        public AsyncAwaiter (params CancellationToken[] tokens)
        {
            this._cts = AsyncUtil.CreateAnyCancelSource(tokens);
        }


        public void Complete ()
        {
            this._cts.Cancel();
        }

        public bool IsCompleted
        {
            get
            {
                return this._cts.IsCancellationRequested;
            }
        }

        public async Task WaitAsync ()
        {
            if (this._cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, this._cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }
        public async Task WaitAsync (CancellationToken token)
        {
            if (this._cts.IsCancellationRequested)
            {
                return;
            }

            try
            {
                token = AsyncUtil.CreateAnyCancelToken(this._cts.Token, token);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException)
            {
                if (this._cts.IsCancellationRequested)
                {
                    return;
                }

                throw;
            }
        }
        public async Task<bool> WaitAsync (TimeSpan timeout)
        {
            if (this._cts.IsCancellationRequested)
            {
                return true;
            }

            try
            {
                await Task.Delay(timeout, this._cts.Token);
            }
            catch (OperationCanceledException)
            {
                return true;
            }
            return false;
        }
        public async Task<bool> WaitAsync (TimeSpan timeout, CancellationToken token)
        {
            if (this._cts.IsCancellationRequested)
            {
                return true;
            }

            try
            {
                token = AsyncUtil.CreateAnyCancelToken(this._cts.Token, token);
                await Task.Delay(timeout, token);
            }
            catch (OperationCanceledException)
            {
                if (this._cts.IsCancellationRequested)
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
            this._cts = AsyncUtil.CreateAnyCancelSource(token);
            this.Timeout = TimeSpan.FromSeconds(55);
        }
        public BufferedReader (CancellationToken token, int capacity)
        {
            this._cts = AsyncUtil.CreateAnyCancelSource(token);
            this.Timeout = TimeSpan.FromSeconds(55);
            this.Capacity = capacity;
        }

        public TimeSpan Timeout { get; set; }
        public int Capacity { get; set; }

        private int _bufflen = 0;

        private bool HasCapacity (int count)
        {
            return this.Capacity < 1 || this._bufflen <= this.Capacity;
        }

        private byte[] _buff;
        private int _bidx;
        private Queue<byte[]> _packs = new Queue<byte[]>();


        public bool DataAvailable
        {
            get
            {
                return this._buff != null || this._packs.Count > 0;
            }
        }

        private CancellationTokenSource _cts;
        private AsyncAwaiter _waitfor_data;
        //AsyncAwaiter _waitfor_capacity;
        //int _nextcapacitylen = 0;


        public async Task<int> ReadAsync (byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
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

                lock (this._packs)
                {
                    this._bufflen -= rl;
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

                if (this._cts.IsCancellationRequested)
                {
                    return rc;
                }

                this._cts.Token.ThrowIfCancellationRequested();
                this._waitfor_data = new AsyncAwaiter();
            }

            await this._waitfor_data.WaitAsync(AsyncUtil.CreateAnyCancelToken(this._cts.Token, cancellationToken));

            goto READBUFF;

        }


        public void PushBuffer (byte[] buff)
        {
            this._cts.Token.ThrowIfCancellationRequested();

            lock (this._packs)
            {
                this._packs.Enqueue(buff);
                this._bufflen += buff.Length;
                if (this._waitfor_data != null)
                {
                    this._waitfor_data.Complete();
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
            if (this._cts.IsCancellationRequested)
            {
                return;
            }

            lock (this._packs)
            {
                if (this._cts.IsCancellationRequested)
                {
                    return;
                }

                this._cts.Cancel();
                if (this._waitfor_data != null)
                {
                    this._waitfor_data.Complete();
                }
                //if (_waitfor_capacity != null) _waitfor_capacity.Complete();

            }
        }
    }


}