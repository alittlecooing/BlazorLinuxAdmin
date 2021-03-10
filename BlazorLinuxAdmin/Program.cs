namespace BlazorLinuxAdmin
{
    using System;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.Extensions.Hosting;

    public class Program
    {
        private static string _pname;

        public static void Main (string[] args)
        {
            _pname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            Console.WriteLine("Process Name : " + _pname);
            Console.WriteLine("CommandLine : " + Environment.CommandLine);
            CreateHostBuilder(args).Build().Run();
        }

        public static bool IsKestrelMode () =>
            _pname != "w3wp"
            && _pname != "iisexpress"
            && (_pname == "dotnet"
                || Environment.OSVersion.Platform == PlatformID.Unix
                || _pname == typeof(Program).Assembly.GetName().Name);

        public static IHostBuilder CreateHostBuilder (string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    if (IsKestrelMode())
                    {
                        webBuilder.UseUrls("http://*:6011");
                        //webBuilder.UseKestrel((wbc, kso) =>
                        //{
                        //    //wbc.ListenAnyIP(6001);
                        //    kso.ListenAnyIP(6011);
                        //    //kso.ListenAnyIP(6012,lo=>
                        //    //{
                        //    //    lo.UseHttps();
                        //    //});
                        //});
                        // webBuilder.UseUrls("http://*:6011", "https://*:6012");
                    }
                    webBuilder.UseStartup<Startup>();
                });
    }
}
