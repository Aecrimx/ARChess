using System;
using System.Net;
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatTimer(float seconds)
    {
        if (seconds >= float.MaxValue) return "Unlimited";
        int mins = Mathf.RoundToInt(seconds / 60f);
        return $"{mins} min";
    }
}
