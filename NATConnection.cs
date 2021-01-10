using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Mirror;
using UnityEngine;

namespace Network.Utility
{
    public class NATConnection
    {
        private enum ConnectionState
        {
            Probing = 0,
            Acknowledging,
            Established
        }

        public static Task EstablishConnection(string localIp, string publicIp, int port, Action<string, int, bool> callback)
        {
            return Task.Factory.StartNew(() =>
            {
                EstablishConnectionInternal(localIp, publicIp, port, callback);
            });
        }

        private class NATConnectionState
        {
            public UdpClient udpClient;
            public IPEndPoint endpoint;
            public ConnectionState LocalState = ConnectionState.Probing;
            public ConnectionState RemoteState = ConnectionState.Probing;
            
            public NATConnectionState(UdpClient client, IPEndPoint endpoint)
            {
                this.udpClient = client;
                this.endpoint = endpoint;
            }

            public bool isConnected()
            {
                return LocalState == ConnectionState.Established
                    && RemoteState == ConnectionState.Established;
            }

            public void Pulse()
            {
                SendState(LocalState);
            }

            public void SendState(ConnectionState state)
            {
                udpClient.Send(new [] {(byte)state}, 1, endpoint);
                Debug.Log($"Sending {state} to {endpoint}");
            }

            public bool Matches(IPEndPoint dest)
            {
                if (endpoint == dest)
                {
                    return true;
                }

                if (endpoint.Address.ToString() == dest.Address.ToString())
                {
                    return true;
                }

                return false;
            }

            public void HandleMessage(byte[] data)
            {
                if (data.Length != 1)
                {
                    Debug.LogError("Received a different sized response than anticipated");
                    return;
                }
                
                var responseVal = (ConnectionState) data[0];
                Debug.Log($"Received message {responseVal} from {endpoint} while we are {LocalState}");

                switch (responseVal)
                {
                    case ConnectionState.Probing:
                        SendState(ConnectionState.Acknowledging);
                        break;
                    case ConnectionState.Acknowledging:
                        SendState(ConnectionState.Established);
                        break;
                    case ConnectionState.Established:
                        SendState(ConnectionState.Established);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                
                RemoteState = responseVal;

                if (LocalState == ConnectionState.Probing)
                {
                    SetConnectionState(ConnectionState.Acknowledging);
                }

                if (RemoteState == ConnectionState.Acknowledging
                    || RemoteState == ConnectionState.Established)
                {
                    SetConnectionState(ConnectionState.Established);
                }
            }

            private void SetConnectionState(ConnectionState newState)
            {
                if (newState == LocalState)
                    return;
                
                Debug.Log($"Setting connection state from {LocalState} => {newState}");
                LocalState = newState;
            }
        }

        private static void EstablishConnectionInternal(string localIp, string publicIp, int port, Action<string, int, bool> callback)
        {
            // When we requested access, the server should have attempted to reach out to us.
            // This initial connection should have told the NAT that we're expected.
            var startTime = DateTime.Now;

            var localEndpoint = new IPEndPoint(IPAddress.Parse(localIp), port);
            var publicEndpoint = new IPEndPoint(IPAddress.Parse(publicIp), port);
            IPEndPoint finalDestination = null;
            
            UdpClient udpClient = new UdpClient(port);

            var localState = new NATConnectionState(udpClient, localEndpoint);
            var publicState = new NATConnectionState(udpClient, publicEndpoint);

            bool ConnectionEstabled() => (localState.isConnected() 
                                          || publicState.isConnected());
            
            // Send a bunch of messages over 10 seconds to establish connection
            while ((DateTime.Now - startTime).TotalSeconds < 10f && !ConnectionEstabled())
            {
                localState.Pulse();
                publicState.Pulse();
                var localResponse = udpClient.ReceiveAsync();
                localResponse.ContinueWith((antecedent) =>
                {
                    var response = antecedent.Result;
                    if (localState.Matches(response.RemoteEndPoint))
                    {
                        localState.HandleMessage(response.Buffer);
                    }
                    else if (publicState.Matches(response.RemoteEndPoint))
                    {
                        publicState.HandleMessage(response.Buffer);
                    }
                    else
                    {
                        Debug.Log($"Endpoint doesn't match correctly: {response.RemoteEndPoint} local({localEndpoint}) remote ({publicEndpoint})");
                    }
                });

                Thread.Sleep(500);
            }
            
            udpClient.Close();

            if (localState.isConnected())
            {
                callback.Invoke(localIp, port, true);
            }
            else if (publicState.isConnected())
            {
                callback.Invoke(publicIp, port, true);
            }
            else
            {
                Debug.LogError("Failed to connect to server");
                callback.Invoke("", 0, false);
            }
        }

    }
}