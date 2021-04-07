namespace BlazorLinuxAdmin.Components
{
    using System;
    using System.IO;
    using System.Text;
    using AntDesign;
    using Microsoft.AspNetCore.Components;

    public partial class FilePreview
    {
        public MessageService MessageService { get; set; }

        [Parameter]
        public FileInfo MyFile { get; set; }

        private bool IsImage() => this.MyFile.FullName.EndsWith(".png")
                || this.MyFile.FullName.EndsWith(".jpg")
                || this.MyFile.FullName.EndsWith(".jpeg");

        private string GetImage()
        {
            using FileStream fs = this.MyFile.OpenRead();
            byte[] b = new byte[fs.Length];
            fs.Read(b, 0, b.Length);
            string str = "data:image/" + this.MyFile.Extension + ";base64," + Convert.ToBase64String(b);
            return str;
        }

        private string FileContent()
        {
            string fileContent = "";
            using FileStream fs = this.MyFile.OpenRead();
            byte[] b = new byte[1024];
            UTF8Encoding temp = new UTF8Encoding(true);

            while (fs.Read(b, 0, b.Length) > 0)
            {
                fileContent += temp.GetString(b);
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
