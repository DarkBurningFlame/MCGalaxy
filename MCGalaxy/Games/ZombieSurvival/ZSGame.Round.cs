﻿/*
    Copyright 2010 MCLawl Team -
    Created by Snowl (David D.) and Cazzar (Cayde D.)

    Dual-licensed under the Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.osedu.org/licenses/ECL-2.0
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MCGalaxy.Commands.World;
using MCGalaxy.Games.ZS;
using MCGalaxy.Network;

namespace MCGalaxy.Games {
    
    public sealed partial class ZSGame : RoundsGame {
        string lastKiller = "";
        int infectCombo = 0;
        
        protected override void DoRound() {
            if (!Running) return;
            List<Player> players = DoRoundCountdown(30);
            if (players == null) return;

            if (!Running) return;
            RoundInProgress = true;
            StartRound(players);
            if (!Running) return;
            DoCoreGame();
            
            if (Running) EndRound();
            if (Running) VoteAndMoveToNextMap();
        }
        
        void StartRound(List<Player> players) {
            Random random = new Random();
            int roundMins = random.Next(Map.Config.MinRoundTime, Map.Config.MaxRoundTime);
            string suffix = roundMins == 1 ? " %Sminute!" : " %Sminutes!";
            Map.Message("This round will last for &a" + roundMins + suffix);
            RoundEnd = DateTime.UtcNow.AddMinutes(roundMins);
            
            Player[] online = PlayerInfo.Online.Items;
            foreach (Player p in online) {
                if (p.level == null || p.level != Map || p.Game.Referee) continue;
                Alive.Add(p);
            }
            Infected.Clear();

            Player first = null;
            do {
                first = QueuedZombie != null ? PlayerInfo.FindExact(QueuedZombie) : players[random.Next(players.Count)];
                QueuedZombie = null;
            } while (first == null || first.level != Map);
            
            Map.Message("&c" + first.DisplayName + " %Sstarted the infection!");
            InfectPlayer(first, null);
        }
        
        void DoCoreGame() {
            Player[] alive = Alive.Items;
            string lastTimeLeft = null;
            int lastCountdown = -1;
            Random random = new Random();
            
            while (alive.Length > 0 && Running && RoundInProgress) {
                Player[] infected = Infected.Items;
                // Do round end.
                int seconds = (int)(RoundEnd - DateTime.UtcNow).TotalSeconds;
                if (seconds <= 0) {
                    SendLevelRaw("", true);
                    EndRound();
                    return;
                }
                if (seconds <= 5 && seconds != lastCountdown) {
                    string suffix = seconds == 1 ? " &4second" : " &4seconds";
                    SendLevelRaw("&4Ending in &f" + seconds + suffix, true);
                    lastCountdown = seconds;
                }
                
                // Update the round time left shown in the top right
                string timeLeft = HUD.GetTimeLeft(seconds);
                if (lastTimeLeft != timeLeft) {
                    HUD.UpdateAllPrimary(this);
                    lastTimeLeft = timeLeft;
                }
                
                DoCollisions(alive, infected, random);
                CheckInvisibilityTime();
                Thread.Sleep(200);
                alive = Alive.Items;
            }
        }
        
        void DoCollisions(Player[] aliveList, Player[] deadList, Random random) {
            int dist = ZSConfig.HitboxPrecision;
            foreach (Player killer in deadList) {
                ZSData killerData = Get(killer);
                killerData.Infected = true;
                aliveList = Alive.Items;

                foreach (Player alive in aliveList) {
                    if (alive == killer) continue;
                    if (!MovementCheck.InRange(alive, killer, dist)) continue;
                    
                    if (killerData.Infected && !Get(alive).Infected
                        && !alive.Game.Referee && !killer.Game.Referee
                        && killer.level == Map && alive.level == Map)
                    {
                        InfectPlayer(alive, killer);
                        
                        if (lastKiller == killer.name) {
                            infectCombo++;
                            if (infectCombo >= 2) {
                                killer.SendMessage("You gained " + (2 + infectCombo) + " " + ServerConfig.Currency);
                                killer.SetMoney(killer.money + (2 + infectCombo));
                                Map.Message("&c" + killer.DisplayName + " %Sis on a rampage! " + (infectCombo + 1) + " infections in a row!");
                            }
                        } else {
                            infectCombo = 0;
                        }
                        
                        lastKiller = killer.name;
                        killerData.CurrentInfected++;
                        killerData.TotalInfected++;
                        killerData.MaxInfected = Math.Max(killerData.CurrentInfected, killerData.MaxInfected);
                        
                        ShowInfectMessage(random, alive, killer);
                        Thread.Sleep(50);
                    }
                }
            }
        }
        
        void SendLevelRaw(string message, bool announce = false) {
            Player[] players = PlayerInfo.Online.Items;
            foreach (Player p in players) {
                if (p.level != Map) continue;
                CpeMessageType type = announce && p.Supports(CpeExt.MessageTypes)
                    ? CpeMessageType.Announcement : CpeMessageType.Normal;
                
                p.Send(Packet.Message(message, type, p.hasCP437));
            }
        }
        
        void CheckInvisibilityTime() {
            DateTime now = DateTime.UtcNow;
            Player[] players = PlayerInfo.Online.Items;
            foreach (Player p in players) {
                if (p.level != Map) continue;
                ZSData data = Get(p);
                if (!data.Invisible) continue;
                
                DateTime end = data.InvisibilityEnd;
                if (now >= end) {
                    Player.Message(p, "&cYou are &bvisible &cagain");
                    ResetInvisibility(p, data);
                    continue;
                }
                
                int left = (int)Math.Ceiling((end - now).TotalSeconds);
                if (left == data.InvisibilityTime) continue;
                data.InvisibilityTime = left;
                
                string msg = "&bInvisibility for &a" + left;
                if (p.Supports(CpeExt.MessageTypes)) {
                    p.SendCpeMessage(CpeMessageType.BottomRight2, msg);
                } else {
                    Player.Message(p, msg);
                }
            }
        }
        
        void CheckHumanPledge(Player p, Player killer) {
            if (!p.Game.PledgeSurvive) return;
            p.Game.PledgeSurvive = false;
            Map.Message("&c" + p.DisplayName + " %Sbroke their pledge of not being infected.");
            
            if (killer == null) {
                Player.Message(p, "As this was an automatic infection, you have not lost any &3" + ServerConfig.Currency);
            } else {
                p.SetMoney(Math.Max(p.money - 2, 0));
            }
        }
        
        void CheckBounty(Player p, Player pKiller) {
            BountyData bounty = FindBounty(p.name);
            if (bounty == null) return;
            Bounties.Remove(bounty);
            
            Player setter = PlayerInfo.FindExact(bounty.Origin);
            if (pKiller == null) {
                Map.Message("Bounty on " + p.ColoredName + " %Sis no longer active");
                if (setter != null) setter.SetMoney(setter.money + bounty.Amount);
            } else if (setter == null) {
                Player.Message(pKiller, "Cannot collect the bounty, as the player who set it is offline.");
            } else {
                Map.Message("&c" + pKiller.DisplayName + " %Scollected the bounty of &a" +
                              bounty.Amount + " %S" + ServerConfig.Currency + " on " + p.ColoredName);
                pKiller.SetMoney(pKiller.money + bounty.Amount);
            }
        }
        
        void ShowInfectMessage(Random random, Player pAlive, Player pKiller) {
            string text = null;
            List<string> infectMsgs = Get(pKiller).InfectMessages;
            
            if (infectMsgs != null && infectMsgs.Count > 0 && random.Next(0, 10) < 5) {
                text = infectMsgs[random.Next(infectMsgs.Count)];
            } else {
                text = infectMessages[random.Next(infectMessages.Count)];
            }
            
            Map.Message(string.Format(text,
                                        "&c" + pKiller.DisplayName + "%S",
                                        pAlive.ColoredName + "%S"));
        }
        
        protected override bool SetMap(string map) {
            bool success = base.SetMap(map);
            if (success && ZSConfig.SetMainLevel) Server.mainLevel = Map;
            return success;
        }

        internal static void RespawnPlayer(Player p) {
            Entities.GlobalRespawn(p, false);
            TabList.Add(p, p, Entities.SelfID);
        }

        public override void EndRound() {
            if (!RoundInProgress) return;
            RoundInProgress = false;
            RoundStart = DateTime.MinValue;
            RoundEnd = DateTime.MinValue;
            HUD.UpdateAllPrimary(this);
            
            if (!Running) return;
            Player[] alive = Alive.Items, dead = Infected.Items;
            Map.Message("&aThe game has ended!");
            
            if (alive.Length == 0) Map.Message("&4Zombies have won this round.");
            else if (alive.Length == 1) Map.Message("&2Congratulations to the sole survivor:");
            else Map.Message("&2Congratulations to the survivors:");
            AnnounceWinners(alive, dead);
            
            Map.Config.RoundsPlayed++;
            if (alive.Length > 0) {
                Map.Config.RoundsHumanWon++;
                foreach (Player p in alive) { IncreaseAliveStats(p); }
            }
            
            GiveMoney(alive);
            Level.SaveSettings(Map);
        }

        void AnnounceWinners(Player[] alive, Player[] dead) {
            if (alive.Length > 0) {
                Map.Message(alive.Join(p => p.ColoredName)); return;
            }
            
            int maxKills = 0, count = 0;
            for (int i = 0; i < dead.Length; i++) {
                maxKills = Math.Max(maxKills, Get(dead[i]).CurrentInfected);
            }
            for (int i = 0; i < dead.Length; i++) {
                if (Get(dead[i]).CurrentInfected == maxKills) count++;
            }
            
            string group = count == 1 ? " zombie " : " zombies ";
            string suffix = maxKills == 1 ? " %Skill" : " %Skills";
            StringFormatter<Player> formatter = p => Get(p).CurrentInfected == maxKills ? p.ColoredName : null;
            Map.Message("&8Best" + group + "%S(&b" + maxKills + suffix + "%S)&8: " + dead.Join(formatter));
        }

        void IncreaseAliveStats(Player p) {
            if (p.Game.PledgeSurvive) {
                Player.Message(p, "You received &a5 &3" + ServerConfig.Currency +
                               " %Sfor successfully pledging that you would survive.");
                p.SetMoney(p.money + 5);
            }
            
            ZSData data = Get(p);
            data.CurrentRoundsSurvived++;
            data.TotalRoundsSurvived++;
            data.MaxRoundsSurvived = Math.Max(data.CurrentRoundsSurvived, data.MaxRoundsSurvived);
            p.SetPrefix(); // stars before name
        }

        void GiveMoney(Player[] alive) {
            Player[] online = PlayerInfo.Online.Items;
            Random rand = new Random();
            
            foreach (Player pl in online) {
                if (pl.level != Map) continue;
                ZSData data = Get(pl);
                data.ResetInvisibility();
                int reward = GetMoneyReward(pl, data, alive, rand);
                
                if (reward == -1) {
                    pl.SendMessage("You may not hide inside a block! No " + ServerConfig.Currency + " for you."); reward = 0;
                } else if (reward > 0) {
                    pl.SendMessage(Colors.gold + "You gained " + reward + " " + ServerConfig.Currency);
                }
                
                pl.SetMoney(pl.money + reward);
                data.ResetState();
                pl.Game.PledgeSurvive = false;
                
                if (pl.Game.Referee) {
                    pl.SendMessage("You gained one " + ServerConfig.Currency + " because you're a ref. Would you like a medal as well?");
                    pl.SetMoney(pl.money + 1);
                }
                
                ZSGame.RespawnPlayer(pl);
                HUD.UpdateTertiary(pl, data.Infected);
            }
        }

        static int GetMoneyReward(Player pl, ZSData data, Player[] alive, Random rand) {
            if (pl.CheckIfInsideBlock()) return -1;
            
            if (alive.Length == 0) {
                return rand.Next(1 + data.CurrentInfected, 5 + data.CurrentInfected);
            } else if (alive.Length == 1 && !data.Infected) {
                return rand.Next(5, 10);
            } else if (alive.Length > 1 && !data.Infected) {
                return rand.Next(2, 6);
            }
            return 0;
        }
    }
}
