using System;

namespace FusionFall_Mod.Core
{
    /// <summary>
    /// Заголовок файла assets версии 6.
    /// </summary>
    internal struct HeaderV6
    {
        public uint MetaSize;
        public uint FileSize;
        public uint Version;
        public uint DataOffset;
        public uint EndianTag;
    }
}

