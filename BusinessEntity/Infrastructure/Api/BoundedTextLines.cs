using System.Runtime.CompilerServices;
using System.Text;

namespace BusinessEntity.Infrastructure.Api;

public static class BoundedTextLines
{
    // Даже один вход без переводов строки не создаёт строку размером с книгу.
    public static async IAsyncEnumerable<string> ReadAsync(TextReader reader,int maximum,[EnumeratorCancellation]CancellationToken ct=default)
    {
        var buffer=new char[8192];var line=new StringBuilder(Math.Min(maximum,8192));
        while(true)
        {
            var count=await reader.ReadAsync(buffer.AsMemory(),ct);if(count==0)break;
            for(var i=0;i<count;i++)
            {
                var c=buffer[i];if(c=='\n'){yield return line.ToString().TrimEnd('\r');line.Clear();}
                else{line.Append(c);if(line.Length>maximum)throw new InvalidDataException("Строка технического потока превышает предел.");}
            }
        }
        if(line.Length>0)yield return line.ToString().TrimEnd('\r');
    }
}
