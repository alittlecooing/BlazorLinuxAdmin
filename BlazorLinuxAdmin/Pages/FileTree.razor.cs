namespace BlazorLinuxAdmin.Pages
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using AntDesign;
    using BlazorLinuxAdmin.Model;
    using Microsoft.AspNetCore.Components;
    using Microsoft.AspNetCore.Components.Rendering;

    public partial class FileTree
    {
        [Parameter]
        public string RootFolder { get; set; }

        private string SearchKey { get; set; }

        private List<MyDirectory> MyDirectories { get; set; }

        [Inject]
        private MessageService MessageService { get; set; }

        protected override async Task OnInitializedAsync()
        {
            RootFolder = this.GetType().Assembly.Location;
            if (RootFolder.StartsWith("/"))
            {
                RootFolder = "/";       //unix
            }
            else
            {
                RootFolder = RootFolder.Split(':')[0] + ":\\";       //windows
            }

            this.InitFileList(RootFolder);

            await base.OnInitializedAsync();
        }

        private void InitFileList(string folder)
        {
            this.RootFolder = Path.Combine(folder);
            this.MyDirectories = Directory.GetDirectories(folder).OrderBy(v => v).ToList().ConvertAll(o => new MyDirectory { Name = new DirectoryInfo(o).Name, FullName = new DirectoryInfo(o).FullName, Icon = "folder", Type = "folder" });
            this.MyDirectories.AddRange(Directory.GetFiles(folder).OrderBy(v => v).ToList().ConvertAll(o => new MyDirectory { Name = new FileInfo(o).Name, FullName = new FileInfo(o).FullName, Icon = "file", Type = "file" }));
        }

        private Task OnLoadDirectoryAsync(TreeEventArgs<MyDirectory> args)
        {
            try
            {
                var dataItem = args.Node.DataItem;
                dataItem.Children.Clear();
                dataItem.Children = Directory.GetDirectories(dataItem.FullName).OrderBy(v => v).ToList().ConvertAll(o => new MyDirectory { Name = new DirectoryInfo(o).Name, FullName = new DirectoryInfo(o).FullName, Icon = "folder", Type = "folder" });
                dataItem.Children.AddRange(Directory.GetFiles(dataItem.FullName).OrderBy(v => v).ToList().ConvertAll(o => new MyDirectory { Name = new FileInfo(o).Name, FullName = new FileInfo(o).FullName, Icon = "file", Type = "file" }));
            }
            catch (System.UnauthorizedAccessException)
            {
                this.MessageService.Error("没有权限访问该文件夹！");
            }
            return Task.CompletedTask;
        }
    }
}
