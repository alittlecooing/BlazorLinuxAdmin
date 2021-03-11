namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    public abstract class TcpMapBaseSession : TcpMapBaseWorker
    {
        protected async Task WorkAsync (Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentException("stream");
            }

            var t1 = this.StreamToSesionAsync(stream);
            var t2 = this.SessionToStreamAsync(stream);
            await Task.WhenAny(t1, t2);
        }

        private async Task StreamToSesionAsync (Stream stream)
        {
            try
            {
                byte[] buffer = new byte[TcpMapService.defaultBufferSize];//TODO:const
                while (true)
                {
                    //TcpMapService.LogMessage(this.GetType().Name + " ReadAsync");
                    int rc = await stream.ReadAsync(buffer, 0, buffer.Length);
                    //TcpMapService.LogMessage(this.GetType().Name + " ReadAsync DONE : " + rc);
                    if (rc == 0)
                    {
                        return;
                    }

                    CommandMessage msg = new CommandMessage
                    {
                        Name = "data",
                        Data = new Memory<byte>(buffer, 0, rc)
                    };
                    await this.WriteMessageAsync(msg);
                    //TcpMapService.LogMessage(this.GetType().Name + " WriteMessageAsync DONE : " + msg);
                }
            }
            catch (Exception)
            {
                //TcpMapService.OnError(x);
            }
            finally
            {
                //TcpMapService.LogMessage(this.GetType().Name + " EXITING StreamToSesionAsync");
            }
        }

        private async Task SessionToStreamAsync (Stream stream)
        {
            try
            {
                while (true)
                {
                    //TcpMapService.LogMessage(this.GetType().Name + " ReadMessageAsync");
                    var msg = await this.ReadMessageAsync();
                    if (msg == null)
                    {
                        return;
                    }

                    if (msg.Name == "data")
                    {
                        //TcpMapService.LogMessage(this.GetType().Name + " ReadMessageAsync DONE : " + msg);
                        //TcpMapService.LogMessage("Data:" + Encoding.UTF8.GetString(msg.Data.ToArray()));
                        await stream.WriteAsync(msg.Data);
                    }
                    else
                    {
                        TcpMapService.LogMessage("Warning:this message shall not pass to BaseSession : " + msg);
                    }
                    //TcpMapService.LogMessage(this.GetType().Name + " WriteAsync DONE : " + msg);
                }
            }
            catch (ObjectDisposedException)
            {
                //no log
            }
            catch (Exception)
            {
                //TcpMapService.OnError(x);
            }
            finally
            {
                //TcpMapService.LogMessage(this.GetType().Name + " EXITING SessionToStreamAsync");
            }
        }

        protected abstract Task<CommandMessage> ReadMessageAsync ();
        protected abstract Task WriteMessageAsync (CommandMessage msg);
    }
}
