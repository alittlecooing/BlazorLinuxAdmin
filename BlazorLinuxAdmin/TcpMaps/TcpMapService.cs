﻿namespace BlazorLinuxAdmin.TcpMaps
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Microsoft.Extensions.DependencyInjection;

    public static class TcpMapService
    {
        public static int defaultBufferSize = 1024 * 128;
        public static string DataFolder { get; set; }
        public static bool IsServiceAdded { get; private set; }
        //public static bool IsServiceMapped { get; private set; }
        public static bool IsServiceRunning { get; private set; }
        public static bool IsUDPServiceRunning { get; private set; }
        public static Exception ServiceError { get; private set; }
        public static ConcurrentQueue<Exception> ServiceErrors { get; } = new ConcurrentQueue<Exception>();

        public static void OnError (Exception err)
        {
            Console.WriteLine(err);
            ServiceError = err;
            ServiceErrors.Enqueue(err);
            while (ServiceErrors.Count > 100)
            {
                ServiceErrors.TryDequeue(out _);
            }

            ErrorOccurred?.Invoke(err);
        }

        public static event Action<Exception> ErrorOccurred;
        public static ConcurrentQueue<string> logMessages = new ConcurrentQueue<string>();

        public static void LogMessage (string msg)
        {
            Console.WriteLine(msg);
            logMessages.Enqueue(msg);
            while (logMessages.Count > 200)
            {
                logMessages.TryDequeue(out _);
            }

            MessageLogged?.Invoke(msg);
        }

        public static event Action<string> MessageLogged;

        public static void AddTcpMaps (IServiceCollection services)
        {
            IsServiceAdded = true;
            StartService();
            StartUDPService();
        }

        private static UdpClient _udp6023;

        private static void StartUDPService ()
        {
            _udp6023 = new UdpClient(new IPEndPoint(IPAddress.Any, 6023));
            _ = WorkUDPAsync();
            IsUDPServiceRunning = true;
        }

        private static async Task WorkUDPAsync ()
        {
            while (IsServiceRunning)
            {
                try
                {
                    var result = await _udp6023.ReceiveAsync();
                    byte[] data = System.Text.Encoding.ASCII.GetBytes("UDP=" + result.RemoteEndPoint);
                    _ = _udp6023.SendAsync(data, data.Length, result.RemoteEndPoint);
                }
                catch (Exception x)
                {
                    OnError(x);
                }
            }
            _udp6023.Dispose();
            _udp6023 = null;
        }

        private static TcpListener _listener6022;

        private static void StartService ()
        {
            try
            {
                _listener6022 = new TcpListener(IPAddress.Any, 6022);
                _listener6022.Start();

                if (string.IsNullOrEmpty(DataFolder))
                {
                    DataFolder = Path.Combine(Directory.GetCurrentDirectory(), "data_tcpmaps");
                }

                if (!Directory.Exists(DataFolder))
                {
                    Directory.CreateDirectory(DataFolder);
                }

                {
                    foreach (string jsonfilepath in Directory.GetFiles(DataFolder, "*.json"))
                    {
                        string shortname = Path.GetFileName(jsonfilepath);
                        try
                        {
                            if (shortname.StartsWith("TcpMapClient_"))
                            {
                                var mapclient = JsonSerializer.Deserialize<TcpMapClient>(File.ReadAllText(jsonfilepath));
                                AddStartClient(mapclient);
                            }
                            if (shortname.StartsWith("TcpMapServer_"))
                            {
                                var mapserver = JsonSerializer.Deserialize<TcpMapServer>(File.ReadAllText(jsonfilepath));
                                AddStartServer(mapserver);
                            }
                            if (shortname.StartsWith("TcpMapConnector_"))
                            {
                                var mapconnector = JsonSerializer.Deserialize<TcpMapConnector>(File.ReadAllText(jsonfilepath));
                                AddStartConnector(mapconnector);
                            }
                        }
                        catch (Exception x) { OnError(new Exception("Failed process " + shortname + " , " + x.Message, x)); }
                    }
                }
            }
            catch (Exception x)
            {
                OnError(x);
                if (_listener6022 != null)
                {
                    _listener6022.Stop();
                    _listener6022 = null;
                }
                return;
            }

            IsServiceRunning = true;
            _ = Task.Run(AcceptWorkAsync);
        }

        private static async Task AcceptWorkAsync ()
        {
            try
            {
                while (true)
                {
                    var socket = await _listener6022.AcceptSocketAsync();
                    _ = Task.Run(async delegate
                      {
                          LogMessage("Warning:accept server " + socket.LocalEndPoint + "," + socket.RemoteEndPoint);
                          try
                          {
                              socket.InitTcp();
                              await ProcesSocketAsync(socket);
                          }
                          catch (Exception x)
                          {
                              OnError(x);
                          }
                          finally
                          {
                              LogMessage("Warning:close server " + socket.LocalEndPoint + "," + socket.RemoteEndPoint);
                              socket.CloseSocket();
                          }
                      });
                }
            }
            catch (Exception x)
            {
                OnError(x);
            }
        }

        private static async Task ProcesSocketAsync (Socket socket)
        {

            while (true)
            {
                var msg = await CommandMessage.ReadFromSocketAsync(socket);

                if (msg == null)
                {
                    //LogMessage("no message ? Connected:" + socket.Connected);
                    return;
                }

                //LogMessage("new msg : " + msg);

                switch (msg.Name)
                {
                    case "ClientConnect":
                    case "SessionConnect":
                        await TcpMapServerClient.AcceptConnectAndWorkAsync(socket, msg);
                        return;
                    case "ConnectorConnect":
                        var server = FindServerWorkerByPort(int.Parse(msg.Args[1]));
                        string failedmsg = null;
                        if (server == null)
                        {
                            failedmsg = "NoPort";
                        }
                        else if (server.Server.IsDisabled)
                        {
                            failedmsg = "IsDisabled";
                        }
                        else if (!server.Server.AllowConnector)
                        {
                            failedmsg = "NotAllow";
                        }
                        else if (server.Server.ConnectorLicense == null)
                        {
                            failedmsg = "NotLicense";
                        }
                        else if (server.Server.ConnectorLicense.Key != msg.Args[0])
                        {
                            failedmsg = "LicenseNotMatch";
                        }
                        if (failedmsg != null)
                        {
                            var resmsg = new CommandMessage("ConnectFailed", failedmsg);
                            await socket.SendAsync(resmsg.Pack(), SocketFlags.None);
                        }
                        else
                        {
                            TcpMapServerConnector connector = new TcpMapServerConnector(server);
                            await connector.AcceptConnectorAndWorkAsync(socket, msg);
                        }
                        break;
                    case ""://other direct request..
                    default:
                        throw new Exception("Invaild command " + msg.Name);
                }
            }
        }

        private static readonly List<TcpMapClientWorker> _clients = new List<TcpMapClientWorker>();// as client side
        private static readonly List<TcpMapServerWorker> _servers = new List<TcpMapServerWorker>();// as server side
        private static readonly List<TcpMapConnectorWorker> _connectors = new List<TcpMapConnectorWorker>();// as connector side

        public static TcpMapClientWorker[] GetClientWorkers () => _clients.LockToArray();
        public static TcpMapServerWorker[] GetServerWorkers () => _servers.LockToArray();
        public static TcpMapConnectorWorker[] GetConnectorWorkers () => _connectors.LockToArray();

        private static TcpMapClientWorker AddStartClient (TcpMapClient client)
        {
            var conn = new TcpMapClientWorker() { Client = client };
            lock (_clients)
            {
                _clients.Add(conn);
            }

            if (!client.IsDisabled)
            {
                conn.StartWork();
            }

            return conn;
        }

        private static TcpMapServerWorker AddStartServer (TcpMapServer server)
        {
            var conn = new TcpMapServerWorker() { Server = server };
            lock (_servers)
            {
                _servers.Add(conn);
            }
            conn.StartWork();
            return conn;
        }

        private static TcpMapConnectorWorker AddStartConnector (TcpMapConnector mapconnector)
        {
            var conn = new TcpMapConnectorWorker() { Connector = mapconnector };
            lock (_connectors)
            {
                _connectors.Add(conn);
            }
            conn.StartWork();
            return conn;
        }

        internal static TcpMapConnectorWorker FindConnectorWorkerByPort (int localport)
        {
            lock (_connectors)
            {
                return _connectors.FirstOrDefault(v => v.Connector.LocalPort == localport);
            }
        }
        internal static TcpMapServerWorker FindServerWorkerByPort (int serverport)
        {
            lock (_servers)
            {
                return _servers.FirstOrDefault(v => v.Server.ServerPort == serverport);
            }
        }

        internal static TcpMapServerWorker FindServerWorkerByKey (string licenseKey, int serverport)
        {
            lock (_servers)
            {
                return _servers.FirstOrDefault(v => v.Server.ServerPort == serverport && v.Server.License.Key == licenseKey);
            }
        }

        public static TcpMapClientWorker CreateClientWorker (TcpMapLicense lic, int serverPort)
        {
            TcpMapClient client = new TcpMapClient() { License = lic };
            client.Id = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            client.IsDisabled = true;
            client.ServerHost = "servername";
            client.ServerPort = serverPort;
            client.ClientHost = "localhost";
            client.ClientPort = 80;
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapClient_" + client.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(client));
            return AddStartClient(client);
        }

        public static void ReAddClient (TcpMapClient client)
        {
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapClient_" + client.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(client));
            TcpMapClientWorker clientWorker = null;
            lock (_clients)
            {
                clientWorker = _clients.Where(v => v.Client.Id == client.Id).FirstOrDefault();
                if (clientWorker != null)
                {
                    _clients.Remove(clientWorker);
                }
            }
            if (clientWorker != null)
            {
                clientWorker.Stop();
            }
            AddStartClient(client);
        }

        private static void CheckPortAvailable (int port)
        {
            {
                var existWorker = FindServerWorkerByPort(port);
                if (existWorker != null)
                {
                    throw new Exception("port is being used by server worker " + existWorker.Server.Id);
                }
            }
            {
                var existWorker = FindConnectorWorkerByPort(port);
                if (existWorker != null)
                {
                    throw new Exception("port is being used by connector worker " + existWorker.Connector.Id);
                }
            }
        }

        public static TcpMapServerWorker CreateServerWorker (TcpMapLicense lic, int port)
        {
            CheckPortAvailable(port);

            TcpMapServer server = new TcpMapServer
            {
                Id = DateTime.Now.ToString("yyyyMMddHHmmssfff"),
                License = lic,
                IsDisabled = false,
                IsValidated = true,
                ServerPort = port
            };
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapServer_" + server.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(server));
            return AddStartServer(server);
        }

        public static void ReAddServer (TcpMapServer server)
        {
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapServer_" + server.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(server));
            TcpMapServerWorker serverWorker = null;
            lock (_clients)
            {
                serverWorker = _servers.Where(v => v.Server.Id == server.Id).FirstOrDefault();
                if (serverWorker != null)
                {
                    _servers.Remove(serverWorker);
                }
            }
            if (serverWorker != null)
            {
                serverWorker.Stop();
            }
            AddStartServer(server);
        }

        public static TcpMapConnectorWorker CreateConnectorWorker (int port)
        {
            CheckPortAvailable(port);

            TcpMapConnector conn = new TcpMapConnector
            {
                Id = DateTime.Now.ToString("yyyyMMddHHmmssfff"),
                LocalPort = port,
                ServerHost = "servername",
                ServerPort = port,
                IsDisabled = true
            };
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapConnector_" + conn.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(conn));
            return AddStartConnector(conn);
        }

        public static void ReAddConnector (TcpMapConnector connector)
        {
            string jsonfilepath = Path.Combine(DataFolder, "TcpMapConnector_" + connector.Id + ".json");
            File.WriteAllText(jsonfilepath, JsonSerializer.Serialize(connector));
            TcpMapConnectorWorker connectorWorker = null;
            lock (_clients)
            {
                connectorWorker = _connectors.Where(v => v.Connector.Id == connector.Id).FirstOrDefault();
                if (connectorWorker != null)
                {
                    _connectors.Remove(connectorWorker);
                }
            }
            if (connectorWorker != null)
            {
                connectorWorker.Stop();
            }
            AddStartConnector(connector);
        }
    }
}