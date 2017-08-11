﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace L_SocketCore
{
    //TODO disconnect when heartbeat timeout
    public class SocketManager
    {
        public delegate void ReceiveData(SocketClient client, MSG_TYPE type, byte[] bytes);
        /// <summary>
        /// Event on receive data
        /// </summary>
        public event ReceiveData OnReceiveData;

        public delegate void AcceptClientAdd(Guid id);

        public event AcceptClientAdd OnAcceptClientAdd;

        public delegate void AcceptClientRemove(Guid id);
        public event AcceptClientRemove OnAcceptClientRemove;

        public delegate void ConnectClientAdd(Guid id);
        public event ConnectClientAdd OnConnectClientAdd;

        public delegate void ConnectClientRemove(Guid id);
        public event ConnectClientRemove OnConnectClientRemove;

        public delegate void ClientStateChange(Guid id, SocketClient.CLIENT_STATE state);
        public event ClientStateChange OnClientStateChange;

        public delegate void HeartbeatStateChange(Guid id, SocketClient.HEARTBEAT_STATE state);
        public event HeartbeatStateChange OnHeartbeatStateChange;

        private Dictionary<Guid, SocketClient> _acceptedClients = new Dictionary<Guid, SocketClient>();
        /// <summary>
        /// Clients connect to me
        /// </summary>
        public Dictionary<Guid, SocketClient> AcceptedClients
        {
            get
            {
                return _acceptedClients;
            }
        }

        private Dictionary<Guid, SocketClient> _connectedClients = new Dictionary<Guid, SocketClient>();
        /// <summary>
        /// Clients I connect to
        /// </summary>
        public Dictionary<Guid, SocketClient> ConnectedClients
        {
            get
            {
                return _connectedClients;
            }
        }

        private const int INT_SIZE = 4;

        public enum MSG_TYPE
        {
            PING,
            PONG,
            TEXT,
            BIN,
            CMD
        }

        public SocketManager()
        {
            heartbeat();
            heartbeatDetect();
        }

        public SocketClient Connect(string hostName, int port)
        {
            TcpClient tcpClient = new TcpClient();
            try
            {
                tcpClient.Connect(hostName, port);
                SocketClient socketClient = new SocketClient(tcpClient);
                ConnectedClients.Add(socketClient.ID, socketClient);
                OnConnectClientAdd?.Invoke(socketClient.ID);
                socketClient.OnStateChange += SocketClient_OnStateChange;
                socketClient.OnHeartbeatStateChange += SocketClient_OnHeartbeatStateChange;
                socketClient.HeartbeatState = SocketClient.HEARTBEAT_STATE.INIT;
                Thread thread = new Thread(() =>
                {
                    receiveData(socketClient);
                });
                thread.Start();
                return socketClient;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Util.WriteLog(ex.Message, "error.txt");
                tcpClient.Close();
                throw;
            }
        }

        private void SocketClient_OnStateChange(Guid id, SocketClient.CLIENT_STATE state)
        {
            OnClientStateChange?.Invoke(id, state);
        }

        public void Send(SocketClient socketToSend, MSG_TYPE type, byte[] data)
        {
            if (socketToSend.State == SocketClient.CLIENT_STATE.DISCONNECTED)
            {
                return;
            }
            Thread thread = new Thread(() => 
            {
                NetworkStream stream = socketToSend.RemoteClient.GetStream();
                //write type
                byte[] buffer = Util.ConvertInt32ToBytes((Int32)type);
                stream.Write(buffer, 0, INT_SIZE);
                //write length
                Int32 dataSize = 0;
                if (data != null)
                {
                    dataSize = (Int32)data.Length;
                }
                buffer = Util.ConvertInt32ToBytes(dataSize);
                stream.Write(buffer, 0, INT_SIZE);
                //write data
                if (data != null)
                {
                    buffer = data;
                    stream.Write(buffer, 0, dataSize);
                }
                stream.Flush();
            });
            thread.Start();
        }

        public void SendText(SocketClient socketToSend, string msg)
        {
            if (msg == null || msg == "")
            {
                return;
            }
            Send(socketToSend, MSG_TYPE.TEXT, Encoding.UTF8.GetBytes(msg));
        }

        public void Listen(int port, string localAddr = "0.0.0.0")
        {
            TcpListener listener = new TcpListener(IPAddress.Parse(localAddr), port);
            try
            {
                listener.Start();
                while (true)
                {
                    //blocks here
                    TcpClient client = listener.AcceptTcpClient();
                    Thread thread = new Thread(() =>
                    {
                        SocketClient socketClient = new SocketClient(client);
                        AcceptedClients.Add(socketClient.ID, socketClient);
                        OnAcceptClientAdd?.Invoke(socketClient.ID);
                        socketClient.OnStateChange += SocketClient_OnStateChange;
                        socketClient.OnHeartbeatStateChange += SocketClient_OnHeartbeatStateChange;
                        socketClient.HeartbeatState = SocketClient.HEARTBEAT_STATE.INIT;
                        Debug.WriteLine(socketClient.ToString() + ' ' + socketClient.State);
                        receiveData(socketClient);

                    });
                    thread.Start();
                }
            }
            catch (Exception ex)
            {

                Debug.WriteLine(ex.Message);
                Util.WriteLog(ex.Message, "error.txt");
                throw;
            }
            finally
            {
                listener.Stop();
            }

        }

        private void SocketClient_OnHeartbeatStateChange(Guid id, SocketClient.HEARTBEAT_STATE state)
        {
            OnHeartbeatStateChange?.Invoke(id, state);
        }

        public void Disconnect(SocketClient socketToDisconnect)
        {
            //TODO Disconnect
        }

        private void receiveData(SocketClient socketClient)
        {
            NetworkStream stream = socketClient.RemoteClient.GetStream();
            while (true)
            {
                try
                {
                    byte[] buffer = new byte[INT_SIZE];
                    //read type
                    stream.Read(buffer, 0, INT_SIZE);
                    //socketClient.State = SocketClient.ClientState.DATA_SEND_BEGIN;
                    Int32 typeInt = BitConverter.ToInt32(buffer, 0);
                    MSG_TYPE type = (MSG_TYPE)IPAddress.NetworkToHostOrder(typeInt);
                    //read length
                    stream.Read(buffer, 0, INT_SIZE);
                    Int32 dataSize = BitConverter.ToInt32(buffer, 0);
                    dataSize = IPAddress.NetworkToHostOrder(dataSize);
                    //read data
                    if (dataSize > 0)
                    {
                        buffer = new byte[dataSize];
                        stream.Read(buffer, 0, dataSize);
                    }
                    else
                    {
                        buffer = null;
                    }
                    dataHandler(socketClient, type, buffer);
                    //socketClient.State = SocketClient.ClientState.DATA_SEND_END;
                }
                catch (Exception ex)
                {
                    if (socketClient.State != SocketClient.CLIENT_STATE.DISCONNECTED)
                    {
                        socketClient.State = SocketClient.CLIENT_STATE.DISCONNECTED;
                        socketClient.RemoteClient.Close();
                        if (AcceptedClients.ContainsKey(socketClient.ID))
                        {
                            AcceptedClients.Remove(socketClient.ID);
                            OnAcceptClientRemove?.Invoke(socketClient.ID);
                        }
                        if (ConnectedClients.ContainsKey(socketClient.ID))
                        {
                            ConnectedClients.Remove(socketClient.ID);
                            OnConnectClientRemove?.Invoke(socketClient.ID);
                        }
                    }
                    break;
                }
            }
        }

        private void dataHandler(SocketClient socketClient, MSG_TYPE type, byte[] buffer)
        {
            switch (type)
            {
                case MSG_TYPE.PING:
                    socketClient.HeartbeatState = SocketClient.HEARTBEAT_STATE.PING_RECEIVE;
                    Send(socketClient, MSG_TYPE.PONG, null);
                    socketClient.HeartbeatState = SocketClient.HEARTBEAT_STATE.PONG_SEND;
                    break;
                case MSG_TYPE.PONG:
                    socketClient.HeartbeatState = SocketClient.HEARTBEAT_STATE.PONG_RECEIVE;
                    break;
                case MSG_TYPE.TEXT:
                    OnReceiveData?.Invoke(socketClient, type, buffer);
                    break;
                case MSG_TYPE.BIN:
                    break;
                case MSG_TYPE.CMD:
                    break;
                default:
                    OnReceiveData?.Invoke(socketClient, type, buffer);
                    break;
            }
        }

        private void heartbeat()
        {
            //heartbeat ConnectedClients
            int interval = 30 * 1000;
            Thread thread = new Thread(() =>
            {
                while (true)
                {
                    foreach (var item in ConnectedClients)
                    {
                        try
                        {
                            var client = item.Value;
                            Send(client, MSG_TYPE.PING, null);
                            client.HeartbeatState = SocketClient.HEARTBEAT_STATE.PING_SEND;
                        }
                        catch (Exception ex)
                        {
                            Debug.Write(ex.Message);
                            Util.WriteLog(ex.Message, "error.txt");
                        }

                    }
                    Thread.Sleep(interval);
                }
            });
            thread.Start();
        }

        private void heartbeatDetect()
        {
            int networkInterval = 5 * 1000;
            Thread thread1 = new Thread(() =>
            {
                while (true)
                {
                    foreach (var item in ConnectedClients.Values)
                    {
                        if (item.HeartbeatState == SocketClient.HEARTBEAT_STATE.PENDING)
                        {
                            item.State = SocketClient.CLIENT_STATE.DISCONNECTED;
                        }
                        if (item.HeartbeatState == SocketClient.HEARTBEAT_STATE.PING_SEND)
                        {
                            item.HeartbeatState = SocketClient.HEARTBEAT_STATE.PENDING;
                        }
                    }
                    Thread.Sleep(networkInterval);
                }
            });
            thread1.Start();
            int detectInterval = 30 * 1000;
            Thread thread2 = new Thread(() =>
            {
                while (true)
                {
                    foreach (var item in AcceptedClients.Values)
                    {
                        if (item.HeartbeatState == SocketClient.HEARTBEAT_STATE.PENDING)
                        {
                            item.State = SocketClient.CLIENT_STATE.DISCONNECTED;
                        }
                        if (item.HeartbeatState == SocketClient.HEARTBEAT_STATE.PING_RECEIVE ||
                            item.HeartbeatState == SocketClient.HEARTBEAT_STATE.PONG_SEND ||
                            item.HeartbeatState == SocketClient.HEARTBEAT_STATE.INIT)
                        {
                            item.HeartbeatState = SocketClient.HEARTBEAT_STATE.PENDING;
                        }
                    }
                    Thread.Sleep(detectInterval);
                }
            });
            thread2.Start();
        }
    }
}
