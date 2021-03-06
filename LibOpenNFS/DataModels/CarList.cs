﻿using System.Collections.Generic;
using LibOpenNFS.Utils;

namespace LibOpenNFS.DataModels
{
    public class Car
    {
        public string IDOne { get; set; }

        public string IDTwo { get; set; }

        public string ModelPath { get; set; }

        public string Maker { get; set; }
        
        public uint NameHash { get; set; }
        
        public byte CarId { get; set; }
        
        public uint TypeHash { get; set; }
        
        public byte SkinsDisabled { get; set; }
        
        public uint ReflectionConfig { get; set; }
    }

    public class CarList : BaseModel
    {
        public CarList(ChunkID id, long size, long position) : base(id, size, position)
        {
            DebugUtil.EnsureCondition(
                id == ChunkID.BCHUNK_CARINFO_ARRAY,
                () => $"Expected BCHUNK_CARINFO_ARRAY, got {id.ToString()}");
        }

        public List<Car> Cars { get; } = new List<Car>();
    }
}
