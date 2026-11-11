using System.Security.Cryptography;
using System.Text;

namespace BusinessEntity.Core.Saving;

// Движковая генерация внутренних IDs одинакова на всех платформах и при повторе операции.
public static class StableOperationIdentity
{
    public static Guid Create(Guid operationId,string purpose)=>new(SHA256.HashData(Encoding.UTF8.GetBytes(operationId.ToString("N")+":"+purpose)).AsSpan(0,16));
}
