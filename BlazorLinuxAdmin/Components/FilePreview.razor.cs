namespace BlazorLinuxAdmin.Components
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using AntDesign;
    using Microsoft.AspNetCore.Components;

    public partial class FilePreview
    {
        public MessageService MessageService { get; set; }

        [Parameter]
        public FileInfo File { get; set; }

        private string FileContent()
        {
            string fileContent = "";
            using (FileStream fs = this.File.OpenRead())
            {
                byte[] b = new byte[1024];
                UTF8Encoding temp = new UTF8Encoding(true);

                while(fs.Read(b, 0, b.Length) > 0)
                {
                    fileContent += temp.GetString(b);
                }
            }

            return ContentFilter(fileContent);
        }

        private string ContentFilter(string content)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }

                return content.Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace(" ", "&nbsp;")
                    .Replace("\r\n", "<br/>")
                    .Replace("\n", "<br/>");
            }
            catch (Exception ex)
            {
                this.MessageService.Error(string.Format(ex.Message));
                return content;
            }
        }
    }
}
