using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace BlazorLinuxAdmin
{
    public class Program
    {
        private static string pname;

        public static void Main (string[] args)
        {
            pname = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
            Console.WriteLine("Process Name : " + pname);
            Console.WriteLine("CommandLine : " + Environment.CommandLine);
            CreateHostBuilder(args).Build().Run();
        }

        public static bool IsKestrelMode ()
        {
            if (pname == "w3wp" || pname == "iisexpress")//IIS or IIS Express
            {
                return false;
            }

            if (pname == "dotnet")  //run directly
            {
                return true;
            }

            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                return true;
            }

            if (pname == typeof(Program).Assembly.GetName().Name)
            {
                return true;
            }

            return false;
        }

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
