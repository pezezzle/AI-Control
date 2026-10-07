using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIControl;

public static class WriteBackups
{
    public static string DirectoryPath => Path.Combine(SettingsStore.DataDirectory,"write-backups");
    public static string Save(string workspace,string relative,string source,string expected,string? directory=null)
    {
        directory ??= DirectoryPath;
        var record=Path.Combine(directory,Guid.NewGuid().ToString("N"));
        var originals=Path.Combine(record,"original");Directory.CreateDirectory(originals);
        var original=Path.Combine(originals,Path.GetFileName(source));File.Copy(source,original);
        using(var reader=new StreamReader(original,new UTF8Encoding(false,true),true))
            if(reader.ReadToEnd()!=expected)throw new InvalidDataException("The file changed while its backup was being saved. Inspect it again before retrying.");
        var metadata=new{Workspace=workspace,RelativePath=relative,Created=DateTimeOffset.UtcNow,Sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)))};
        File.WriteAllText(Path.Combine(record,"metadata.json"),JsonSerializer.Serialize(metadata,SettingsStore.Json),new UTF8Encoding(false));
        return original;
    }
}
