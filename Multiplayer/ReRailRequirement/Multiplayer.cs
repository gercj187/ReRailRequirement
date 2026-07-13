#nullable enable
using System;
using MPAPI;
using MPAPI.Interfaces;
using MPAPI.Interfaces.Packets;
using UnityEngine;

namespace ReRailRequirement
{
    // Host -> Client: complete gameplay-relevant settings snapshot.
    public sealed class ClientBoundRerailSettingsPacket : IPacket
    {
        public float MaxDistanceMeters { get; set; }
        public float DistanceToDM1U { get; set; }
        public float RerailRange { get; set; }
        public float MaxWeightTons { get; set; }
        public bool AllowDM1URerailCrane { get; set; }
        public float BasePriceMultiplier { get; set; }
        public float PricePerMeterMultiplier { get; set; }
    }

    // Client -> Host: sent after the client world has finished loading.
    public sealed class ServerBoundRerailSettingsRequestPacket : IPacket
    {
        public bool Ready { get; set; }
    }

    internal static class RRR_Multiplayer
    {
        private static GameObject? _runtimeObject;

        public static bool IsHost
        {
            get
            {
                try
                {
                    return MultiplayerAPI.Instance != null &&
                           MultiplayerAPI.Server != null &&
                           MultiplayerAPI.Instance.IsHost;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool IsClient
        {
            get
            {
                try
                {
                    return MultiplayerAPI.Instance != null &&
                           MultiplayerAPI.Client != null &&
                           !MultiplayerAPI.Instance.IsHost;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static void Initialize()
        {
            if (_runtimeObject != null)
                return;

            _runtimeObject = new GameObject("ReRailRequirement_Multiplayer");
            UnityEngine.Object.DontDestroyOnLoad(_runtimeObject);
            _runtimeObject.AddComponent<RRR_MultiplayerClient>();
            _runtimeObject.AddComponent<RRR_MultiplayerServer>();

            Debug.Log("[ReRailRequirement][MP] Multiplayer runtime created.");
        }

        public static void BroadcastHostSettings()
        {
            if (!IsHost)
                return;

            RRR_MultiplayerServer.Instance?.SendSettingsToAll();
        }
    }

    internal sealed class RRR_MultiplayerClient : MonoBehaviour
    {
        public static RRR_MultiplayerClient? Instance { get; private set; }

        private IClient? _client;
        private bool _registered;
        private bool _settingsReceived;
        private float _nextRequestTime;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }
        }

        private void Update()
        {
            RefreshClientReference();

            if (!_registered)
                TryRegister();

            TryRequestSettings();
        }

        private void RefreshClientReference()
        {
            IClient? current = MultiplayerAPI.Client;
            if (ReferenceEquals(_client, current))
                return;

            _client = current;
            _registered = false;
            _settingsReceived = false;
            _nextRequestTime = 0f;

            Debug.Log("[ReRailRequirement][MP] Client connection changed; settings synchronization reset.");
        }

        private void TryRegister()
        {
            if (_client == null)
                return;

            _client.RegisterPacket<ClientBoundRerailSettingsPacket>(OnSettingsReceived);
            _registered = true;

            Debug.Log("[ReRailRequirement][MP] Client packet handler registered.");
        }

        private void TryRequestSettings()
        {
            if (!RRR_Multiplayer.IsClient || !_registered || _client == null || _settingsReceived)
                return;

            if (PlayerManager.PlayerTransform == null || !AStartGameData.carsAndJobsLoadingFinished)
                return;

            if (Time.unscaledTime < _nextRequestTime)
                return;

            _client.SendPacketToServer(
                new ServerBoundRerailSettingsRequestPacket { Ready = true },
                reliable: true);

            _nextRequestTime = Time.unscaledTime + 2f;
            Debug.Log("[ReRailRequirement][MP] Host settings requested.");
        }

        private void OnSettingsReceived(ClientBoundRerailSettingsPacket packet)
        {
            if (packet == null || Main.settings == null)
                return;

            // Host values are authoritative. Clamp again to prevent malformed packets.
            Main.settings.maxDistanceMeters = Mathf.Clamp(Mathf.Round(packet.MaxDistanceMeters), 5f, 50f);
            Main.settings.bc_distanceToDM1U_m = Mathf.Clamp(Mathf.Round(packet.DistanceToDM1U), 5f, 25f);
            Main.settings.bc_rerailRange_m = Mathf.Clamp(Mathf.Round(packet.RerailRange), 10f, 50f);
            Main.settings.bc_maxWeight_t = Mathf.Clamp(Mathf.Round(packet.MaxWeightTons), 10f, 50f);
            Main.settings.bc_allowDM1U_rerailCrane = packet.AllowDM1URerailCrane;
            Main.settings.bcx_basePriceMul = Mathf.Clamp(packet.BasePriceMultiplier, 1f, 2.5f);
            Main.settings.bcx_pricePerMeterMul = Mathf.Clamp(packet.PricePerMeterMultiplier, 1f, 2.5f);

            _settingsReceived = true;
            Debug.Log("[ReRailRequirement][MP] Host settings applied.");
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            _client = null;
            _registered = false;
            _settingsReceived = false;
        }
    }

    internal sealed class RRR_MultiplayerServer : MonoBehaviour
    {
        public static RRR_MultiplayerServer? Instance { get; private set; }

        private IServer? _server;
        private bool _registered;

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else if (Instance != this)
            {
                Destroy(this);
                return;
            }
        }

        private void Update()
        {
            RefreshServerReference();

            if (!_registered)
                TryRegister();
        }

        private void RefreshServerReference()
        {
            IServer? current = MultiplayerAPI.Server;
            if (ReferenceEquals(_server, current))
                return;

            _server = current;
            _registered = false;
            Debug.Log("[ReRailRequirement][MP] Server connection changed; packet registration reset.");
        }

        private void TryRegister()
        {
            if (_server == null)
                return;

            _server.RegisterPacket<ServerBoundRerailSettingsRequestPacket>(OnSettingsRequested);
            _registered = true;

            Debug.Log("[ReRailRequirement][MP] Server packet handler registered.");
        }

        private void OnSettingsRequested(ServerBoundRerailSettingsRequestPacket packet, IPlayer sender)
        {
            if (packet == null || sender == null || !packet.Ready)
                return;

            SendSettingsToPlayer(sender);
            Debug.Log("[ReRailRequirement][MP] Host settings sent to ready client.");
        }

        public void SendSettingsToAll()
        {
            if (!_registered || _server == null || Main.settings == null)
                return;

            _server.SendPacketToAll(CreateSettingsPacket(), reliable: true, excludeSelf: true);
            Debug.Log("[ReRailRequirement][MP] Updated host settings broadcast.");
        }

        private void SendSettingsToPlayer(IPlayer player)
        {
            if (_server == null || player == null || Main.settings == null)
                return;

            _server.SendPacketToPlayer(CreateSettingsPacket(), player, reliable: true);
        }

        private static ClientBoundRerailSettingsPacket CreateSettingsPacket()
        {
            Settings s = Main.settings;

            return new ClientBoundRerailSettingsPacket
            {
                MaxDistanceMeters = s.maxDistanceMeters,
                DistanceToDM1U = s.bc_distanceToDM1U_m,
                RerailRange = s.bc_rerailRange_m,
                MaxWeightTons = s.bc_maxWeight_t,
                AllowDM1URerailCrane = s.bc_allowDM1U_rerailCrane,
                BasePriceMultiplier = s.bcx_basePriceMul,
                PricePerMeterMultiplier = s.bcx_pricePerMeterMul
            };
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            _server = null;
            _registered = false;
        }
    }
}