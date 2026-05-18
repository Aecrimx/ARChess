using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  LanDiscovery
//
//  RESPONSIBILITY: UDP LAN broadcast so hosts can be found on the same network.
//
//  SCENE SETUP:
//  Attach to the same GameObject as LanNetworkManager in the MainMenu scene.
//
//  USAGE:
//  Host:   Call AdvertiseServer() after StartHost().
//  Client: Call StartDiscovery(); subscribe to OnServerDiscovered to populate list.
//          Call StopDiscovery() when leaving the join panel.
// ─────────────────────────────────────────────────────────────────────────────

// ── Message types ─────────────────────────────────────────────────────────────
public struct DiscoveryRequest : NetworkMessage { }

public struct DiscoveryResponse : NetworkMessage
{
    /// <summary>URI Mirror uses to connect (set by base class infrastructure).</summary>
    public Uri uri;

    /// <summary>Display name of the hosting player.</summary>
    public string HostName;

    /// <summary>Human-readable timer label, e.g. "5 min" or "Unlimited".</summary>
    public string TimerLabel;
}

// ── Discovery component ───────────────────────────────────────────────────────
public class LanDiscovery : NetworkDiscoveryBase<DiscoveryRequest, DiscoveryResponse>
{
    /// <summary>
    /// Raised on clients whenever a host is found or re-advertised.
    /// Use this instead of the Unity Event inherited from the base class.
    /// </summary>
    public static event Action<DiscoveryResponse> OnServerDiscovered;

    // ── Server side ───────────────────────────────────────────────────────────

    protected override DiscoveryResponse ProcessRequest(DiscoveryRequest request,
                                                        IPEndPoint endpoint)
    {
        var mgr = LanNetworkManager.Instance;
        return new DiscoveryResponse
        {
            // Critical: clients use this URI to call StartClient(uri).
            // Without it the joining device has no address to connect to.
            uri        = Transport.active.ServerUri(),
            HostName   = mgr != null ? mgr.HostPlayerName : "Host",
            TimerLabel = mgr != null ? FormatTimer(mgr.TimerSeconds) : "?",
        };
    }

    // ── Client side ───────────────────────────────────────────────────────────

    protected override void ProcessResponse(DiscoveryResponse response,
                                            IPEndPoint endpoint)
    {
        // Transport.active.ServerUri() returns "tcp4://localhost:7777".
        // Replace 'localhost' with the actual LAN IP of the host device
        // (the source address of the UDP discovery packet).
        response.uri = new UriBuilder(response.uri)
        {
            Host = endpoint.Address.ToString()
        }.Uri;

        OnServerDiscovered?.Invoke(response);
    }

    public void SendDiscoveryRequestTo(IPAddress address)
    {
        if (clientUdpClient == null || address == null)
            return;

        using (NetworkWriterPooled writer = NetworkWriterPool.Get())
        {
            writer.WriteLong(secretHandshake);
            writer.Write(GetRequest());

            ArraySegment<byte> data = writer.ToArraySegment();
            clientUdpClient.SendAsync(data.Array, data.Count, new IPEndPoint(address, serverBroadcastListenPort));
        }
    }

    public IEnumerable<IPAddress> GetLikelyLanAddresses()
    {
        var yielded = new HashSet<string>();

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            IPInterfaceProperties properties;
            try
            {
                properties = networkInterface.GetIPProperties();
            }
            catch
            {
                continue;
            }

            foreach (UnicastIPAddressInformation unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily != AddressFamily.InterNetwork)
                    continue;

                if (IPAddress.IsLoopback(unicast.Address))
                    continue;

                if (!IsPrivateLanAddress(unicast.Address))
                    continue;

                byte[] localBytes = unicast.Address.GetAddressBytes();
                for (int lastOctet = 1; lastOctet <= 254; lastOctet++)
                {
                    if (lastOctet == localBytes[3]) continue;

                    var address = new IPAddress(new byte[]
                    {
                        localBytes[0],
                        localBytes[1],
                        localBytes[2],
                        (byte)lastOctet
                    });

                    if (yielded.Add(address.ToString()))
                        yield return address;
                }
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatTimer(float seconds)
    {
        if (seconds >= float.MaxValue) return "Unlimited";
        int mins = Mathf.RoundToInt(seconds / 60f);
        return $"{mins} min";
    }

    private static bool IsPrivateLanAddress(IPAddress address)
    {
        byte[] bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        if (bytes[0] == 10)
            return true;

        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;

        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        return false;
    }
}
