using System.Text;

namespace AIControl;
public static class ModelMetadata
{
    public static long? ContextLimit(string path)
    {
        if (!File.Exists(path)) return null;
        using var reader = new BinaryReader(File.OpenRead(path),Encoding.UTF8);
        if (reader.ReadUInt32()!=0x46554747 || reader.ReadUInt32() is not (2 or 3)) throw new InvalidDataException("Invalid GGUF header.");
        reader.ReadUInt64(); var count=reader.ReadUInt64();if(count>100000)throw new InvalidDataException("Invalid GGUF metadata count.");
        string Str() { var length=reader.ReadUInt64(); if(length>32_000_000)throw new InvalidDataException("Invalid GGUF string.");return Encoding.UTF8.GetString(reader.ReadBytes(checked((int)length))); }
        object? Value(uint type, bool keep=false)
        {
            if(type==8)return Str();
            if(type==9){var subtype=reader.ReadUInt32();var n=reader.ReadUInt64();if(n>2_000_000 || subtype==9)throw new InvalidDataException("Invalid GGUF array.");for(ulong i=0;i<n;i++)Value(subtype);return null;}
            return type switch {0=>reader.ReadByte(),1=>reader.ReadSByte(),2=>reader.ReadUInt16(),3=>reader.ReadInt16(),4=>reader.ReadUInt32(),5=>reader.ReadInt32(),6=>reader.ReadSingle(),7=>reader.ReadBoolean(),10=>reader.ReadUInt64(),11=>reader.ReadInt64(),12=>reader.ReadDouble(),_=>throw new InvalidDataException("Invalid GGUF type.")};
        }
        for(ulong i=0;i<count;i++){var key=Str();var value=Value(reader.ReadUInt32());if(key.EndsWith(".context_length",StringComparison.Ordinal))return Convert.ToInt64(value);}
        return null;
    }
}
