using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    public partial class Program : MyGridProgram
    {
        private const string BROADCAST_TAG = "TestAlign";
        
        private IMyRemoteControl _remoteControl;
        private IMyBroadcastListener _broadcastListener;

        public Program()
        {
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
            
            _remoteControl = this.GetLocalBlock<IMyRemoteControl>();
            if (_remoteControl == null)
            {
                Echo("Error: No remote control found!");
            }
            
            _broadcastListener = IGC.RegisterBroadcastListener(BROADCAST_TAG);
            _broadcastListener.SetMessageCallback(BROADCAST_TAG);
        }

        public void Save()
        {
        }

        public void Main(string argument, UpdateType updateSource)
        {
            if ((updateSource & UpdateType.IGC) > 0)
            {
                while (_broadcastListener.HasPendingMessage)
                {
                    var message = _broadcastListener.AcceptMessage();
                    if (message.Tag == BROADCAST_TAG && message.Data is string)
                    {
                        var data = message.Data.ToString();
                        if (data == "getalignmentmatrix")
                        {
                            HandleAlignmentMatrixRequest(message.Source);
                        }
                    }
                }
            }
            
            Echo("TestAlignTarget Ready");
            Echo($"Remote Control: {(_remoteControl != null ? _remoteControl.CustomName : "NOT FOUND")}");
        }

        private void HandleAlignmentMatrixRequest(long requesterId)
        {
            if (_remoteControl == null)
            {
                Echo("Cannot send matrix - no remote control!");
                return;
            }
            
            var matrix = _remoteControl.WorldMatrix;
            
            var response = $"alignmentmatrix|{matrix.M11}|{matrix.M12}|{matrix.M13}|{matrix.M14}|" +
                          $"{matrix.M21}|{matrix.M22}|{matrix.M23}|{matrix.M24}|" +
                          $"{matrix.M31}|{matrix.M32}|{matrix.M33}|{matrix.M34}|" +
                          $"{matrix.M41}|{matrix.M42}|{matrix.M43}|{matrix.M44}";
            
            IGC.SendUnicastMessage(requesterId, BROADCAST_TAG, response);
            Echo($"Sent alignment matrix to {requesterId}");
        }
    }
}
