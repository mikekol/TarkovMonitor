using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TarkovMonitor
{
    internal class TarkovDevRepository
    {
        public List<TarkovDev.Task> Tasks;
        public List<Map> Maps;
        public List<TarkovDev.Item> Items;

        public TarkovDevRepository()
        {
            Tasks = new List<TarkovDev.Task>();
            Maps = new List<Map>();
            Items = new List<TarkovDev.Item>();
        }
    }
}
