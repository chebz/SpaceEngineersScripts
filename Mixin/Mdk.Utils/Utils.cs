
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI.Ingame;

namespace IngameScript
{
    public static class Utils
    {
        public static bool GetLocalBlock<T>(this Program program, string name, out T block) where T : class, IMyTerminalBlock
        {
            var blocks = new List<T>();
            program.GridTerminalSystem.GetBlocksOfType(blocks);
            blocks = blocks.Where(b => b.IsSameConstructAs(program.Me)).ToList();
            
            block = blocks.FirstOrDefault(b => b.CustomName == name);
            return block != null;
        }

        public static List<T> GetLocalBlocks<T>(this Program program) where T : class, IMyTerminalBlock
        {
            var blocks = new List<T>();
            program.GridTerminalSystem.GetBlocksOfType(blocks);
            return blocks.Where(b => b.IsSameConstructAs(program.Me)).ToList();
        }

        public static List<T> GetLocalBlocks<T>(this Program program, string name) where T : class, IMyTerminalBlock
        {
            var blocks = new List<T>();
            program.GridTerminalSystem.GetBlocksOfType(blocks);
            blocks = blocks.Where(b => b.IsSameConstructAs(program.Me)).ToList();
            
            return blocks.Where(b => b.CustomName == name).ToList();
        }

        public static T GetLocalBlock<T>(this Program program, string name, int index = 0) where T : class, IMyTerminalBlock
        {
            var blocks = GetLocalBlocks<T>(program, name);
            return blocks.Count > index ? blocks[index] : null;
        }

        public static T GetLocalBlock<T>(this Program program, int index = 0) where T : class, IMyTerminalBlock
        {
            var blocks = GetLocalBlocks<T>(program);
            return blocks.Count > index ? blocks[index] : null;
        }

        public static List<T> GetLocalBlocksInGroup<T>(this Program program, string groupName) where T : class, IMyTerminalBlock
        {
            var group = program.GridTerminalSystem.GetBlockGroupWithName(groupName);
            if (group == null)
                return new List<T>();

            var blocks = new List<T>();
            group.GetBlocksOfType<T>(blocks);
            
            return blocks.Where(b => b.IsSameConstructAs(program.Me)).ToList();
        }
    }
}