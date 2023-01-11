﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using MultiplayerMod.multiplayer;
using Steamworks;
using UnityEngine;

namespace MultiplayerMod.steam
{
    public class Server : MonoBehaviour
    {
        private HSteamNetPollGroup _hNetPollGroup;
        public event System.Action ServerCreated;
        public event Action<CSteamID> ClientJoined;

        private readonly Dictionary<CSteamID, HSteamNetConnection> _clients =
            new Dictionary<CSteamID, HSteamNetConnection>();

        private static bool _hostServerAfterLoad;
        private bool _isServerStarted;
        public CSteamID SteamId => SteamGameServer.GetSteamID();

        void OnEnable()
        {
            Debug.Log("Multiplayer.RestartAppIfNecessary");

            SteamAPI.RestartAppIfNecessary(new AppId_t(457140));
            Debug.Log("Multiplayer.Server.Init");
            SteamAPI.Init();
            Debug.Log("Multiplayer.Server.Done");

            SteamNetworkingUtils.InitRelayNetworkAccess();

            if (!SteamManager.Initialized)
            {
                throw new Exception("Steam manager is not initialized");
            }

            SteamClient.SetWarningMessageHook(delegate(int severity, StringBuilder text)
            {
                Debug.Log($"Steam warning. {severity} {text}.");
            });
            Callback<SteamServersConnected_t>.CreateGameServer(delegate
            {
                Debug.Log("Game server created");
                SteamGameServer.SetMaxPlayerCount(4);
                SteamGameServer.SetPasswordProtected(false);
                SteamGameServer.SetServerName($"{SteamFriends.GetPersonaName()}'s game");
                SteamGameServer.SetBotPlayerCount(0); // optional, defaults to zero
                SteamGameServer.SetMapName("MilkyWay");
                ServerCreated?.Invoke();
            });
            Callback<SteamServerConnectFailure_t>.CreateGameServer(delegate(SteamServerConnectFailure_t t)
            {
                Debug.Log("SteamServerConnectFailure_t");
                Debug.Log(t);
            });
            Callback<SteamServersDisconnected_t>.CreateGameServer(delegate(SteamServersDisconnected_t t)
            {
                Debug.Log("SteamServersDisconnected_t");
                Debug.Log(t);
            });
            Callback<GSPolicyResponse_t>.CreateGameServer(delegate
            {
                Debug.Log(SteamGameServer.BSecure()
                    ? "ONI Multiplayer is VAC Secure!"
                    : "ONI Multiplayer is not VAC Secure!");
            });
            Callback<ValidateAuthTicketResponse_t>.CreateGameServer(delegate(ValidateAuthTicketResponse_t t)
            {
                Debug.Log("ValidateAuthTicketResponse_t");
                Debug.Log(t);
            });
            Callback<SteamNetConnectionStatusChangedCallback_t>.CreateGameServer(Steam_HandleIncomingConnection);
        }

        public static void HostServerAfterInit()
        {
            Debug.Log("Will host server after world is ready.");
            _hostServerAfterLoad = true;
        }

        public void HostServerIfNeeded()
        {
            if (!_hostServerAfterLoad) return;
            // Avoid multiplayer in follow up game loads.
            _hostServerAfterLoad = false;
            StartServer();
        }

        public void BroadcastCommand(Command command, object payload = null)
        {
            BroadcastCommand(null, command, payload);
        }

        public void BroadcastCommand(CSteamID? initiatorId, Command command, object payload = null)
        {
            foreach (var (cSteamID, hSteamNetConnection) in _clients)
            {
                if (cSteamID == initiatorId) continue;

                using var message = new ServerToClientEnvelope(new ServerToClientEnvelope.ServerToClientMessage(command, payload));
                SteamGameServerNetworkingSockets.SendMessageToConnection(hSteamNetConnection,
                    message.IntPtr, message.Size,
                    Steamworks.Constants.k_nSteamNetworkingSend_Reliable, out var messageOut);
                if (messageOut == 0)
                {
                    Debug.Log($"Failed to send message {initiatorId}. Message out is {messageOut}.");
                }
            }
        }

        private void StartServer()
        {
            Debug.Log("Multiplayer server startup");
            Debug.Log(SteamFriends.GetPersonaName());

            if (!GameServer.Init(0, 27020, 27015, EServerMode.eServerModeNoAuthentication, "0.0.1.0"))
            {
                // SteamGameServer.Init() failed, log an error and return
                Debug.LogError("SteamGameServer.Init() failed.");
                return;
            }

            SteamGameServer.SetModDir("OxygenNotIncluded");
            SteamGameServer.SetProduct("OxygenNotIncluded Multiplayer");
            SteamGameServer.SetGameDescription("OxygenNotIncluded Multiplayer");


            SteamGameServer.LogOnAnonymous();
            SteamNetworkingUtils.InitRelayNetworkAccess();
            SteamGameServer.SetAdvertiseServerActive(true);

            SteamGameServerNetworkingSockets.CreateListenSocketP2P(0, 0, null);
            _hNetPollGroup = SteamGameServerNetworkingSockets.CreatePollGroup();
            _isServerStarted = true;
        }

        void Update()
        {
            if (!_isServerStarted)
                return;
            // Run Steam client callbacks
            GameServer.RunCallbacks();
            SteamGameServerNetworkingSockets.RunCallbacks();
            ReceiveNetworkData();
        }

        void OnDestroy()
        {
            Debug.Log("Server.OnDestroy");
            GameServer.Shutdown();
            _isServerStarted = false;
        }

        private void Steam_HandleIncomingConnection(SteamNetConnectionStatusChangedCallback_t pCallback)
        {
            // Connection handle
            var hConn = pCallback.m_hConn;

            // Full connection info
            var info = pCallback.m_info;

            // Previous state.  (Current state is in m_info.m_eState)
            var eOldState = pCallback.m_eOldState;
            var steamID = pCallback.m_info.m_identityRemote.GetSteamID();

            // Check if a client has connected
            if (info.m_hListenSocket != HSteamListenSocket.Invalid &&
                eOldState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_None &&
                info.m_eState == ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting)
            {
                EResult res = SteamGameServerNetworkingSockets.AcceptConnection(hConn);
                if (res != EResult.k_EResultOK)
                {
                    Debug.Log($"AcceptConnection returned {res}");
                    SteamGameServerNetworkingSockets.CloseConnection(hConn,
                        (int)ESteamNetConnectionEnd.k_ESteamNetConnectionEnd_AppException_Generic,
                        "Failed to accept connection",
                        false);
                    return;
                }

                SteamGameServerNetworkingSockets.SetConnectionPollGroup(hConn, _hNetPollGroup);

                _clients.Add(steamID, hConn);
                Debug.Log($"Connection accepted from {steamID}");
                ClientJoined?.Invoke(steamID);
            }
            else if ((eOldState ==
                      ESteamNetworkingConnectionState.k_ESteamNetworkingConnectionState_Connecting ||
                      eOldState == ESteamNetworkingConnectionState
                          .k_ESteamNetworkingConnectionState_Connected) &&
                     info.m_eState == ESteamNetworkingConnectionState
                         .k_ESteamNetworkingConnectionState_ClosedByPeer)
            {
                // Handle disconnecting a client
                _clients.Remove(steamID);
                Debug.Log($"Connection closed from {steamID}");
            }
        }

        private void ReceiveNetworkData()
        {
            var messages = new IntPtr[128];
            var numMessages =
                SteamGameServerNetworkingSockets.ReceiveMessagesOnPollGroup(_hNetPollGroup, messages, 128);
            for (var idxMsg = 0; idxMsg < numMessages; idxMsg++)
            {
                var message = (SteamNetworkingMessage_t)((GCHandle)messages[idxMsg]).Target;
                var steamIDRemote = message.m_identityPeer.GetSteamID();
                var connection = message.m_conn;

                Debug.Log($"Received message from {steamIDRemote}");

                // TODO handle 
                // message.m_pData

                message.Release();
            }
        }
    }
}