﻿/*
    Copyright 2015 MCGalaxy
    Original level physics copyright 2010 MCSharp team (Modified for use with MCZall/MCLawl/MCGalaxy)
        
    Dual-licensed under the    Educational Community License, Version 2.0 and
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

namespace MCGalaxy.BlockPhysics {

    public static class OtherPhysics {
        
        public static bool DoLeafDecay(Level lvl, Check C) {
            const int dist = 4;
            ushort x, y, z;
            lvl.IntToPos(C.b, out x, out y, out z);

            for (int xx = -dist; xx <= dist; xx++)
                for (int yy = -dist; yy <= dist; yy++)
                    for (int zz = -dist; zz <= dist; zz++)
            {
                int index = lvl.PosToInt((ushort)(x + xx), (ushort)(y + yy), (ushort)(z + zz));
                if (index < 0) continue;
                byte type = lvl.blocks[index];
                
                if (type == Block.trunk)
                    lvl.leaves[index] = 0;
                else if (type == Block.leaf)
                    lvl.leaves[index] = -2;
                else
                    lvl.leaves[index] = -1;
            }

            for (int i = 1; i <= dist; i++)
                for (int xx = -dist; xx <= dist; xx++)
                    for (int yy = -dist; yy <= dist; yy++)
                        for (int zz = -dist; zz <= dist; zz++)
            {
                int index = lvl.PosToInt((ushort)(x + xx), (ushort)(y + yy), (ushort)(z + zz));
                if (index < 0) continue;
                
                if (lvl.leaves[index] == i - 1) {
                    CheckLeaf(lvl, i, x + xx - 1, y + yy, z + zz);
                    CheckLeaf(lvl, i, x + xx + 1, y + yy, z + zz);
                    CheckLeaf(lvl, i, x + xx, y + yy - 1, z + zz);
                    CheckLeaf(lvl, i, x + xx, y + yy + 1, z + zz);
                    CheckLeaf(lvl, i, x + xx, y + yy, z + zz - 1);
                    CheckLeaf(lvl, i, x + xx, y + yy, z + zz + 1);
                }
            }
            return lvl.leaves[C.b] < 0;
        }
        
        static void CheckLeaf(Level lvl, int i, int x, int y, int z) {
            int index = lvl.PosToInt((ushort)x, (ushort)y, (ushort)z);
            if (index < 0) return;
            
            sbyte type;
            if (lvl.leaves.TryGetValue(index, out type) && type == -2)
                lvl.leaves[index] = (sbyte)i;
        }
        
        public static void DoFalling(Level lvl, Check C, byte type) {
            if (lvl.physics == 0 || lvl.physics == 5) { C.time = 255; return; }
            ushort x, y, z;
            lvl.IntToPos(C.b, out x, out y, out z);
            int index = C.b;
            bool movedDown = false;
            
            do {
                index = lvl.IntOffset(index, 0, -1, 0); //Get block below each loop
                if (lvl.GetTile(index) == Block.Zero) break;
                bool hitBlock = false;
                
                switch (lvl.blocks[index]) {
                    case Block.air:
                    case Block.water:
                    case Block.lava:
                        movedDown = true;
                        break;
                        //Adv physics crushes plants with sand
                    case Block.shrub:
                    case Block.yellowflower:
                    case Block.redflower:
                    case Block.mushroom:
                    case Block.redmushroom:
                        if (lvl.physics > 1) movedDown = true;
                        break;
                    default:
                        hitBlock = true;
                        break;
                }
                if (hitBlock || lvl.physics > 1) break;
            } while (true);

            if (movedDown) {
                lvl.AddUpdate(C.b, Block.air);
                if (lvl.physics > 1)
                    lvl.AddUpdate(index, type);
                else
                    lvl.AddUpdate(lvl.IntOffset(index, 0, 1, 0), type);
                
                AirPhysics.PhysAir(lvl, lvl.PosToInt((ushort)(x + 1), y, z));
                AirPhysics.PhysAir(lvl, lvl.PosToInt((ushort)(x - 1), y, z));
                AirPhysics.PhysAir(lvl, lvl.PosToInt(x, y, (ushort)(z + 1)));
                AirPhysics.PhysAir(lvl, lvl.PosToInt(x, y, (ushort)(z - 1)));
                AirPhysics.PhysAir(lvl, lvl.PosToInt(x, (ushort)(y + 1), z));
            }
            C.time = 255;
        }

        public static void DoStairs(Level lvl, Check C) {
            int bBelow = lvl.IntOffset(C.b, 0, -1, 0);
            byte tile = lvl.GetTile(bBelow);
            
            if (tile == Block.staircasestep) {
                lvl.AddUpdate(C.b, Block.air);
                lvl.AddUpdate(bBelow, Block.staircasefull);
            } else if (tile == Block.cobblestoneslab) {
                lvl.AddUpdate(C.b, Block.air);
                lvl.AddUpdate(bBelow, Block.stone);
            }
            C.time = 255;
        }
        
        public static void DoFloatwood(Level lvl, Check C) {
            int index = lvl.IntOffset(C.b, 0, -1, 0);
            if (lvl.GetTile(index) == Block.air) {
                lvl.AddUpdate(C.b, Block.air);
                lvl.AddUpdate(index, Block.wood_float);
            } else {
                index = lvl.IntOffset(C.b, 0, 1, 0);
                if (Block.Convert(lvl.GetTile(index)) == Block.water) {
                    lvl.AddUpdate(C.b, lvl.blocks[index]);
                    lvl.AddUpdate(index, Block.wood_float);
                }
            }
            C.time = 255;
        }
    }
}
