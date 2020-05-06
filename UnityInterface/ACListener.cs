using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace AirDriVR
{
    public class ACListener : GameListener
    {
        public string hostIP = "127.0.0.1";
        public int hostPort = 9996;

        public Text infoText;

        [FormerlySerializedAs("airDriVrController")] public AirDriVRTestController airDriVrTestController;

        private IPEndPoint acEndPoint;
        private UdpClient client;

        private ACCarInfo info;
        private Vector2 forceVector;
        private float forceMag;
    
        private readonly object infoLock = new object();

        [ReadOnly]
        public bool hasConnected = false;

        public bool logGForceToFile = false;
        public string logFolder;

        private StreamWriter logStreamWriter;

        private bool isConnecting = false;
        
        public void Connect()
        {
        
            if (hasConnected)
            {
                Debug.LogError("Already connected. Now attempt to disconnect and reconnect.");
                OnDisable();
            }
        
            if (isConnecting)
            {
                Debug.LogError("Can't start handshake; an attempt to handshake is undergoing.");
                return;
            }
        
            acEndPoint = new IPEndPoint(IPAddress.Parse(hostIP), hostPort);
            client = new UdpClient();

            StartCoroutine(HandshakeStart());
        }

        IEnumerator HandshakeStart()
        {
            Debug.Log("Initiating handshake");
            isConnecting = true;
            var hs = new ACHandshaker(0, 1, 0);
            var hsPacketSize = Marshal.SizeOf<ACHandshaker>();
            var packet = new byte[hsPacketSize];

            var ptr = Marshal.AllocHGlobal(hsPacketSize);
            Marshal.StructureToPtr(hs, ptr, true);
            Marshal.Copy(ptr, packet, 0, hsPacketSize);

            try
            {
                client.Send(packet, packet.Length, acEndPoint);
            }
            catch (Exception e)
            {
                Debug.Log(e);
                isConnecting = false;
                yield break;
            }

            var listeningTask = client.ReceiveAsync();

            while (!listeningTask.IsCompleted)
            {
                yield return null;
            }

            var hsResponse = FromBytes<ACHandshackerResponse>(listeningTask.Result.Buffer);
            Debug.Log(hsResponse.ToString());

            // Subscribe
            hs.operationId = 1;
            Marshal.StructureToPtr(hs, ptr, true);
            Marshal.Copy(ptr, packet, 0, hsPacketSize);

            client.Send(packet, packet.Length, acEndPoint);
        
            Marshal.FreeHGlobal(ptr);

            isConnecting = false;
            hasConnected = true;

            if (logGForceToFile)
            {
                var logFilePath = Path.Combine(logFolder, $"{DateTime.Now:yyyyMMdd-hh-mm-ss-ff}.csv");
                logStreamWriter = File.AppendText(logFilePath);
                logStreamWriter.WriteLine("lap_time,horizontal,longitudinal");
            }

            Task.Run(ListenToUpdate);
            while (hasConnected)
            {
                lock (infoLock)
                {
                    airDriVrTestController.SetGForce(
                        info.accG_horizontal * horizontalMultipler, 
                        -info.accG_frontal * longitudinalMultiplier);
                    infoText.text = info.ToString();
                }
                yield return null;
            }
        }

        private void ListenToUpdate()
        {
            while (hasConnected)
            {
                var tmp = FromBytes<ACCarInfo>(client.Receive(ref acEndPoint));
                lock (infoLock)
                {
                    info = tmp;
                }

                logStreamWriter?.WriteLine($"{tmp.lapTime},{tmp.accG_horizontal},{-tmp.accG_frontal}");
            }
        }

        private void OnDisable()
        {
            if (hasConnected)
            {
                hasConnected = false;
                isConnecting = false;
                // 3: Dismiss connection
                var hs = new ACHandshaker(0, 1, 3);
                var hsPacketSize = Marshal.SizeOf<ACHandshaker>();
                var packet = new byte[hsPacketSize];

                var ptr = Marshal.AllocHGlobal(hsPacketSize);
                Marshal.StructureToPtr(hs, ptr, true);
                Marshal.Copy(ptr, packet, 0, hsPacketSize);

                client.Send(packet, packet.Length, acEndPoint);
        
                Marshal.FreeHGlobal(ptr);
        
                Debug.Log("Successfully unsubscribed");
                
                logStreamWriter?.Dispose();
            }
        }
    }
}
