﻿namespace BlazorLinuxAdmin.Pages
{
    using System.Linq;
    using BlazorPlus;

    public partial class FileTree
    {
        private PlusControl fileListPanel;
        private PlusControl previewPanel;

        private void Initialize_Bdt_Div1 (BlazorDomTree bdt)
        {
            string rootFolder = this.GetType().Assembly.Location;
            if (rootFolder.StartsWith("/"))
            {
                rootFolder = "/";       //unix
            }
            else
            {
                rootFolder = rootFolder.Split(':')[0] + ":\\";       //windows
            }

            PlusControl div1 = bdt.Root.Create("div style='width:33%'");
            PlusControl div2 = bdt.Root.Create("div style='width:33%;border-left:solid 1px gray;border-right:solid 1px gray;'");
            PlusControl div3 = bdt.Root.Create("div style='width:33%'");

            div1.Create("div style='font-weight:bold'").InnerText("root " + rootFolder);
            div1.Create("hr style='margin:0.5em 0'");

            div2.Create("div").InnerHTML("Files:");
            div2.Create("hr style='margin:0.5em 0'");
            this.fileListPanel = div2.Create("div style='max-height:400px;overflow:auto'");

            div3.Create("div").InnerHTML("Preview:");
            div3.Create("hr style='margin:0.5em 0'");
            this.previewPanel = div3.Create("div style='max-height:400px;overflow:auto'");

            this.CreateComponentTree(div1.Create("div style='max-height:400px;overflow:auto'"), rootFolder);

            this.ShowFileList(rootFolder);
        }

        private void ShowFileList (string folder)
        {
            this.fileListPanel.ClearChildren();
            this.previewPanel.ClearChildren();

            this.fileListPanel.Create("div style='font-weight:bold'").InnerText(System.IO.Path.Combine(folder));

            string[] files = System.IO.Directory.GetFiles(folder).OrderBy(v => v).ToArray();

            this.fileListPanel.Create("div").InnerText(files.Length + " files");
            this.fileListPanel.Create("hr");

            if (files.Length == 0)
            {
                this.fileListPanel.Create("div style='text-align:center;padding:3px;'").InnerText("<empty>");
                return;
            }

            foreach (string filepath in files)
            {
                string filename = System.IO.Path.GetFileName(filepath);

                PlusControl div = this.fileListPanel.Create("div style='padding:3px;cursor:default;'");
                div.InnerText(filename);
                div.OnClick(delegate
                {
                    this.PreviewFile(filepath);
                });
            }
        }

        private void PreviewFile (string filepath)
        {
            this.previewPanel.ClearChildren();

            string filename = System.IO.Path.GetFileName(filepath);
            var info = new System.IO.FileInfo(filepath);
            this.previewPanel.Create("div style='font-weight:bold'").InnerText(filename);
            this.previewPanel.Create("div").InnerText(info.Length.ToString("###,##0") + " bytes");
            this.previewPanel.Create("hr");

            if (info.Length == 0)
            {
                string text = System.IO.File.ReadAllText(filepath, System.Text.Encoding.UTF8);
                if (string.IsNullOrEmpty(text))
                {
                    this.previewPanel.Create("div").InnerText("<empty/>");
                    return;
                }
                _ = this.previewPanel.Create("div").InnerText(text.Length + " chars");
                this.previewPanel.Create("hr");
                this.previewPanel.Create("div").InnerText(text);
                return;
            }

            if (info.Length < 32768)
            {
                byte[] filedata = null;
                string str;
                try
                {
                    filedata = System.IO.File.ReadAllBytes(filepath);
                    str = new System.IO.StreamReader(new System.IO.MemoryStream(filedata), System.Text.Encoding.UTF8, true).ReadToEnd();
                }
                catch
                {
                    str = System.IO.File.ReadAllText(filepath, System.Text.Encoding.UTF8);
                }

                if (filedata == null || str.Where(c => IsValidChar(c)).Count() > str.Length * 3 / 4)
                {
                    this.previewPanel.Create("div style='white-space:pre-wrap;word-break:break-word;'").InnerText(str);
                }
                else
                {
                    this.previewPanel.Create("div").InnerText(filedata.Length + " bytes");
                    this.previewPanel.Create("div").InnerText("no preview logic for this file yet.");
                }
                return;
            }

            this.previewPanel.Create("div").InnerText("no preview logic for this file yet.");
        }

        private static bool IsValidChar (char c)
        {
            if (c >= 32 && c <= 127)
            {
                return true;
            }
            if (c > 255)
            {
                return true;
            }
            switch (c)
            {
                case '\r':
                case '\n':
                case '\t':
                case ' ':
                    return true;
            }

            //BlazorSession.Current.ConsoleLog(c + ":" + (int)c);
            return false;
        }

        private void CreateComponentTree (PlusControl div, string parentfolder)
        {
            PlusControl table = div.Create("table style='cursor:default;'");

            foreach (string subfolder in System.IO.Directory.GetDirectories(parentfolder).OrderBy(v => v))
            {
                string foldername = System.IO.Path.GetFileName(subfolder);

                PlusControl tr = table.Create("tr");
                PlusControl td0 = tr.Create("td style='width:20px;'");
                PlusControl icon = td0.Create("span class='oi oi-plus'");
                PlusControl spantext = tr.Create("td").Create("span").InnerText(foldername);

                PlusControl tr1 = table.Create("tr style='display:none'");
                tr1.Create("td");
                PlusControl subtd = tr1.Create("td");

                void Toggle ()
                {
                    if (tr1.GetStyle("display") == "none")
                    {
                        if (subtd.GetChildCount() == 0)
                        {
                            this.CreateComponentTree(subtd, subfolder);/*Recursive*/
                        }

                        icon.CssClass("oi oi-minus");
                        tr1.SetStyle("display", "");
                    }
                    else
                    {
                        icon.CssClass("oi oi-plus");
                        tr1.SetStyle("display", "none");
                    }
                }

                //if (parentfolder=="/")Toggle();
                new PlusControl[] { icon, spantext }.OnClick(delegate
                {
                    this.ShowFileList(subfolder);

                    Toggle();
                });
            }

            if (table.GetChildCount() == 0)
            {
                string[] files = System.IO.Directory.GetFiles(parentfolder);
                if (files.Length != 0)
                {
                    table.Create("tr").Create("td").InnerText("<" + files.Length + " files>");
                }
                else
                {
                    table.Create("tr").Create("td").InnerText("<empty>");
                }
            }
        }
    }
}