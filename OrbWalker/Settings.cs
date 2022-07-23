using LowLevelInput.Hooks;
using Newtonsoft.Json;
using System;
using System.IO;

namespace OrbWalker
{
    public class Settings
    {
        public int ActivationKeyMinions { get; set; } = (int)VirtualKeyCode.C;
        public int ActivationKeyEnemies { get; set; } = (int)VirtualKeyCode.Space;
        public void CreateNew(string path)
        {
            using (StreamWriter sw = new StreamWriter(File.Create(path)))
            {
                sw.WriteLine("/* All Corresponding Key Bind Key Codes");
                foreach (int i in Enum.GetValues(typeof(VirtualKeyCode)))
                {
                    sw.WriteLine($"* \t{i} - {(VirtualKeyCode)i}");
                }
                sw.WriteLine("*/");
                sw.WriteLine(JsonConvert.SerializeObject(this));
            }
        }

        public void Load(string path)
        {
            ActivationKeyMinions = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)).ActivationKeyMinions;
            ActivationKeyEnemies = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(path)).ActivationKeyEnemies;
        }
    }
}
