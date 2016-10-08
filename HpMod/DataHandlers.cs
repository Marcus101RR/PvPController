﻿using System;
using System.IO;
using System.IO.Streams;
using System.Collections.Generic;
using System.Linq;
using Terraria;
using TShockAPI;
using System.Diagnostics;

namespace PvPController
{
    internal delegate bool GetDataHandlerDelegate(GetDataHandlerArgs args);

    internal class GetDataHandlerArgs : EventArgs
    {
        public TSPlayer Player { get; private set; }
        public MemoryStream Data { get; private set; }

        public GetDataHandlerArgs(TSPlayer player, MemoryStream data)
        {
            Player = player;
            Data = data;
        }
    }

    /* Contains the handlers for certain packets received by the server */
    internal static class GetDataHandlers
    {
        private static Dictionary<PacketTypes, GetDataHandlerDelegate> _getDataHandlerDelegates;

        /* Adds the handler functions as handlers for the given PacketType */
        public static void InitGetDataHandler()
        {
            _getDataHandlerDelegates = new Dictionary<PacketTypes, GetDataHandlerDelegate>
            {
                {PacketTypes.ProjectileNew, HandleProjectile},
                {PacketTypes.PlayerDamage, HandleDamage},
                {PacketTypes.PlayerUpdate, HandlePlayerUpdate},
            };
        }

        /* Checks if there is a handler for the given packet type and will return
         * whether or not the packet was handled
         * 
         * @param type
         *          The PacketType
         *          
         * @param player
         *          The player that sent the packet
         *          
         * @param data
         *          The packet data
         * 
         * @return
         *          Whether or not the packet was handled (and should therefore not be processed
         *          by anything else)
         */
        public static bool HandlerGetData(PacketTypes type, TSPlayer player, MemoryStream data)
        {
            GetDataHandlerDelegate handler;
            if (_getDataHandlerDelegates.TryGetValue(type, out handler))
            {
                try
                {
                    return handler(new GetDataHandlerArgs(player, data));
                }
                catch (Exception ex)
                {
                    TShock.Log.Error(ex.ToString());
                }
            }
            return false;
        }

        public static Stopwatch[] LastBannedUsage = new Stopwatch[256];
        public static DateTime[] LastMessage = new DateTime[256];

        /* Handles the case when the player shoots a banned projectile and
         * starts a stopwatch from the moment they use it, or reset
         * the existing stopwatch.
         * 
         * @param args
         *          The GetDataHandlerArgs containing the player that sent the packet and the data of the packet
         * 
         * @return
         *          Whether or not the packet was handled (and should therefore not be processed
         *          by anything else)
         */
        private static bool HandleProjectile(GetDataHandlerArgs args)
        {
            var ident = args.Data.ReadInt16();
            var pos = new Vector2(args.Data.ReadSingle(), args.Data.ReadSingle());
            var vel = new Vector2(args.Data.ReadSingle(), args.Data.ReadSingle());
            var knockback = args.Data.ReadSingle();
            var dmg = args.Data.ReadInt16();
            var owner = args.Data.ReadInt8();
            var type = args.Data.ReadInt16();
            var bits = (BitsByte)args.Data.ReadInt8();
            owner = (byte)args.Player.Index;
            float[] ai = new float[Projectile.maxAI];

            if (PvPController.Config.BannedProjectileIDs.Contains(type) && args.Player.TPlayer.hostile)
            {
                    var proj = Main.projectile[ident];
                    proj.active = false;
                    proj.type = 0;
                    if (PvPController.Config.HideDisallowedProjectiles)
                    {
                        TSPlayer.All.SendData(PacketTypes.ProjectileDestroy, "", ident);
                    }
                    proj.owner = 255;
                    proj.active = false;
                    proj.type = 0;
                    TSPlayer.All.SendData(PacketTypes.ProjectileNew, "", ident);
                    if (LastBannedUsage[args.Player.Index] != null)
                    {
                        LastBannedUsage[args.Player.Index].Stop();
                        LastBannedUsage[args.Player.Index].Reset();
                    }
                    else
                    {
                        LastBannedUsage[args.Player.Index] = new Stopwatch();
                    }
                    LastBannedUsage[args.Player.Index].Start();

                    if ((DateTime.Now - LastMessage[args.Player.Index]).TotalSeconds > 2) {
                        args.Player.SendMessage("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++", Color.Red);
                        args.Player.SendMessage("That projectile does not work in PVP. Using it will cause you to do no damage!", Color.Red);
                        args.Player.SendMessage("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++", Color.Red);
                        LastMessage[args.Player.Index] = DateTime.Now;
                    }
                    return true;
            }
            else if (PvPController.Config.WeaponBuff.Count(p => p.weaponID == args.Player.SelectedItem.netID) > 0 && args.Player.TPlayer.hostile)
            {
                var proj = new Projectile();
                proj.SetDefaults(type);
                
                if (proj.ranged && dmg > 0)
                {
                    var weaponBuffs = PvPController.Config.WeaponBuff.Where(p => p.weaponID == args.Player.SelectedItem.netID).ToList();
                    foreach (var weaponBuff in weaponBuffs)
                    {
                        args.Player.SetBuff(weaponBuff.debuffID, Convert.ToInt32((weaponBuff.immobiliseMilliseconds / 1000f) * 60), true);
                    }
                }
            }
            return false;
        }

        /* Handles the case when the player uses a banned item and
         * starts a stopwatch from the moment they use it, or reset
         * the existing stopwatch.
         * 
         * @param args
         *          The GetDataHandlerArgs containing the player that sent the packet and the data of the packet
         * 
         * @return
         *          Whether or not the packet was handled (and should therefore not be processed
         *          by anything else)
         */
        private static bool HandlePlayerUpdate(GetDataHandlerArgs args)
        {
            byte plr = args.Data.ReadInt8();
            BitsByte control = args.Data.ReadInt8();
            BitsByte pulley = args.Data.ReadInt8();
            byte item = args.Data.ReadInt8();
            var pos = new Vector2(args.Data.ReadSingle(), args.Data.ReadSingle());
            var vel = Vector2.Zero;
            
            if (control[5] && PvPController.Config.BannedItemIDs.Contains(args.Player.SelectedItem.netID) && args.Player.TPlayer.hostile)
            {
                if (LastBannedUsage[args.Player.Index] != null)
                {
                    LastBannedUsage[args.Player.Index].Stop();
                    LastBannedUsage[args.Player.Index].Reset();
                }
                else
                {
                    LastBannedUsage[args.Player.Index] = new Stopwatch();
                }

                LastBannedUsage[args.Player.Index].Start();

                if ((DateTime.Now - LastMessage[args.Player.Index]).TotalSeconds > 2)
                {
                    args.Player.SendMessage("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++", Color.Red);
                    args.Player.SendMessage("That weapon does not work in PVP. Using it will cause you to do no damage!", Color.Red);
                    args.Player.SendMessage("++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++++", Color.Red);
                    LastMessage[args.Player.Index] = DateTime.Now;
                }
            }

            return false;
        }
        
        /* Determines whether to block damage if they have recently used a banned item
         * or modifies damage if the projectile is on the modifications list.
         * 
         * @param args
         *          The GetDataHandlerArgs object containing the player who sent the packet
         *          and the data in it.
         * 
         * @return
         *          Whether or not the packet was handled (and should therefore not be processed
         *          by anything else)
         */
        private static bool HandleDamage(GetDataHandlerArgs args)
        {
			if (args.Player == null) return false;
			var index = args.Player.Index;
			var playerId = (byte) args.Data.ReadByte();
			var dir = args.Data.ReadByte();
			var damage = args.Data.ReadInt16();
            var text = args.Data.ReadString();
			var crit = args.Data.ReadBoolean();
			args.Data.ReadByte();

            if (TShock.Players[playerId] == null)
                return false;

            // Check whether the stopwatch is still valid, and if so send back the real HP to the client that damaged
            // the player.
            if (LastBannedUsage[args.Player.Index] != null && LastBannedUsage[args.Player.Index].IsRunning)
            {
                if (LastBannedUsage[args.Player.Index].Elapsed.TotalSeconds < PvPController.Config.DamageDisableSeconds)
                {
                    args.Player.SendData(PacketTypes.PlayerHp, "", playerId);
                    return true;
                }
                else
                {
                    LastBannedUsage[args.Player.Index].Stop();
                }
            }
            else
            {
                // Find out what projectile did the damage
                int currentIndex = 0;
                Projectile[] closeDamageProjectiles = new Projectile[255];
                for (int i = 0; i <= 255; i++)
                {
                    var projectile = Main.projectile[i];
                    if (projectile.active && projectile.owner == index)
                    {
                        if (Math.Abs(projectile.damage - damage) < 60)
                        {
                            closeDamageProjectiles[currentIndex++] = projectile;
                        }
                    }
                }

                float smallestDistance = -1;
                Projectile closestProjectile = null;

                for (int i = 0; i <= 255; i++)
                {
                    var projectile = closeDamageProjectiles[i];

                    // If there's no projectile object then it means we've reached the end of the array
                    // of valid items
                    if (projectile == null)
                    {
                        break;
                    }

                    if (projectile.active && projectile.owner == index)
                    {
                        float distance = Vector2.Distance(projectile.position, Main.player[playerId].position);
                        if (smallestDistance == -1 || distance < smallestDistance)
                        {
                            smallestDistance = distance;
                            closestProjectile = projectile;
                        }
                    }
                }

                // Given a valid projectile, apply modification if it exists
                if (closestProjectile != null)
                {
                    if (PvPController.Config.ProjectileModification.Count(p => p.projectileID == closestProjectile.type) > 0)
                    {
                        // Get then apply modification to damage
                        var modification = PvPController.Config.ProjectileModification.FirstOrDefault(p => p.projectileID == closestProjectile.type);
                        damage = Convert.ToInt16(damage*modification.damageRatio);

                        // Get damage dealt to display as purple combat text
                        int life = Main.player[playerId].statLife;
                        Main.player[playerId].Hurt(damage, 0, true);
                        int realDamage = life - Main.player[playerId].statLife;

                        /* Send out the HP value so that the client who now has the wrong value
                         * (due to health being update before the packet is sent) can use the right one */
                        args.Player.SendData(PacketTypes.PlayerHp, "", playerId);


                        // Send the combat text
                        var msgColor = new Color(162, 0, 255);
                        NetMessage.SendData((int)PacketTypes.CreateCombatText, index, -1, $"{realDamage}", (int)msgColor.PackedValue, Main.player[playerId].position.X, Main.player[playerId].position.Y - 32);

                        // Send the damage using the special method to avoid invincibility frames issue
                        SendPlayerDamage(TShock.Players[playerId], dir, damage);
                        return true;
                    }
                }
            }
            return false;
        }

        /* Sends a raw packet built from base values to both fix the invincibility frames
         * and provide a way to modify incoming damage.
         * 
         * @param player
         *          The TSPlayer object of the player to get hurt
         *          
         * @param hitDirection
         *          The hit direction (left or right, -1, 1)
         *          
         * @param damage
         *          The amount of damage to deal to the player
         */
        private static void SendPlayerDamage(TSPlayer player, int hitDirection, int damage)
        {
            // This flag permutation gives low invinc frames for proper client
            // sync
            BitsByte flags = new BitsByte();
            flags[0] = true; // PVP
            flags[1] = false; // Crit
            flags[2] = false; // Cooldown -1
            flags[3] = false; // Cooldown +1
            byte[] playerDamage = new PacketFactory()
                .SetType((short)PacketTypes.PlayerDamage)
                .PackByte((byte)player.Index)
                .PackByte((byte)hitDirection)
                .PackInt16((short)damage)
                .PackString("")
                .PackByte(flags)
                .GetByteData();
            player.SendRawData(playerDamage);
        }
    }
}