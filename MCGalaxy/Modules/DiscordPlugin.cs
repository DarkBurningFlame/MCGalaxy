﻿/*
    Copyright 2015 MCGalaxy
        
    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using MCGalaxy.Config;
using MCGalaxy.Network;
using MCGalaxy.Tasks;

namespace MCGalaxy.Modules.Discord {

    public static class DiscordConfig {
        [ConfigString("bot-token", null, "", true)]
        public static string BotToken = "";
        [ConfigString("read-channel-ids", null, "", true)]
        public static string ReadChannels = "";
        [ConfigString("send-channel-ids", null, "", true)]
        public static string SendChannels = "";
        
        [ConfigBool("enabled", null, false)]
        public static bool Enabled;
        
        const string file = "properties/discordbot.properties";
        static ConfigElement[] cfg;
        
        public static void Load() {
            // create default config file
            if (!File.Exists(file)) Save();

            if (cfg == null) cfg = ConfigElement.GetAll(typeof(DiscordConfig));
            ConfigElement.ParseFile(cfg, file, null);
        }
        
        public static void Save() {
            if (cfg == null) cfg = ConfigElement.GetAll(typeof(DiscordConfig));
            ConfigElement.SerialiseSimple(cfg, file, null);
        }
    }
    
    public sealed class DiscordWebsocket : ClientWebSocket {
        public string Token;
        public Action<string> Handler;
        TcpClient client;
        SslStream stream;
        
        public DiscordWebsocket() {
            path = "/?v=6&encoding=json";
        }
        
        const string host = "gateway.discord.gg";
        // stubs
        public override bool LowLatency { set { } }
        public override string IP { get { return ""; } }
        
        public void Connect() {
            client = new TcpClient();
            client.Connect(host, 443);

            stream   = HttpUtil.WrapSSLStream(client.GetStream(), host);
            protocol = this;
            Init();
        }
        
        public void SendMessage(int opcode, JsonObject data) {
            JsonObject obj = new JsonObject();
            obj["op"] = opcode;
            obj["d"]  = data;
            SendMessage(obj);
        }
        
        public void SendMessage(JsonObject obj) {
            StringWriter dst  = new StringWriter();
            JsonWriter   w    = new JsonWriter(dst);
            w.SerialiseObject = raw => JsonSerialisers.WriteObject(w, raw);
            w.WriteObject(obj);
            
            string str = dst.ToString();
            SendRaw(Encoding.UTF8.GetBytes(str), SendFlags.None);
        }
        
        public void ReadLoop() {
            byte[] data = new byte[4096];
            for (;;) {
                int len = stream.Read(data, 0, 4096);
                if (len == 0) break; // disconnected
                HandleReceived(data, len);
            }
        }
        
        protected override void HandleData(byte[] data, int len) {
            string value = Encoding.UTF8.GetString(data, 0, len);
            Handler(value);
        }
        
        protected override void SendRaw(byte[] data, SendFlags flags) {
            stream.Write(data);
        }
        
        public override void Close() {
            client.Close();
        }
        
        protected override void Disconnect(int reason) {
            base.Disconnect(reason);
            Close();
        }
        
        protected override void WriteCustomHeaders() {
            WriteHeader("Authorization: Bot " + Token);
            WriteHeader("Host: " + host);
        }
    }
    
    public sealed class DiscordBot {
        Thread thread;
        DiscordWebsocket socket;
        string lastSequence;
        
        const int OPCODE_DISPATCH_EVENT  = 0;
        const int OPCODE_HEARTBEAT       = 1;
        const int OPCODE_IDENTIFY        = 2;
        const int OPCODE_STATUS_UPDATE   = 3;
        const int OPCODE_VOICE_STATE_UPDATE = 4;
        const int OPCODE_RESUME          = 6;
        const int OPCODE_REQUEST_SERVER_MEMBERS = 8;
        const int OPCODE_INVALID_SESSION = 9;
        const int OPCODE_HELLO           = 10;
        const int OPCODE_HEARTBEAT_ACK   = 11;
        
        void HandlePacket(string value) {
            JsonReader ctx = new JsonReader(value);
            JsonObject obj = (JsonObject)ctx.Parse();
            
            Logger.Log(LogType.SystemActivity, value);
            if (obj == null) return;
            
            int opcode = int.Parse((string)obj["op"]);
            DispatchPacket(opcode, obj);
        }
        
        void DispatchPacket(int opcode, JsonObject obj) {
            if (opcode == OPCODE_HELLO) HandleHello(obj);
        }
        
        
        void HandleHello(JsonObject obj) {
            JsonObject data = (JsonObject)obj["d"];
            string interval = (string)data["heartbeat_interval"];            
            int msInterval  = int.Parse(interval);
            
            Server.Background.QueueRepeat(SendHeartbeat, null, 
                                          TimeSpan.FromMilliseconds(msInterval));
            SendIdentify();
        }
        
        
        void SendHeartbeat(SchedulerTask task) {
            JsonObject obj = new JsonObject();
            obj["op"] = OPCODE_HEARTBEAT;
            
            if (lastSequence != null) {
                obj["d"] = int.Parse(lastSequence);
            } else {
                obj["d"] = null;
            }
            socket.SendMessage(obj);
        }
        
        void SendIdentify() {
        	JsonObject data = new JsonObject();
        	
        	JsonObject props = new JsonObject();
        	props["$os"] = "linux";
        	props["$browser"] = "MCGRelayBot";
        	props["$device"]  = "MCGRelayBot";
        	
        	data["token"]   = socket.Token;
        	data["intents"] = 1 << 9; // GUILD_MESSAGES
        	data["properties"] = props;
        	socket.SendMessage(OPCODE_IDENTIFY, data);
        }
        
        public void RunAsync() {
            socket = new DiscordWebsocket();
            socket.Token   = DiscordConfig.BotToken;
            socket.Handler = HandlePacket;
                
            thread      = new Thread(IOThread);
            thread.Name = "MCG-DiscordRelay";
            thread.IsBackground = true;
            thread.Start();
        }
        
        void IOThread() {
            try {
                socket.Connect();
                socket.ReadLoop();
            } catch (Exception ex) {
                Logger.LogError("Discord relay error", ex);
            }
        }
    }
    
    public sealed class DiscordPlugin : Plugin {
        public override string creator { get { return Server.SoftwareName + " team"; } }
        public override string MCGalaxy_Version { get { return Server.Version; } }
        public override string name { get { return "DiscordRelayPlugin"; } }
        DiscordBot bot;
        
        public override void Load(bool startup) {
            DiscordConfig.Load();
            if (!DiscordConfig.Enabled) return;
            
            bot = new DiscordBot();
            bot.RunAsync();
        }
        
        public override void Unload(bool shutdown) {

        }
    }
}
