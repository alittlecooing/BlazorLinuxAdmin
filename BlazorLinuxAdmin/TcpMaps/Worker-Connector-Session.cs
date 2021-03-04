namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    internal class TcpMapConnectorSession : TcpMapBaseSession
    {
        private readonly Stream _stream;

        public TcpMapConnectorSession (Stream stream) => this._stream = stream;

        public async Task WorkAsync () => await this.WorkAsync(this._stream);

        private Stream _sread;
        private Stream _swrite;

        protected override async Task<CommandMessage> ReadMessageAsync ()
        {
        ReadAgain:

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
                    TcpMapService.LogMessage("Error: 3 Ignore message " + msg);
                    break;
            }
            goto ReadAgain;
        }

        protected override async Task WriteMessageAsync (CommandMessage msg) => await this._swrite.WriteAsync(msg.Pack());

        public async Task DirectWorkAsync (Stream sread, Stream swrite)
        {
            this._sread = sread;
            this._swrite = swrite;
            await this.WorkAsync();
        }
    }
}
