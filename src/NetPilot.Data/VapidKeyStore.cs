using System.Security.Cryptography;
using LiteDB;
using Microsoft.AspNetCore.DataProtection;
using NetPilot.Core.Notifications;
using NetPilot.Data.Documents;

namespace NetPilot.Data;

public class VapidKeyStore : IVapidKeyStore
{
    private readonly ILiteCollection<VapidKeyDocument> _collection;
    private readonly IDataProtector _protector;

    public VapidKeyStore(NetPilotDatabase db, IDataProtectionProvider protectionProvider)
    {
        _collection = db.GetCollection<VapidKeyDocument>("vapid_keys");
        _protector = protectionProvider.CreateProtector("NetPilot.Notifications.VapidPrivateKey");
    }

    public Task<VapidKeys> GetOrCreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_collection.FindById("default") is null)
        {
            var candidate = Generate();
            try { _collection.Insert(candidate); }
            catch (LiteException ex) when (ex.ErrorCode == LiteException.INDEX_DUPLICATE_KEY) { }
        }
        // Insert is atomic across shared connections; always use the winning document.
        var doc = _collection.FindById("default");
        return Task.FromResult(new VapidKeys(doc.PublicKey, _protector.Unprotect(doc.ProtectedPrivateKey)));
    }

    private VapidKeyDocument Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(true);
        var publicPoint = new byte[65];
        publicPoint[0] = 4;
        parameters.Q.X!.CopyTo(publicPoint, 1);
        parameters.Q.Y!.CopyTo(publicPoint, 33);
        try
        {
            return new() { PublicKey = Base64Url(publicPoint), ProtectedPrivateKey = _protector.Protect(Base64Url(parameters.D!)) };
        }
        finally { CryptographicOperations.ZeroMemory(parameters.D!); }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
