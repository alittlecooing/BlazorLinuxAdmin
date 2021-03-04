namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class TcpMapServerSession : TcpMapBaseSession
    {
        private Stream _stream;
        private string _sid;
        public TcpMapServerSession (Stream stream, string sid)
        {
            this._stream = stream;
            this._sid = sid;
        }

        private readonly CancellationTokenSource cts_wait_work = new CancellationTokenSource();

        public async Task WorkAsync ()
        {
            try
            {
                await this.WorkAsync(this._stream);
            }
            finally
            {
                this.cts_wait_work.Cancel();
            }
        }

        private Stream _sread;
        private Stream _swrite;
        private readonly CancellationTokenSource cts_wait_attach = new CancellationTokenSource();

        private async Task WaitForAttachAsync ()
        {
            for (int i = 0; i < 100; i++)
            {
                if (this._sread != null && this._swrite != null)
                {
                    return;
                }

                if (await this.cts_wait_attach.Token.WaitForSignalSettedAsync(100))
                {
                    Debug.Assert(this._sread != null && this._swrite != null);
                    return;
                }
            }
            throw new Exception("timeout");
        }

        protected override async Task<CommandMessage> ReadMessageAsync ()
        {
            if (this._sread == null)
            {
                await this.WaitForAttachAsync();
            }

        ReadAgain:
            var msg = await CommandMessage.ReadFromStreamAsync(this._sread);
            if (msg == null || msg.Name == "data")
            {
                return msg;
            }

            //TcpMapService.LogMessage("ServerSession:get message " + msg);

            switch (msg.Name)
            {
                case "_ping_":
                    await this._swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
                    break;
                case "_ping_result_":
                    break;
                default:
                    TcpMapService.LogMessage("Error: 6 Ignore message " + msg);
                    break;
            }
            goto ReadAgain;
        }

        protected override async Task WriteMessageAsync (CommandMessage msg)
        {
            if (this._swrite == null)
            {
                await this.WaitForAttachAsync();
            }

            await this._swrite.WriteAsync(msg.Pack());
        }

        public async Task UseThisSocketAsync (Stream sread, Stream swrite)
        {
            this._sread = sread;
            this._swrite = swrite;
            this.cts_wait_attach.Cancel();
            await this.cts_wait_work.Token.WaitForSignalSettedAsync(-1);
        }
    }
}
