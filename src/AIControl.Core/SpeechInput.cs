using System.Diagnostics;
using System.Text;

namespace AIControl;
public static class SpeechInput
{
    public static byte[] DecodeWav(string base64)
    {
        if (base64.Length > 5_200_000) throw new InvalidDataException("Recording exceeds two minutes.");
        byte[] data;
        try { data = Convert.FromBase64String(base64); } catch (FormatException) { throw new InvalidDataException("Malformed audio."); }
        if (data.Length < 44 || data.Length > 3_840_044 || Encoding.ASCII.GetString(data,0,4) != "RIFF" || Encoding.ASCII.GetString(data,8,4) != "WAVE"
            || Encoding.ASCII.GetString(data,12,4) != "fmt " || BitConverter.ToInt32(data,16) != 16 || BitConverter.ToInt16(data,20) != 1
            || BitConverter.ToInt16(data,22) != 1 || BitConverter.ToInt32(data,24) != 16000 || BitConverter.ToInt32(data,28) != 32000
            || BitConverter.ToInt16(data,32) != 2 || BitConverter.ToInt16(data,34) != 16 || Encoding.ASCII.GetString(data,36,4) != "data"
            || BitConverter.ToInt32(data,40) != data.Length - 44 || BitConverter.ToInt32(data,4) != data.Length - 8)
            throw new InvalidDataException("Expected mono 16 kHz PCM WAV audio.");
        return data;
    }
    public static async Task<string> TranscribeAsync(AssistantSettings settings, string base64, CancellationToken token)
    {
        var wav = DecodeWav(base64);
        var exe = Environment.ExpandEnvironmentVariables(settings.SpeechExecutable);
        var model = Environment.ExpandEnvironmentVariables(settings.SpeechModel);
        if (!File.Exists(exe) || !File.Exists(model)) throw new FileNotFoundException("Local Whisper runtime or speech model is missing. Check Settings.");
        var folder = Path.Combine(SettingsStore.DataDirectory,"speech-temp",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        var input = Path.Combine(folder,"recording.wav"); var output = Path.Combine(folder,"transcript");
        try
        {
            await File.WriteAllBytesAsync(input,wav,token);
            var info = new ProcessStartInfo(exe) { WorkingDirectory=Path.GetDirectoryName(exe), UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true };
            foreach (var arg in new[]{"-m",model,"-f",input,"-l","auto","-otxt","-of",output,"-ng","-np","-t","8"}) info.ArgumentList.Add(arg);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Whisper could not start.");
            var stdout=process.StandardOutput.ReadToEndAsync(token);var stderr=process.StandardError.ReadToEndAsync(token);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(5));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { if (!process.HasExited) process.Kill(true); throw; }
            await stdout; var error=await stderr;
            if (process.ExitCode!=0 || !File.Exists(output+".txt")) throw new InvalidDataException("Local transcription failed: "+error[..Math.Min(800,error.Length)]);
            return (await File.ReadAllTextAsync(output+".txt",token)).Trim();
        }
        finally { foreach(var file in Directory.EnumerateFiles(folder)) File.Delete(file);Directory.Delete(folder); }
    }
}
