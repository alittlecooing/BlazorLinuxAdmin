namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class TcpMapServerSession : TcpMapBaseSession
    {
        private readonly Stream stream;
        private readonly string sid;
        public TcpMapServerSession (Stream stream, string sid)
        {
            this.stream = stream;
            this.sid = sid;
        }

        private readonly CancellationTokenSource cts_wait_work = new CancellationTokenSource();

        public async Task WorkAsync ()
        {
            try
            {
                await this.WorkAsync(this.stream);
            }
            finally
            {
                this.cts_wait_work.Cancel();
            }
        }

        private Stream sread;
        private Stream swrite;
        private readonly CancellationTokenSource cts_wait_attach = new CancellationTokenSource();

        private async Task WaitForAttachAsync ()
        {
            for (int i = 0; i < 100; i++)
            {
                if (this.sread != null && this.swrite != null)
                {
                    return;
                }

                if (await this.cts_wait_attach.Token.WaitForSignalSettedAsync(100))
                {
                    Debug.Assert(this.sread != null && this.swrite != null);
                    return;
                }
            }
            throw new Exception("timeout");
        }

        protected override async Task<CommandMessage> ReadMessageAsync ()
        {
            if (this.sread == null)
            {
                await this.WaitForAttachAsync();
            }

        ReadAgain:
            var msg = await CommandMessage.ReadFromStreamAsync(this.sread);
            if (msg == null || msg.Name == "data")
            {
                return msg;
            }

            //TcpMapService.LogMessage("ServerSession:get message " + msg);

            switch (msg.Name)
            {
                case "_ping_":
                    await this.swrite.WriteAsync(new CommandMessage("_ping_result_").Pack());
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
            if (this.swrite == null)
            {
                await this.WaitForAttachAsync();
            }

            await this.swrite.WriteAsync(msg.Pack());
        }

        public async Task UseThisSocketAsync (Stream sread, Stream swrite)
        {
            this.sread = sread;
            this.swrite = swrite;
            this.cts_wait_attach.Cancel();
            await this.cts_wait_work.Token.WaitForSignalSettedAsync(-1);
        }
    }
}
